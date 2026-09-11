using System.Drawing;
using System.Drawing.Drawing2D;

namespace HwpPzWall;

/// <summary>
/// "모니터 번호 확인" — 각 모니터에 큰 번호를 잠시 띄워 어느 게 몇 번인지 눈으로 보게 한다.
/// Windows 디스플레이 설정의 [식별] 기능과 같은 역할.
/// </summary>
internal static class MonitorIdentifier
{
    public static void Show(IReadOnlyList<MonitorInfo> monitors, int milliseconds = 2500)
    {
        var forms = new List<Form>();

        foreach (var m in monitors)
        {
            var form = new IdentifyForm(m);
            forms.Add(form);
            form.Show();
            // 물리 픽셀 좌표로 직접 배치한다(모니터마다 DPI 가 달라도 어긋나지 않게).
            Native.SetWindowPos(form.Handle, Native.HWND_TOPMOST,
                m.Bounds.X, m.Bounds.Y, m.Bounds.Width, m.Bounds.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        var timer = new System.Windows.Forms.Timer { Interval = milliseconds };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            foreach (var f in forms) f.Close();
        };
        timer.Start();
    }

    private sealed class IdentifyForm : Form
    {
        private readonly MonitorInfo _monitor;

        public IdentifyForm(MonitorInfo monitor)
        {
            _monitor = monitor;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;   // SetWindowPos 로 잡은 크기를 WinForms 가 다시 건드리지 않게
            BackColor = Color.Black;
            Opacity = 0.78;
            DoubleBuffered = true;
            Enabled = false;              // 클릭이 통과하지 않아도 되지만 입력은 받지 않는다
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            var size = Math.Min(ClientSize.Width, ClientSize.Height);

            using var bigFont = new Font("Segoe UI", size * 0.32f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font("맑은 고딕", Math.Max(12f, size * 0.035f), FontStyle.Regular, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.White);
            using var dim = new SolidBrush(Color.FromArgb(210, Color.White));

            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            var numberRect = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height * 0.82f);
            g.DrawString(_monitor.DisplayIndex.ToString(), bigFont, white, numberRect, format);

            var label = $"{_monitor.FriendlyName}   {_monitor.Bounds.Width}×{_monitor.Bounds.Height}" +
                        (_monitor.IsPrimary ? "   [주 모니터]" : "");
            var labelRect = new RectangleF(0, ClientSize.Height * 0.78f, ClientSize.Width, ClientSize.Height * 0.18f);
            g.DrawString(label, smallFont, dim, labelRect, format);
        }
    }
}
