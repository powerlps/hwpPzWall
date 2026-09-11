using System.Drawing;

namespace HwpPzWall;

internal sealed class SettingsForm : Form
{
    private readonly AppConfig _working;
    private List<MonitorInfo> _monitors;

    private readonly ComboBox _monitorCombo = new();
    private readonly Button _identifyButton = new();

    private readonly CheckBox[] _edgeChecks = new CheckBox[4];
    private readonly NumericUpDown[] _edgeValues = new NumericUpDown[4];
    private static readonly string[] EdgeNames = { "왼쪽", "오른쪽", "위쪽", "아래쪽" };

    private readonly PreviewPanel _preview = new();
    private readonly Button _showZoneButton = new();
    private readonly ZoneOverlay _overlay = new();

    private readonly Label _statusLabel = new();

    private readonly HotkeyBox _hotkeyBox = new();
    private readonly RadioButton _modeJump = new();
    private readonly RadioButton _modeWall = new();

    private readonly CheckBox _autoStart = new();
    private readonly CheckBox _enableOnLaunch = new();
    private readonly CheckBox _showNotifications = new();

    private bool _loading;

    /// <summary>저장을 눌렀을 때의 설정.</summary>
    public AppConfig Result => _working;

    public SettingsForm(AppConfig config)
    {
        _working = config.Clone();
        _monitors = MonitorInfo.Enumerate();

        Text = "hwpPzWall 설정";
        Font = new Font("맑은 고딕", 9F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(520, 700);
        ShowInTaskbar = true;

        BuildUi();
        LoadFromConfig();
    }

    // ---------------- UI 구성 ----------------

    private void BuildUi()
    {
        int y = 12;

        // --- 현재 상태 ---
        _statusLabel.Location = new Point(12, y);
        _statusLabel.Size = new Size(496, 32);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.BorderStyle = BorderStyle.FixedSingle;
        _statusLabel.Padding = new Padding(10, 0, 0, 0);
        Controls.Add(_statusLabel);

        y += _statusLabel.Height + 10;

        // --- 1. 금지 영역 ---
        var zoneGroup = new GroupBox
        {
            Text = "1. 마우스 동작금지 영역",
            Location = new Point(12, y),
            Size = new Size(496, 420),
        };
        Controls.Add(zoneGroup);

        var monitorLabel = new Label { Text = "모니터", Location = new Point(16, 30), AutoSize = true };
        zoneGroup.Controls.Add(monitorLabel);

        _monitorCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _monitorCombo.Location = new Point(66, 26);
        _monitorCombo.Size = new Size(330, 24);
        _monitorCombo.SelectedIndexChanged += OnMonitorChanged;
        zoneGroup.Controls.Add(_monitorCombo);

        _identifyButton.Text = "번호 확인";
        _identifyButton.Location = new Point(404, 25);
        _identifyButton.Size = new Size(78, 26);
        _identifyButton.Click += (_, _) => MonitorIdentifier.Show(_monitors);
        zoneGroup.Controls.Add(_identifyButton);

        var hint = new Label
        {
            Text = "막을 가장자리를 고르고 폭을 픽셀로 지정하세요. 한글 프레젠테이션의\n" +
                   "쪽 미리보기는 보통 왼쪽 10~30px 안에서 열립니다.",
            Location = new Point(16, 60),
            Size = new Size(466, 36),
            ForeColor = SystemColors.GrayText,
        };
        zoneGroup.Controls.Add(hint);

        int rowY = 104;
        for (int i = 0; i < 4; i++)
        {
            int index = i;

            _edgeChecks[i] = new CheckBox
            {
                Text = EdgeNames[i],
                Location = new Point(20, rowY + 2),
                Size = new Size(86, 24),
            };
            _edgeChecks[i].CheckedChanged += (_, _) => OnEdgeCheckChanged(index);
            zoneGroup.Controls.Add(_edgeChecks[i]);

            _edgeValues[i] = new NumericUpDown
            {
                Location = new Point(112, rowY),
                Size = new Size(84, 24),
                Minimum = 1,
                Maximum = 2000,
                Value = 15,
                Increment = 5,
                TextAlign = HorizontalAlignment.Right,
                Enabled = false,
            };
            _edgeValues[i].ValueChanged += (_, _) => CommitAndRefresh();
            zoneGroup.Controls.Add(_edgeValues[i]);

            zoneGroup.Controls.Add(new Label
            {
                Text = "px",
                Location = new Point(202, rowY + 4),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
            });

            rowY += 32;
        }

        // 실제 화면 위에 붉은 박스로 보여주는 버튼 — 가장자리 입력칸 오른쪽
        _showZoneButton.Location = new Point(258, 110);
        _showZoneButton.Size = new Size(150, 34);
        _showZoneButton.Click += (_, _) => ToggleZoneOverlay();
        zoneGroup.Controls.Add(_showZoneButton);

        zoneGroup.Controls.Add(new Label
        {
            Text = "지정한 영역을 실제 화면 위에\n반투명 붉은 박스로 표시합니다.\n켜 둔 채로 px 값을 바꾸면\n바로 따라 움직입니다.",
            Location = new Point(258, 150),
            Size = new Size(226, 64),
            ForeColor = SystemColors.GrayText,
        });

        var previewLabel = new Label
        {
            Text = "미리보기  (빨간 띠 = 금지 영역 · 모니터를 클릭하면 선택됩니다)",
            Location = new Point(16, rowY + 6),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };
        zoneGroup.Controls.Add(previewLabel);

        _preview.Location = new Point(16, rowY + 28);
        _preview.Size = new Size(466, 148);
        _preview.MonitorSelected += (_, monitor) => SelectMonitor(monitor);
        zoneGroup.Controls.Add(_preview);

        y += zoneGroup.Height + 10;

        // --- 2. 단축키 ---
        var hotkeyGroup = new GroupBox
        {
            Text = "2. 켜기/끄기 단축키",
            Location = new Point(12, y),
            Size = new Size(496, 76),
        };
        Controls.Add(hotkeyGroup);

        _hotkeyBox.Location = new Point(16, 30);
        _hotkeyBox.Size = new Size(200, 26);
        _hotkeyBox.HotkeyChanged += (_, _) => CommitAndRefresh();
        hotkeyGroup.Controls.Add(_hotkeyBox);

        hotkeyGroup.Controls.Add(new Label
        {
            Text = "입력칸을 클릭한 뒤 원하는 키를 누르세요.\nF1~F12 는 단독으로도 지정할 수 있습니다.",
            Location = new Point(228, 26),
            Size = new Size(256, 36),
            ForeColor = SystemColors.GrayText,
        });

        y += hotkeyGroup.Height + 10;

        // --- 3. 동작 방식 ---
        var modeGroup = new GroupBox
        {
            Text = "3. 금지 영역에 닿았을 때",
            Location = new Point(12, y),
            Size = new Size(496, 96),
        };
        Controls.Add(modeGroup);

        _modeJump.Text = "뛰어넘기 — 영역을 건너뛰어 반대편으로 (모니터 간 이동 유지, 권장)";
        _modeJump.Location = new Point(16, 26);
        _modeJump.Size = new Size(466, 24);
        _modeJump.CheckedChanged += (_, _) => CommitAndRefresh();
        modeGroup.Controls.Add(_modeJump);

        _modeWall.Text = "완전 차단 — 벽처럼 막음 (빠르게 움직여도 뚫리지 않음)";
        _modeWall.Location = new Point(16, 54);
        _modeWall.Size = new Size(466, 24);
        _modeWall.CheckedChanged += (_, _) => CommitAndRefresh();
        modeGroup.Controls.Add(_modeWall);

        y += modeGroup.Height + 10;

        // --- 4. 기타 ---
        var etcGroup = new GroupBox
        {
            Text = "4. 기타",
            Location = new Point(12, y),
            Size = new Size(496, 108),
        };
        Controls.Add(etcGroup);

        _autoStart.Text = "Windows 시작 시 자동 실행";
        _autoStart.Location = new Point(16, 26);
        _autoStart.Size = new Size(466, 24);
        etcGroup.Controls.Add(_autoStart);

        _enableOnLaunch.Text = "프로그램이 켜질 때 차단도 바로 켜기";
        _enableOnLaunch.Location = new Point(16, 52);
        _enableOnLaunch.Size = new Size(466, 24);
        etcGroup.Controls.Add(_enableOnLaunch);

        _showNotifications.Text = "켜짐/꺼짐이 바뀔 때 알림 풍선 표시";
        _showNotifications.Location = new Point(16, 78);
        _showNotifications.Size = new Size(466, 24);
        etcGroup.Controls.Add(_showNotifications);

        y += etcGroup.Height + 14;

        // --- 버튼 ---
        var saveButton = new Button
        {
            Text = "저장",
            Location = new Point(320, y),
            Size = new Size(90, 30),
            DialogResult = DialogResult.OK,
        };
        saveButton.Click += OnSave;
        Controls.Add(saveButton);

        var cancelButton = new Button
        {
            Text = "취소",
            Location = new Point(418, y),
            Size = new Size(90, 30),
            DialogResult = DialogResult.Cancel,
        };
        Controls.Add(cancelButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        ClientSize = new Size(520, y + 30 + 14);
    }

    // ---------------- 값 채우기 / 읽기 ----------------

    private void LoadFromConfig()
    {
        _loading = true;

        _monitorCombo.Items.Clear();
        foreach (var m in _monitors) _monitorCombo.Items.Add(m.Caption);

        if (_monitors.Count > 0)
        {
            // 이미 설정이 있는 모니터를, 없으면 보조 모니터를 먼저 보여준다.
            int index = _monitors.FindIndex(m => _working.GetEdges(m).HasAny);
            if (index < 0) index = _monitors.FindIndex(m => !m.IsPrimary);
            if (index < 0) index = 0;
            _monitorCombo.SelectedIndex = index;
        }

        _hotkeyBox.SetHotkey(_working.HotkeyModifiers, _working.HotkeyVirtualKey);
        _modeJump.Checked = _working.Mode == BlockMode.JumpOver;
        _modeWall.Checked = _working.Mode == BlockMode.HardWall;

        _autoStart.Checked = AutoStart.IsEnabled();
        _enableOnLaunch.Checked = _working.EnableBlockingOnLaunch;
        _showNotifications.Checked = _working.ShowNotifications;

        _loading = false;

        LoadEdgesForSelectedMonitor();
        RefreshPreview();
        UpdateStatusLabel();
        UpdateShowZoneButton();
    }

    private MonitorInfo? SelectedMonitor =>
        _monitorCombo.SelectedIndex >= 0 && _monitorCombo.SelectedIndex < _monitors.Count
            ? _monitors[_monitorCombo.SelectedIndex]
            : null;

    /// <summary>미리보기에서 모니터를 클릭했을 때 드롭다운 선택을 맞춘다.</summary>
    private void SelectMonitor(MonitorInfo monitor)
    {
        int index = _monitors.IndexOf(monitor);
        if (index >= 0 && index != _monitorCombo.SelectedIndex)
            _monitorCombo.SelectedIndex = index;   // OnMonitorChanged 가 나머지를 처리한다
    }

    private void OnMonitorChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        LoadEdgesForSelectedMonitor();
        RefreshPreview();
    }

    private void LoadEdgesForSelectedMonitor()
    {
        var monitor = SelectedMonitor;
        if (monitor == null) return;

        _loading = true;
        var edges = _working.GetEdges(monitor);
        int[] values = { edges.Left, edges.Right, edges.Top, edges.Bottom };

        for (int i = 0; i < 4; i++)
        {
            bool on = values[i] > 0;
            _edgeChecks[i].Checked = on;
            _edgeValues[i].Enabled = on;
            _edgeValues[i].Value = Math.Clamp(on ? values[i] : 15, (int)_edgeValues[i].Minimum, (int)_edgeValues[i].Maximum);
        }
        _loading = false;
    }

    private void OnEdgeCheckChanged(int index)
    {
        _edgeValues[index].Enabled = _edgeChecks[index].Checked;
        CommitAndRefresh();
    }

    private void CommitAndRefresh()
    {
        if (_loading) return;

        var monitor = SelectedMonitor;
        if (monitor != null)
        {
            _working.SetEdges(monitor, new EdgeZones
            {
                Left = _edgeChecks[0].Checked ? (int)_edgeValues[0].Value : 0,
                Right = _edgeChecks[1].Checked ? (int)_edgeValues[1].Value : 0,
                Top = _edgeChecks[2].Checked ? (int)_edgeValues[2].Value : 0,
                Bottom = _edgeChecks[3].Checked ? (int)_edgeValues[3].Value : 0,
            });
        }

        _working.Mode = _modeWall.Checked ? BlockMode.HardWall : BlockMode.JumpOver;

        if (_hotkeyBox.VirtualKey != 0)
        {
            _working.HotkeyModifiers = _hotkeyBox.Modifiers;
            _working.HotkeyVirtualKey = _hotkeyBox.VirtualKey;
        }

        RefreshPreview();
        UpdateStatusLabel();
    }

    private void RefreshPreview()
    {
        var rects = _working.BuildZoneRects(_monitors);
        _preview.SetData(_monitors, rects, SelectedMonitor?.DeviceName);

        // 화면에 띄워 둔 상태라면 값이 바뀔 때마다 같이 움직인다.
        if (_overlay.IsVisible) _overlay.Show(rects);
    }

    // ---------------- 화면 위 영역 표시 ----------------

    private void ToggleZoneOverlay()
    {
        if (_overlay.IsVisible)
        {
            _overlay.Hide();
        }
        else
        {
            var rects = _working.BuildZoneRects(_monitors);
            if (rects.Count == 0)
            {
                MessageBox.Show(this,
                    "먼저 막을 가장자리에 체크하고 폭을 지정하세요.",
                    "hwpPzWall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _overlay.Show(rects);
        }
        UpdateShowZoneButton();
    }

    private void UpdateShowZoneButton()
    {
        _showZoneButton.Text = _overlay.IsVisible ? "영역 숨기기" : "영역 확인";
    }

    // ---------------- 현재 차단 상태 ----------------

    private void UpdateStatusLabel()
    {
        bool on = BlockEngine.IsRunning;
        var hotkey = HotkeyText.Format(_working.HotkeyModifiers, _working.HotkeyVirtualKey);

        _statusLabel.Text = on
            ? $"● 지금 차단 중입니다.   끄려면 {hotkey}"
            : $"● 지금 차단이 꺼져 있습니다.   켜려면 {hotkey}  (또는 트레이 아이콘 우클릭)";

        _statusLabel.ForeColor = on ? Color.FromArgb(0xC0, 0x28, 0x25) : SystemColors.GrayText;
        _statusLabel.BackColor = on ? Color.FromArgb(0xFF, 0xEB, 0xEE) : SystemColors.Control;
        _statusLabel.Font = new Font(Font, on ? FontStyle.Bold : FontStyle.Regular);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _overlay.Dispose();
        base.OnFormClosed(e);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        CommitAndRefresh();

        if (_hotkeyBox.VirtualKey == 0)
        {
            MessageBox.Show(this, "켜기/끄기 단축키를 지정해 주세요.", "hwpPzWall",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
            _hotkeyBox.Focus();
            return;
        }

        if (_working.Zones.All(z => !z.Edges.HasAny))
        {
            var answer = MessageBox.Show(this,
                "금지 영역이 하나도 지정되지 않았습니다.\n이대로 저장하면 차단을 켜도 아무 일도 일어나지 않습니다.\n\n그래도 저장할까요?",
                "hwpPzWall", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                DialogResult = DialogResult.None;
                return;
            }
        }

        _working.HotkeyModifiers = _hotkeyBox.Modifiers;
        _working.HotkeyVirtualKey = _hotkeyBox.VirtualKey;
        _working.EnableBlockingOnLaunch = _enableOnLaunch.Checked;
        _working.ShowNotifications = _showNotifications.Checked;
        _working.AutoStartWithWindows = _autoStart.Checked;

        if (_autoStart.Checked != AutoStart.IsEnabled())
            AutoStart.Set(_autoStart.Checked);
    }

    /// <summary>모니터 구성이 바뀌었을 때 목록을 다시 읽는다.</summary>
    public void ReloadMonitors()
    {
        var selectedDevice = SelectedMonitor?.DeviceName;
        _monitors = MonitorInfo.Enumerate();

        _loading = true;
        _monitorCombo.Items.Clear();
        foreach (var m in _monitors) _monitorCombo.Items.Add(m.Caption);

        int index = _monitors.FindIndex(m =>
            string.Equals(m.DeviceName, selectedDevice, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = _monitors.Count > 0 ? 0 : -1;
        if (index >= 0) _monitorCombo.SelectedIndex = index;
        _loading = false;

        LoadEdgesForSelectedMonitor();
        RefreshPreview();
    }
}
