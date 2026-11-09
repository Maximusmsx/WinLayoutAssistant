using System.Drawing.Drawing2D;

namespace WinLayoutAssistant;

/// <summary>
/// Shared palette + custom controls, v1.4 visual refresh.
/// Quiet light surfaces, deep navy→indigo header, royal-blue accent that
/// matches the app icon. Cosmetic only – no behavior changes.
/// </summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(0xF5, 0xF6, 0xFA); // cool soft grey page
    public static readonly Color Card = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(0xE7, 0xE9, 0xF0);
    public static readonly Color Well = Color.FromArgb(0xF8, 0xF9, 0xFC);        // listbox inset
    public static readonly Color Header = Color.FromArgb(0x0B, 0x12, 0x26);      // deep navy
    public static readonly Color HeaderEnd = Color.FromArgb(0x24, 0x1E, 0x63);   // indigo
    public static readonly Color HeaderSub = Color.FromArgb(0x9C, 0xA6, 0xC4);
    public static readonly Color Accent = Color.FromArgb(0x25, 0x63, 0xEB);      // royal blue
    public static readonly Color AccentHover = Color.FromArgb(0x1D, 0x4E, 0xD8);
    public static readonly Color AccentPressed = Color.FromArgb(0x1E, 0x40, 0xAF);
    public static readonly Color AccentSoft = Color.FromArgb(0xEE, 0xF3, 0xFE);
    public static readonly Color AccentSoftHover = Color.FromArgb(0xDC, 0xE8, 0xFD);
    public static readonly Color AccentSoftPressed = Color.FromArgb(0xCB, 0xDC, 0xF9);
    public static readonly Color Text = Color.FromArgb(0x0F, 0x17, 0x2A);
    public static readonly Color TextSecondary = Color.FromArgb(0x64, 0x74, 0x8B);
    public static readonly Color Green = Color.FromArgb(0x10, 0xB9, 0x81);
    public static readonly Color GreenSoft = Color.FromArgb(0xEC, 0xFD, 0xF5);
    public static readonly Color DangerSoft = Color.FromArgb(0xFE, 0xF2, 0xF2);
    public static readonly Color DangerSoftHover = Color.FromArgb(0xFE, 0xE2, 0xE2);
    public static readonly Color DangerSoftPressed = Color.FromArgb(0xFE, 0xCA, 0xCA);
    public static readonly Color DangerText = Color.FromArgb(0xDC, 0x26, 0x26);
    public static readonly Color GhostHover = Color.FromArgb(0xF6, 0xF7, 0xF9);
    public static readonly Color GhostPressed = Color.FromArgb(0xEF, 0xF1, 0xF5);

    public static readonly Font TitleFont = MakeFont("Segoe UI Variable Display", 16.5f, FontStyle.Bold);
    public static readonly Font SectionFont = MakeFont("Segoe UI Variable Small", 8.5f, FontStyle.Bold);
    public static readonly Font BodyFont = MakeFont("Segoe UI Variable Text", 10f, FontStyle.Regular);
    public static readonly Font SmallFont = MakeFont("Segoe UI Variable Text", 8.5f, FontStyle.Regular);

    /// <summary>Segoe UI Variable on Win11, Segoe UI fallback on Win10.</summary>
    private static Font MakeFont(string family, float size, FontStyle style)
    {
        try
        {
            var ff = new FontFamily(family);
            if (ff.IsStyleAvailable(style)) return new Font(ff, size, style);
        }
        catch { /* family not installed -> fall through to Segoe UI */ }
        return new Font("Segoe UI", size, style);
    }

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>
    /// Soft drop shadow painted UNDER a card (call from the parent's
    /// OnPaintBackground; the card itself paints over the middle later).
    /// Three stacked layers, farthest and faintest first.
    /// </summary>
    public static void DrawSoftShadow(Graphics g, Rectangle bounds, int radius)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawShadowLayer(g, bounds, radius + 10, 10, 8);
        DrawShadowLayer(g, bounds, radius + 6, 5, 14);
        DrawShadowLayer(g, bounds, radius + 2, 2, 24);
        g.SmoothingMode = old;
    }

    private static void DrawShadowLayer(Graphics g, Rectangle b, int radius, int dy, int alpha)
    {
        var r = new Rectangle(b.X, b.Y + dy, b.Width, b.Height);
        using var path = RoundedRect(r, radius);
        using var brush = new SolidBrush(Color.FromArgb(alpha, 0x0F, 0x17, 0x2A));
        g.FillPath(brush, path);
    }
}

/// <summary>White card with a 14px corner radius and a hairline border.</summary>
public sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 14;
    public RoundedPanel()
    {
        BackColor = Theme.Card;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Radius);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Theme.CardBorder, 1);
        e.Graphics.DrawPath(pen, path);
    }
}

