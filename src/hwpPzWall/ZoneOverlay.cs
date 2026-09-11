using System.Drawing;

namespace HwpPzWall;

/// <summary>
/// 금지 영역을 실제 화면 위에 반투명 붉은 박스로 보여준다.
/// 설정창에서 [영역 확인] 을 누르면 켜지고, 값을 바꾸면 그 자리에서 따라 움직인다.
/// 창은 클릭이 통과하도록 만들어져 있어서 뒤쪽 조작을 막지 않는다.
/// </summary>
internal sealed class ZoneOverlay : IDisposable
{
    private readonly List<OverlayWindow> _windows = new();

    /// <summary>표시 중인지 여부. 지정된 영역이 하나도 없어도 "표시 중" 상태는 유지된다.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>
    /// 지정한 사각형들을 화면에 그린다. 이미 떠 있으면 창을 새로 만들지 않고 좌표만 옮기므로,
    /// 설정값을 바꾸는 동안 깜박이지 않는다.
    /// </summary>
    public void Show(IReadOnlyList<Rectangle> rects)
    {
        var valid = rects.Where(r => r.Width > 0 && r.Height > 0).ToList();

        while (_windows.Count > valid.Count)
        {
            var extra = _windows[^1];
            _windows.RemoveAt(_windows.Count - 1);
            extra.Close();
            extra.Dispose();
        }

        while (_windows.Count < valid.Count)
        {
            var window = new OverlayWindow();
            _windows.Add(window);
            window.Show();
        }

        for (int i = 0; i < valid.Count; i++)
        {
            var r = valid[i];
            // 물리 픽셀 좌표로 직접 배치한다(모니터마다 배율이 달라도 어긋나지 않게).
            Native.SetWindowPos(_windows[i].Handle, Native.HWND_TOPMOST,
                r.X, r.Y, r.Width, r.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        IsVisible = true;
    }

    public void Hide()
    {
        foreach (var w in _windows)
        {
            w.Close();
            w.Dispose();
        }
        _windows.Clear();
        IsVisible = false;
    }

    public void Dispose() => Hide();

    private sealed class OverlayWindow : Form
    {
        public OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;   // SetWindowPos 로 잡은 크기를 WinForms 가 다시 건드리지 않게
            BackColor = Color.FromArgb(0xE5, 0x39, 0x35);
            Opacity = 0.5;
            Enabled = false;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // 클릭 통과 + 포커스 안 뺏음 + Alt+Tab 목록에 안 나타남
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                              Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }
}
