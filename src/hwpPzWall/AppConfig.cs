using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HwpPzWall;

/// <summary>한 모니터의 네 변에 지정된 금지 폭(물리 픽셀).</summary>
internal sealed class EdgeZones
{
    public int Left { get; set; }
    public int Right { get; set; }
    public int Top { get; set; }
    public int Bottom { get; set; }

    [JsonIgnore]
    public bool HasAny => Left > 0 || Right > 0 || Top > 0 || Bottom > 0;

    public EdgeZones Clone() => new() { Left = Left, Right = Right, Top = Top, Bottom = Bottom };
}

/// <summary>모니터 하나에 대한 금지 영역 설정. 모니터 식별은 장치명 → 좌표 → 번호 순으로 시도한다.</summary>
internal sealed class MonitorZone
{
    public string DeviceName { get; set; } = "";
    public int DisplayIndex { get; set; }
    public int BoundsX { get; set; }
    public int BoundsY { get; set; }
    public int BoundsWidth { get; set; }
    public int BoundsHeight { get; set; }
    public EdgeZones Edges { get; set; } = new();

    [JsonIgnore]
    public Rectangle SavedBounds => new(BoundsX, BoundsY, BoundsWidth, BoundsHeight);

    public void SetBounds(Rectangle r)
    {
        BoundsX = r.X; BoundsY = r.Y; BoundsWidth = r.Width; BoundsHeight = r.Height;
    }
}

/// <summary>커서가 금지 영역에 닿았을 때의 동작.</summary>
internal enum BlockMode
{
    /// <summary>금지 영역을 뛰어넘어 반대편으로 이동 — 모니터 간 이동은 그대로 된다. (권장)</summary>
    JumpOver = 0,
    /// <summary>벽처럼 완전히 막는다 — 해당 방향으로 더 못 간다.</summary>
    HardWall = 1,
}

internal sealed class AppConfig
{
    public List<MonitorZone> Zones { get; set; } = new();

    public uint HotkeyModifiers { get; set; } = Native.MOD_CONTROL | Native.MOD_ALT;
    public uint HotkeyVirtualKey { get; set; } = (uint)Keys.F9;

    public BlockMode Mode { get; set; } = BlockMode.JumpOver;

    public bool AutoStartWithWindows { get; set; }
    public bool EnableBlockingOnLaunch { get; set; }
    public bool ShowNotifications { get; set; } = true;

    // ---------- 저장/불러오기 ----------

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hwpPzWall");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (cfg != null)
                {
                    cfg.Zones ??= new List<MonitorZone>();
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"설정 불러오기 실패: {ex.Message}");
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Write($"설정 저장 실패: {ex.Message}");
            MessageBox.Show($"설정을 저장하지 못했습니다.\n\n{ex.Message}", "hwpPzWall",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public AppConfig Clone()
    {
        var copy = (AppConfig)MemberwiseClone();
        copy.Zones = Zones.Select(z => new MonitorZone
        {
            DeviceName = z.DeviceName,
            DisplayIndex = z.DisplayIndex,
            BoundsX = z.BoundsX,
            BoundsY = z.BoundsY,
            BoundsWidth = z.BoundsWidth,
            BoundsHeight = z.BoundsHeight,
            Edges = z.Edges.Clone(),
        }).ToList();
        return copy;
    }

    // ---------- 실제 차단 사각형 계산 ----------

    /// <summary>현재 연결된 모니터 배치에 맞춰 금지 사각형 목록을 만든다.</summary>
    public List<Rectangle> BuildZoneRects(IReadOnlyList<MonitorInfo> monitors)
    {
        var rects = new List<Rectangle>();
        foreach (var z in Zones)
        {
            if (!z.Edges.HasAny) continue;
            var mon = ResolveMonitor(z, monitors);
            if (mon == null) continue;

            var b = mon.Bounds;
            var e = z.Edges;

            if (e.Left > 0)
                rects.Add(new Rectangle(b.Left, b.Top, Math.Min(e.Left, b.Width), b.Height));
            if (e.Right > 0)
            {
                var w = Math.Min(e.Right, b.Width);
                rects.Add(new Rectangle(b.Right - w, b.Top, w, b.Height));
            }
            if (e.Top > 0)
                rects.Add(new Rectangle(b.Left, b.Top, b.Width, Math.Min(e.Top, b.Height)));
            if (e.Bottom > 0)
            {
                var h = Math.Min(e.Bottom, b.Height);
                rects.Add(new Rectangle(b.Left, b.Bottom - h, b.Width, h));
            }
        }
        return rects;
    }

    /// <summary>저장된 설정이 지금의 어느 모니터에 해당하는지 찾는다.</summary>
    public static MonitorInfo? ResolveMonitor(MonitorZone zone, IReadOnlyList<MonitorInfo> monitors)
    {
        var byDevice = monitors.FirstOrDefault(m =>
            !string.IsNullOrEmpty(zone.DeviceName) &&
            string.Equals(m.DeviceName, zone.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (byDevice != null) return byDevice;

        var byBounds = monitors.FirstOrDefault(m => m.Bounds == zone.SavedBounds);
        if (byBounds != null) return byBounds;

        return monitors.FirstOrDefault(m => m.DisplayIndex == zone.DisplayIndex);
    }

    public EdgeZones GetEdges(MonitorInfo monitor)
    {
        var z = Zones.FirstOrDefault(z => ResolveMonitor(z, new[] { monitor }) == monitor);
        return z?.Edges ?? new EdgeZones();
    }

    /// <summary>해당 모니터의 설정을 갱신한다. 값이 모두 0이면 항목을 제거한다.</summary>
    public void SetEdges(MonitorInfo monitor, EdgeZones edges)
    {
        var existing = Zones.FirstOrDefault(z =>
            string.Equals(z.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase));

        if (!edges.HasAny)
        {
            if (existing != null) Zones.Remove(existing);
            return;
        }

        if (existing == null)
        {
            existing = new MonitorZone { DeviceName = monitor.DeviceName };
            Zones.Add(existing);
        }
        existing.DisplayIndex = monitor.DisplayIndex;
        existing.SetBounds(monitor.Bounds);
        existing.Edges = edges.Clone();
    }
}
