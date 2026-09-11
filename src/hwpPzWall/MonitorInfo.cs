using System.Drawing;
using System.Text.RegularExpressions;

namespace HwpPzWall;

/// <summary>물리 픽셀 기준 모니터 정보. (앱은 Per-Monitor V2 DPI 인식이므로 좌표 보정이 필요 없다)</summary>
internal sealed partial class MonitorInfo
{
    public string DeviceName { get; init; } = "";     // 예: \.\DISPLAY2
    public string FriendlyName { get; init; } = "";   // 예: Generic PnP Monitor
    public Rectangle Bounds { get; init; }
    public bool IsPrimary { get; init; }
    public int DisplayIndex { get; init; }            // \.\DISPLAY2 -> 2

    public string Caption
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(FriendlyName) ? "디스플레이" : FriendlyName;
            var primary = IsPrimary ? "  [주 모니터]" : "";
            return $"{DisplayIndex}번  {name}  ({Bounds.Width}×{Bounds.Height}){primary}";
        }
    }

    [GeneratedRegex(@"DISPLAY(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayNumber();

    public static List<MonitorInfo> Enumerate()
    {
        var list = new List<MonitorInfo>();

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref Native.RECT clip, IntPtr data)
        {
            var mi = new Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (!Native.GetMonitorInfo(hMonitor, ref mi)) return true;

            var device = mi.szDevice ?? "";
            var m = DisplayNumber().Match(device);
            var index = m.Success ? int.Parse(m.Groups[1].Value) : list.Count + 1;

            list.Add(new MonitorInfo
            {
                DeviceName = device,
                FriendlyName = GetFriendlyName(device),
                Bounds = mi.rcMonitor.ToRectangle(),
                IsPrimary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                DisplayIndex = index,
            });
            return true;
        }

        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        list.Sort((a, b) => a.DisplayIndex.CompareTo(b.DisplayIndex));
        return list;
    }

    private static string GetFriendlyName(string deviceName)
    {
        try
        {
            var dd = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
            if (Native.EnumDisplayDevices(deviceName, 0, ref dd, 0))
                return dd.DeviceString ?? "";
        }
        catch { /* 이름은 부가 정보일 뿐이므로 실패해도 무시 */ }
        return "";
    }

    /// <summary>전체 가상 데스크톱 영역.</summary>
    public static Rectangle VirtualBounds(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return Rectangle.Empty;
        var r = monitors[0].Bounds;
        foreach (var m in monitors) r = Rectangle.Union(r, m.Bounds);
        return r;
    }
}
