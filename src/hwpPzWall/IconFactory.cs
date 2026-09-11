using System.Drawing;
using System.Drawing.Drawing2D;

namespace HwpPzWall;

/// <summary>
/// 트레이 아이콘을 코드로 그린다(별도 리소스 파일 불필요).
/// 모니터 모양 + 막혀 있는 띠.
///   켜짐: 붉은색 + 화면 안쪽까지 붉게 물듦 (화면에 표시되는 금지 영역 색과 같다)
///   꺼짐: 회색 + 흰 화면
/// 색을 못 알아보는 경우에도 구분되도록, 켜짐일 때는 화면 전체 색조까지 바뀐다.
/// </summary>
internal static class IconFactory
{
    private static readonly List<IntPtr> Handles = new();

    public static Icon Create(bool active, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var accent = active ? Color.FromArgb(0xE5, 0x39, 0x35) : Color.FromArgb(0x8A, 0x8A, 0x8A);
            var frame = active ? Color.FromArgb(0x9A, 0x1B, 0x1B) : Color.FromArgb(0x5A, 0x5A, 0x5A);
            var glass = active ? Color.FromArgb(245, 0xFF, 0xE8, 0xE6) : Color.FromArgb(210, 0xFF, 0xFF, 0xFF);

            float pad = size * 0.09f;
            var screen = new RectangleF(pad, pad * 1.6f, size - pad * 2, size - pad * 3.4f);

            using (var body = new SolidBrush(glass))
                g.FillRectangle(body, screen);

            using (var pen = new Pen(frame, Math.Max(1.4f, size * 0.075f)))
                g.DrawRectangle(pen, screen.X, screen.Y, screen.Width, screen.Height);

            // 막힌 왼쪽 띠
            var band = new RectangleF(screen.X, screen.Y, screen.Width * 0.24f, screen.Height);
            using (var bandBrush = new SolidBrush(accent))
                g.FillRectangle(bandBrush, band);

            // 받침대
            using (var stand = new SolidBrush(frame))
            {
                float sw = size * 0.34f;
                g.FillRectangle(stand, (size - sw) / 2f, screen.Bottom + size * 0.04f, sw, size * 0.10f);
            }
        }

        var handle = bmp.GetHicon();
        Handles.Add(handle);
        // 원본 핸들을 뒤에서 직접 파괴하므로 Clone 으로 독립 인스턴스를 만든다.
        using var temp = Icon.FromHandle(handle);
        return (Icon)temp.Clone();
    }

    public static void ReleaseAll()
    {
        foreach (var h in Handles) Native.DestroyIcon(h);
        Handles.Clear();
    }
}
