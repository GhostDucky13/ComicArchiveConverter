using System.Drawing.Drawing2D;

namespace ComicArchiveConverter;

/// <summary>Colours, fonts and small styling helpers, so the whole app looks consistent.</summary>
internal static class Theme
{
    public static readonly Color Accent = Color.FromArgb(79, 70, 229);        // indigo
    public static readonly Color AccentDark = Color.FromArgb(67, 56, 202);
    public static readonly Color AccentLight = Color.FromArgb(238, 242, 255);
    public static readonly Color AccentSecond = Color.FromArgb(14, 165, 233);  // sky blue (gradient end)
    public static readonly Color Background = Color.FromArgb(244, 246, 251);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(203, 213, 225);
    public static readonly Color Text = Color.FromArgb(15, 23, 42);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color Warning = Color.FromArgb(217, 119, 6);
    public static readonly Color Error = Color.FromArgb(220, 38, 38);

    public static readonly Font Body = new("Segoe UI", 9.75F);
    public static readonly Font BodyBold = new("Segoe UI Semibold", 9.75F);
    public static readonly Font SectionTitle = new("Segoe UI Semibold", 11.5F);
    public static readonly Font Title = new("Segoe UI Semibold", 16F);
    public static readonly Font Hint = new("Segoe UI", 12F);
    public static readonly Font Mono = new("Consolas", 9.5F);

    /// <summary>The big coloured button (Convert).</summary>
    public static Button PrimaryButton(string text) => Style(new Button { Text = text }, Accent, Color.White, Accent);

    /// <summary>A normal white button with a grey border.</summary>
    public static Button SecondaryButton(string text) => Style(new Button { Text = text }, Surface, Text, Border);

    private static Button Style(Button button, Color back, Color fore, Color border)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = back;
        button.ForeColor = fore;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = back == Accent ? AccentDark : AccentLight;
        button.Font = back == Accent ? BodyBold : Body;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(96, 36);
        button.Padding = new Padding(12, 0, 12, 0);
        button.Margin = new Padding(6, 3, 0, 3);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
        return button;
    }

    public static void MakePrimary(Button button, bool primary)
    {
        Color back = primary ? Accent : Surface;
        button.BackColor = back;
        button.ForeColor = primary ? Color.White : Text;
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentDark : AccentLight;
    }

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? Body,
        ForeColor = color ?? Text,
        Margin = new Padding(3, 8, 3, 3),
    };

    public static CheckBox CheckBox(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Body,
        ForeColor = Text,
        Margin = new Padding(3, 5, 12, 3),
        Cursor = Cursors.Hand,
    };

    public static RadioButton Radio(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Body,
        ForeColor = Text,
        Margin = new Padding(3, 5, 14, 3),
        Cursor = Cursors.Hand,
    };

    public static ComboBox Combo(params string[] items)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 300,
            Font = Body,
            FlatStyle = FlatStyle.System,
            Margin = new Padding(3, 3, 12, 3),
        };
        combo.Items.AddRange(items);
        return combo;
    }

    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
