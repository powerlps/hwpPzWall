using System.Drawing;
using System.Drawing.Drawing2D;

namespace HwpPzWall;

/// <summary>
/// 모니터 배치와 금지 영역을 축소해서 보여주는 미리보기.
/// 모니터 그림을 클릭하면 그 모니터가 선택된다(드롭다운과 연동).
/// </summary>
internal sealed class PreviewPanel : Panel
{
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private IReadOnlyList<Rectangle> _zones = Array.Empty<Rectangle>();
    private string? _selectedDevice;
    private MonitorInfo? _hovered;

    /// <summary>미리보기에서 모니터를 클릭했을 때.</summary>
    public event EventHandler<MonitorInfo>? MonitorSelected;

    public PreviewPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(0xF5, 0xF6, 0xF8);
        BorderStyle = BorderStyle.FixedSingle;
    }

    public void SetData(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<Rectangle> zones, string? selectedDevice)
    {
        _monitors = monitors;
        _zones = zones;
        _selectedDevice = selectedDevice;
        if (_hovered != null && !monitors.Contains(_hovered)) _hovered = null;
        Invalidate();
    }

    // ---------------- 좌표 변환 ----------------

    /// <summary>가상 데스크톱 좌표를 패널 좌표로 옮기는 변환기. 모니터가 없으면 null.</summary>
    private Mapper? BuildMapper()
    {
        if (_monitors.Count == 0) return null;

        var virt = MonitorInfo.VirtualBounds(_monitors);
        if (virt.Width <= 0 || virt.Height <= 0) return null;

        const int margin = 10;
        float scale = Math.Min(
            (ClientSize.Width - margin * 2f) / virt.Width,
            (ClientSize.Height - margin * 2f) / virt.Height);
        if (scale <= 0) return null;

        return new Mapper(
            virt,
            scale,
            (ClientSize.Width - virt.Width * scale) / 2f,
            (ClientSize.Height - virt.Height * scale) / 2f);
    }

    private readonly record struct Mapper(Rectangle Virtual, float Scale, float OffsetX, float OffsetY)
    {
        public RectangleF Map(Rectangle r) => new(
            OffsetX + (r.X - Virtual.X) * Scale,
            OffsetY + (r.Y - Virtual.Y) * Scale,
            Math.Max(1f, r.Width * Scale),
            Math.Max(1f, r.Height * Scale));
    }

    private MonitorInfo? HitTest(Point location)
    {
        var mapper = BuildMapper();
        if (mapper == null) return null;

        foreach (var m in _monitors)
            if (mapper.Value.Map(m.Bounds).Contains(location)) return m;

        return null;
    }

    // ---------------- 마우스 ----------------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (!ReferenceEquals(hit, _hovered))
        {
            _hovered = hit;
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hovered != null)
        {
            _hovered = null;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var hit = HitTest(e.Location);
            if (hit != null) MonitorSelected?.Invoke(this, hit);
        }
        base.OnMouseClick(e);
    }

    // ---------------- 그리기 ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_monitors.Count == 0)
        {
            TextRenderer.DrawText(g, "모니터 정보를 읽을 수 없습니다.", Font, ClientRectangle,
                SystemColors.GrayText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var mapper = BuildMapper();
        if (mapper == null) return;

        using var fill = new SolidBrush(Color.White);
        using var hoverFill = new SolidBrush(Color.FromArgb(0xF1, 0xF6, 0xFD));
        using var selectedFill = new SolidBrush(Color.FromArgb(0xEA, 0xF2, 0xFF));
        using var edge = new Pen(Color.FromArgb(0x9A, 0xA0, 0xA6), 1.5f);
        using var hoverEdge = new Pen(Color.FromArgb(0x6C, 0xA0, 0xE8), 1.8f);
        using var selectedEdge = new Pen(Color.FromArgb(0x1A, 0x73, 0xE8), 2.2f);
        using var zoneBrush = new SolidBrush(Color.FromArgb(150, 0xE5, 0x39, 0x35));
        using var numberFont = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold);

        foreach (var m in _monitors)
        {
            var r = mapper.Value.Map(m.Bounds);
            bool isSelected = string.Equals(m.DeviceName, _selectedDevice, StringComparison.OrdinalIgnoreCase);
            bool isHovered = ReferenceEquals(m, _hovered);

            g.FillRectangle(isSelected ? selectedFill : isHovered ? hoverFill : fill, r);
            g.DrawRectangle(isSelected ? selectedEdge : isHovered ? hoverEdge : edge, r.X, r.Y, r.Width, r.Height);

            var text = m.DisplayIndex.ToString();
            var size = g.MeasureString(text, numberFont);
            var color = isSelected ? Color.FromArgb(0x1A, 0x73, 0xE8)
                      : isHovered ? Color.FromArgb(0x6C, 0xA0, 0xE8)
                      : Color.FromArgb(0xB0, 0xB4, 0xB8);
            using var textBrush = new SolidBrush(color);
            g.DrawString(text, numberFont, textBrush,
                r.X + (r.Width - size.Width) / 2f, r.Y + (r.Height - size.Height) / 2f);
        }

        foreach (var z in _zones)
            g.FillRectangle(zoneBrush, mapper.Value.Map(z));
    }
}
