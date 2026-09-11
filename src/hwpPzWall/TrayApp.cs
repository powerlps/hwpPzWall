using System.Drawing;

namespace HwpPzWall;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly MessageWindow _messages;
    private readonly Icon _iconOn;
    private readonly Icon _iconOff;

    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _autoStartItem;

    private AppConfig _config;
    private bool _firstRunPending;
    private SettingsForm? _settingsForm;
    private System.Windows.Forms.Timer? _displayChangeDebounce;

    public TrayApp(bool firstRun)
    {
        _config = AppConfig.Load();

        _iconOn = IconFactory.Create(active: true);
        _iconOff = IconFactory.Create(active: false);

        _toggleItem = new ToolStripMenuItem("차단 켜기", null, (_, _) => Toggle()) { CheckOnClick = false };
        _autoStartItem = new ToolStripMenuItem("Windows 시작 시 자동 실행", null, (_, _) => ToggleAutoStart())
        {
            Checked = AutoStart.IsEnabled(),
        };

        var menu = new ContextMenuStrip { Font = new Font("맑은 고딕", 9F) };
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("설정...", null, (_, _) => ShowSettings()));
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("종료", null, (_, _) => ExitApp()));

        _tray = new NotifyIcon
        {
            Icon = _iconOff,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _messages = new MessageWindow();
        _messages.HotkeyPressed += Toggle;
        _messages.DisplayChanged += OnDisplayChanged;

        ApplyHotkey(notifyOnFailure: true);
        UpdateTrayState();

        if (firstRun || _config.Zones.Count == 0)
        {
            // 처음 설정을 마치면 곧바로 켜 준다 — 설정만 하고 아무 일도 안 일어나면 헷갈린다.
            _firstRunPending = true;
            ShowSettings();
        }
        else if (_config.EnableBlockingOnLaunch)
        {
            SetBlocking(true, silent: false);
        }
    }

    // ---------------- 차단 제어 ----------------

    private void Toggle() => SetBlocking(!BlockEngine.IsRunning, silent: false);

    private void SetBlocking(bool on, bool silent)
    {
        if (on)
        {
            var monitors = MonitorInfo.Enumerate();
            var zones = _config.BuildZoneRects(monitors);

            if (zones.Count == 0)
            {
                Notify("금지 영역이 지정되지 않았습니다. 설정에서 먼저 지정하세요.", ToolTipIcon.Warning, force: true);
                ShowSettings();
                return;
            }

            var bounds = monitors.Select(m => m.Bounds).ToList();
            if (!BlockEngine.Start(zones, bounds, _config.Mode))
            {
                Notify(BlockEngine.LastError ?? "차단을 시작하지 못했습니다.", ToolTipIcon.Error, force: true);
                UpdateTrayState();
                return;
            }

            if (!silent) Notify($"차단 켜짐 — 영역 {zones.Count}개", ToolTipIcon.Info);
        }
        else
        {
            BlockEngine.Stop();
            if (!silent) Notify("차단 꺼짐", ToolTipIcon.Info);
        }

        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        bool on = BlockEngine.IsRunning;
        var hotkey = HotkeyText.Format(_config.HotkeyModifiers, _config.HotkeyVirtualKey);

        _tray.Icon = on ? _iconOn : _iconOff;
        _toggleItem.Text = on ? $"차단 끄기 ({hotkey})" : $"차단 켜기 ({hotkey})";

        var text = on ? $"hwpPzWall — 차단 켜짐 ({hotkey})" : $"hwpPzWall — 차단 꺼짐 ({hotkey})";
        _tray.Text = text.Length > 63 ? text[..63] : text;

        _autoStartItem.Checked = AutoStart.IsEnabled();
    }

    private void Notify(string message, ToolTipIcon icon, bool force = false)
    {
        if (!force && !_config.ShowNotifications) return;
        try
        {
            _tray.BalloonTipTitle = "hwpPzWall";
            _tray.BalloonTipText = message;
            _tray.BalloonTipIcon = icon;
            _tray.ShowBalloonTip(2000);
        }
        catch (Exception ex)
        {
            Log.Write($"알림 표시 실패: {ex.Message}");
        }
    }

    // ---------------- 단축키 ----------------

    private void ApplyHotkey(bool notifyOnFailure)
    {
        if (!_messages.RegisterHotkey(_config.HotkeyModifiers, _config.HotkeyVirtualKey, out var error) && notifyOnFailure)
            Notify($"{error}\n설정에서 다른 키로 바꿔 주세요.", ToolTipIcon.Warning, force: true);
    }

    private void ToggleAutoStart()
    {
        bool target = !AutoStart.IsEnabled();
        if (AutoStart.Set(target))
        {
            _config.AutoStartWithWindows = target;
            _config.Save();
        }
        _autoStartItem.Checked = AutoStart.IsEnabled();
    }

    // ---------------- 설정창 ----------------

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_config) { Icon = _iconOn };
        var result = _settingsForm.ShowDialog();

        if (result == DialogResult.OK)
        {
            _config = _settingsForm.Result;
            _config.Save();

            _messages.UnregisterHotkey();
            ApplyHotkey(notifyOnFailure: true);

            if (BlockEngine.IsRunning)
            {
                // 켜져 있었다면 새 영역으로 다시 시작한다.
                SetBlocking(true, silent: true);
            }
            else if (_firstRunPending && _config.Zones.Any(z => z.Edges.HasAny))
            {
                _firstRunPending = false;
                SetBlocking(true, silent: false);
            }
        }

        _settingsForm.Dispose();
        _settingsForm = null;
        UpdateTrayState();
    }

    // ---------------- 디스플레이 변경 ----------------

    private void OnDisplayChanged()
    {
        // 해상도 변경 직후에는 값이 잠깐 요동치므로 조금 기다렸다 반영한다.
        _displayChangeDebounce?.Stop();
        _displayChangeDebounce?.Dispose();

        _displayChangeDebounce = new System.Windows.Forms.Timer { Interval = 1200 };
        _displayChangeDebounce.Tick += (_, _) =>
        {
            _displayChangeDebounce!.Stop();

            var monitors = MonitorInfo.Enumerate();
            Log.Write($"디스플레이 구성 변경 감지 — 모니터 {monitors.Count}개");

            if (BlockEngine.IsRunning)
            {
                var zones = _config.BuildZoneRects(monitors);
                if (zones.Count == 0)
                {
                    BlockEngine.Stop();
                    Notify("모니터 구성이 바뀌어 금지 영역을 찾을 수 없습니다. 차단을 껐습니다.", ToolTipIcon.Warning, force: true);
                }
                else
                {
                    BlockEngine.UpdateZones(zones, monitors.Select(m => m.Bounds).ToList());
                }
                UpdateTrayState();
            }

            if (_settingsForm is { IsDisposed: false }) _settingsForm.ReloadMonitors();
        };
        _displayChangeDebounce.Start();
    }

    // ---------------- 종료 ----------------

    private void ExitApp()
    {
        BlockEngine.Stop();
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            BlockEngine.Stop();
            _displayChangeDebounce?.Dispose();
            _messages.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _iconOn.Dispose();
            _iconOff.Dispose();
            IconFactory.ReleaseAll();
        }
        base.Dispose(disposing);
    }
}
