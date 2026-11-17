using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ComicArchiveConverter;

/// <summary>The coloured banner at the top of the window with the icon, title and subtitle.</summary>
[DesignerCategory("Code")]
internal sealed class HeaderPanel : Panel
{
    private readonly Image? _icon;

    public HeaderPanel(Image? icon)
    {
        _icon = icon;
        Height = 76;
        Dock = DockStyle.Fill;
        Margin = new Padding(0);
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    public string Subtitle { get; set; } = string.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var brush = new LinearGradientBrush(ClientRectangle, Theme.Accent, Theme.AccentSecond, LinearGradientMode.Horizontal))
            g.FillRectangle(brush, ClientRectangle);

        int x = 20;
        if (_icon is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_icon, new Rectangle(x, (Height - 44) / 2, 44, 44));
            x += 58;
        }

        TextRenderer.DrawText(g, Text, Theme.Title, new Point(x, 12), Color.White, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Subtitle, Theme.Body, new Point(x + 1, 44), Color.FromArgb(224, 231, 255), TextFormatFlags.NoPadding);
    }
}

/// <summary>A white card with a dashed, rounded border that lights up while files are dragged over it.</summary>
[DesignerCategory("Code")]
internal sealed class DropPanel : Panel
{
    private bool _highlight;

    public DropPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Background;
        Padding = new Padding(3);
        AllowDrop = true;
    }

    public bool Highlight
    {
        get => _highlight;
        set
        {
            if (_highlight == value) return;
            _highlight = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle bounds = new(1, 1, Width - 3, Height - 3);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, 10);
        using (var fill = new SolidBrush(_highlight ? Theme.AccentLight : Theme.Surface))
            g.FillPath(fill, path);
        using var pen = new Pen(_highlight ? Theme.Accent : Theme.Border, 2F) { DashStyle = DashStyle.Dash };
        g.DrawPath(pen, path);
    }
}

/// <summary>What the drop zone shows while the list is empty.</summary>
[DesignerCategory("Code")]
internal sealed class EmptyHint : Control
{
    public EmptyHint()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        AllowDrop = true;
        BackColor = Theme.Surface;
    }

    public bool Highlight { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Highlight ? Theme.AccentLight : Theme.Surface);

        int cx = Width / 2;
        int top = Math.Max(10, Height / 2 - 80);

        // A simple "drop" arrow into a tray.
        using (var pen = new Pen(Theme.Accent, 5F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLine(pen, cx, top, cx, top + 44);
            g.DrawLines(pen, new[] { new Point(cx - 18, top + 26), new Point(cx, top + 44), new Point(cx + 18, top + 26) });
            g.DrawLines(pen, new[] { new Point(cx - 34, top + 46), new Point(cx - 34, top + 62), new Point(cx + 34, top + 62), new Point(cx + 34, top + 46) });
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak;
        TextRenderer.DrawText(g, "Drop comic folders or CBZ / CBR / CB7 files here",
            Theme.Hint, new Rectangle(10, top + 80, Width - 20, 30), Theme.Text, flags);
        TextRenderer.DrawText(g,
            "Folders become CBZ files  •  Archives become folders of images\nYou can also paste copied files with Ctrl+V",
            Theme.Body, new Rectangle(10, top + 112, Width - 20, 44), Theme.Muted, flags);
    }
}
