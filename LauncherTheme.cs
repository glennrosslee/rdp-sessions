using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class LauncherTheme
{
    public readonly bool HighContrast;
    public readonly Color Surface, Card, Border, Text, Secondary, Accent, Hover, Input, Error, Primary, OnPrimary;

    public LauncherTheme() : this(
        Equals(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1), 0),
        SystemInformation.HighContrast)
    {
    }

    internal LauncherTheme(bool dark, bool highContrast)
    {
        HighContrast = highContrast;
        Surface = HighContrast ? SystemColors.Window : dark ? Color.FromArgb(27, 28, 32) : Color.FromArgb(250, 251, 253);
        Text = HighContrast ? SystemColors.WindowText : dark ? Color.FromArgb(242, 243, 246) : Color.FromArgb(27, 34, 44);
        Card = HighContrast ? Surface : dark ? Color.FromArgb(35, 37, 43) : Color.FromArgb(240, 243, 248);
        Secondary = HighContrast ? Text : dark ? Color.FromArgb(177, 182, 192) : Color.FromArgb(79, 91, 108);
        Border = HighContrast ? Text : dark ? Color.FromArgb(83, 87, 97) : Color.FromArgb(134, 145, 160);
        Input = HighContrast ? Surface : dark ? Color.FromArgb(36, 38, 44) : Color.White;
        Error = HighContrast ? Text : dark ? Color.FromArgb(255, 156, 153) : Color.FromArgb(158, 35, 35);
        Hover = HighContrast ? SystemColors.Highlight : Blend(Surface, Text, dark ? 0.13 : 0.08);
        var glass = System.Windows.SystemParameters.WindowGlassColor;
        Accent = HighContrast ? Text : ReadableAccent(Color.FromArgb(glass.R, glass.G, glass.B));
        Primary = HighContrast ? SystemColors.Highlight : Accent;
        OnPrimary = HighContrast ? SystemColors.HighlightText : Surface;
    }

    public Button Button(string label, bool primary = false)
    {
        var button = new Button
        {
            Text = label,
            UseVisualStyleBackColor = false,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Primary : Surface,
            ForeColor = primary ? OnPrimary : Text
        };
        button.FlatAppearance.BorderSize = HighContrast ? 1 : 0;
        button.FlatAppearance.BorderColor = Text;
        if (!HighContrast)
        {
            button.FlatAppearance.MouseOverBackColor = primary ? Blend(Primary, Text, 0.18) : Hover;
            button.FlatAppearance.MouseDownBackColor = primary ? Blend(Primary, Text, 0.30) : Hover;
        }
        return button;
    }

    public ContextMenuStrip ContextMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        if (!HighContrast)
        {
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors(this)) { RoundedEdges = false };
        }
        return menu;
    }

    public ContextMenuStrip LauncherMenu()
    {
        var menu = ContextMenu();
        menu.BackColor = HighContrast ? SystemColors.Menu : Surface;
        menu.ForeColor = HighContrast ? SystemColors.MenuText : Text;
        menu.Padding = new Padding(6, 5, 6, 5);
        menu.Renderer = new LauncherMenuRenderer(this);
        return menu;
    }

    private sealed class LauncherMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly LauncherTheme theme;
        public LauncherMenuRenderer(LauncherTheme theme) : base(new MenuColors(theme))
        {
            this.theme = theme;
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs args)
        {
            if (!args.Item.Selected) return;
            var rect = Rectangle.Inflate(new Rectangle(Point.Empty, args.Item.Size), -3, -2);
            using (var brush = new SolidBrush(theme.Hover))
                args.Graphics.FillRectangle(brush, rect);
            using (var brush = new SolidBrush(theme.HighContrast ? SystemColors.HighlightText : theme.Accent))
                args.Graphics.FillRectangle(brush, rect.Left, rect.Top + 4, 2, rect.Height - 8);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs args)
        {
            args.TextColor = theme.HighContrast
                ? args.Item.Selected ? SystemColors.HighlightText : SystemColors.MenuText
                : Equals(args.Item.Tag, "delete") ? theme.Error : theme.Text;
            base.OnRenderItemText(args);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs args)
        {
            using (var pen = new Pen(theme.Border))
                args.Graphics.DrawLine(pen, 9, args.Item.Height / 2, args.Item.Width - 9, args.Item.Height / 2);
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        private readonly LauncherTheme theme;
        public MenuColors(LauncherTheme theme) { this.theme = theme; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return theme.Surface; } }
        public override Color MenuBorder { get { return theme.Border; } }
        public override Color MenuItemBorder { get { return theme.Hover; } }
        public override Color MenuItemSelected { get { return theme.Hover; } }
    }

    private Color ReadableAccent(Color color)
    {
        for (int step = 0; step <= 10; step++)
        {
            Color candidate = Blend(color, Text, step / 10.0);
            double a = Luminance(candidate) + 0.05;
            double b = Luminance(Surface) + 0.05;
            if (System.Math.Max(a, b) / System.Math.Min(a, b) >= 5)
                return candidate;
        }
        return Text;
    }

    private static double Luminance(Color color)
    {
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : System.Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        return Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }
}

internal sealed class MonitorBadge : Control
{
    private readonly LauncherTheme theme;

    public MonitorBadge(LauncherTheme theme)
    {
        this.theme = theme;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        Graphics graphics = args.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(Width / 36f, Height / 36f);
        using (var background = new SolidBrush(theme.Card))
        using (var line = new Pen(theme.Accent, 1.7f))
        {
            graphics.FillEllipse(background, 0, 0, 36, 36);
            graphics.DrawRectangle(line, 9, 10, 18, 12);
            graphics.DrawLine(line, 18, 22, 18, 26);
            graphics.DrawLine(line, 14, 26, 22, 26);
        }
    }
}