/// <summary>
/// Rounded button with hover and pressed states. Fill = Accent / Soft / White
/// depending on <see cref="Kind"/>; text is drawn centred.
/// </summary>
public sealed class RoundedButton : Button
{
    public enum Kind { Primary, Soft, Ghost, DangerSoft }
    public Kind ButtonKind { get; set; } = Kind.Primary;
    public int Radius { get; set; } = 12;
    private bool _hover;
    private bool _pressed;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.BodyFont;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Radius);
        Color fill, fore;
        switch (ButtonKind)
        {
            case Kind.Primary:
                fill = _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                fore = Color.White; break;
            case Kind.Soft:
                fill = _pressed ? Theme.AccentSoftPressed : _hover ? Theme.AccentSoftHover : Theme.AccentSoft;
                fore = Theme.Accent; break;
            case Kind.DangerSoft:
                fill = _pressed ? Theme.DangerSoftPressed : _hover ? Theme.DangerSoftHover : Theme.DangerSoft;
                fore = Theme.DangerText; break;
            default:
                fill = _pressed ? Theme.GhostPressed : _hover ? Theme.GhostHover : Color.White;
                fore = Theme.Text; break;
        }
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        if (ButtonKind == Kind.Ghost)
        {
            using var pen = new Pen(Theme.CardBorder, 1);
            e.Graphics.DrawPath(pen, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Small section label, e.g. "平铺方式". Sits on a white card.</summary>
public sealed class SectionLabel : Label
{
    public SectionLabel(string text)
    {
        Text = text;
        Font = Theme.SectionFont;
        ForeColor = Theme.TextSecondary;
        BackColor = Theme.Card;
        AutoSize = true;
    }
}

/// <summary>
/// Deep navy→indigo gradient header. Everything (badge, title, subtitle,
/// version pill) is painted here directly: transparent child labels over a
/// UserPaint panel would show flat BackColor patches in WinForms.
/// </summary>
public sealed class GradientHeader : Panel
{
    public string TitleText { get; set; } = "";
    public string SubText { get; set; } = "";
    public string VersionText { get; set; } = "";

    public GradientHeader()
    {
        BackColor = Theme.Header;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;

        // base diagonal wash: deep navy -> indigo
        using (var brush = new LinearGradientBrush(rect, Theme.Header, Theme.HeaderEnd, 30f))
            g.FillRectangle(brush, rect);

        // soft indigo glow bleeding in from the upper right
        using (var gp = new GraphicsPath())
        {
            var glowRect = new Rectangle(rect.Width - 300, -140, 440, 300);
            gp.AddEllipse(glowRect);
            using var pg = new PathGradientBrush(gp)
            {
                CenterColor = Color.FromArgb(52, 0x4C, 0x60, 0xEB),
                SurroundColors = new[] { Color.FromArgb(0, 0x4C, 0x60, 0xEB) }
            };
            g.FillEllipse(pg, glowRect);
        }

        DrawBadge(g, 24, 25, 46);

        if (!string.IsNullOrEmpty(TitleText))
            TextRenderer.DrawText(g, TitleText, Theme.TitleFont, new Point(84, 18),
                Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(SubText))
            TextRenderer.DrawText(g, SubText, Theme.SmallFont, new Point(85, 56),
                Theme.HeaderSub, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        DrawVersionPill(g, rect);
    }

    /// <summary>Mini app-icon: rounded gradient square + tiling grid.</summary>
    private static void DrawBadge(Graphics g, int x, int y, int s)
    {
        var rect = new Rectangle(x, y, s, s);
        using (var path = Theme.RoundedRect(rect, 13))
        using (var brush = new LinearGradientBrush(rect,
                   Color.FromArgb(0x38, 0xBD, 0xF8), Color.FromArgb(0x25, 0x63, 0xEB), 45f))
            g.FillPath(brush, path);

        using var tile = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
        FillRound(g, tile, x + 11, y + 11, 13, 24, 4); // left, tall
        FillRound(g, tile, x + 27, y + 11, 8, 10, 3);  // right top
        FillRound(g, tile, x + 27, y + 24, 8, 11, 3);  // right bottom
    }

    /// <summary>Glass pill with the version string, vertically centred right.</summary>
    private void DrawVersionPill(Graphics g, Rectangle rect)
    {
        if (string.IsNullOrEmpty(VersionText)) return;
        const int pw = 56, ph = 24;
        var pill = new Rectangle(rect.Width - 24 - pw, (rect.Height - ph) / 2, pw, ph);
        using var path = Theme.RoundedRect(pill, ph / 2);
        using (var fill = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            g.FillPath(fill, path);
        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1))
            g.DrawPath(pen, path);
        TextRenderer.DrawText(g, VersionText, Theme.SmallFont, pill,
            Color.FromArgb(235, 255, 255, 255),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static void FillRound(Graphics g, Brush b, int x, int y, int w, int h, int r)
    {
        using var path = Theme.RoundedRect(new Rectangle(x, y, w, h), r);
        g.FillPath(b, path);
    }
}

/// <summary>Small status dot: emerald core with a soft halo.</summary>
public sealed class GlowDot : Control
{
    public GlowDot()
    {
        Size = new Size(12, 12);
        BackColor = Theme.Background;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var halo = new SolidBrush(Color.FromArgb(46, Theme.Green)))
            g.FillEllipse(halo, 0, 0, Width - 1, Height - 1);
        using (var mid = new SolidBrush(Color.FromArgb(90, Theme.Green)))
            g.FillEllipse(mid, 2, 2, Width - 5, Height - 5);
        using (var core = new SolidBrush(Theme.Green))
            g.FillEllipse(core, 3, 3, Width - 7, Height - 7);
    }
}

/// <summary>Tray context menu colours aligned with the app palette.</summary>
internal sealed class MenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color MenuBorder => Theme.CardBorder;
    public override Color MenuItemBorder => Theme.Accent;
    public override Color MenuItemSelected => Theme.AccentSoft;
    public override Color MenuItemSelectedGradientBegin => Theme.AccentSoft;
    public override Color MenuItemSelectedGradientEnd => Theme.AccentSoft;
    public override Color MenuItemPressedGradientBegin => Theme.AccentSoftHover;
    public override Color MenuItemPressedGradientEnd => Theme.AccentSoftHover;
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
    public override Color SeparatorDark => Theme.CardBorder;
    public override Color SeparatorLight => Theme.CardBorder;
}
