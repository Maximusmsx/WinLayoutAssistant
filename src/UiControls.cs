using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace WinLayoutAssistant;

/// <summary>
/// v1.4 (restyle) design tokens: airy light page, white cards, royal-blue
/// accent, generous whitespace. Cosmetic only – no behavior changes.
/// </summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(0xF7, 0xF8, 0xFA);
    public static readonly Color Card = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(0xEF, 0xEF, 0xF2);
    public static readonly Color Divider = Color.FromArgb(0xF0, 0xF1, 0xF4);
    public static readonly Color Well = Color.FromArgb(0xF6, 0xF7, 0xF9);
    public static readonly Color Accent = Color.FromArgb(0x25, 0x63, 0xEB);
    public static readonly Color AccentHover = Color.FromArgb(0x1D, 0x4E, 0xD8);
    public static readonly Color AccentPressed = Color.FromArgb(0x1E, 0x40, 0xAF);
    public static readonly Color AccentSoft = Color.FromArgb(0xEE, 0xF2, 0xFD);
    public static readonly Color AccentSoftHover = Color.FromArgb(0xE0, 0xE9, 0xFC);
    public static readonly Color AccentSoftPressed = Color.FromArgb(0xD0, 0xDE, 0xFA);
    public static readonly Color Text = Color.FromArgb(0x1B, 0x1E, 0x28);
    public static readonly Color TextSecondary = Color.FromArgb(0x9A, 0xA0, 0xB0);
    public static readonly Color Green = Color.FromArgb(0x22, 0xC5, 0x5E);
    public static readonly Color Amber = Color.FromArgb(0xF5, 0x9E, 0x0B);
    public static readonly Color DangerText = Color.FromArgb(0xDC, 0x26, 0x26);
    public static readonly Color DangerSoft = Color.FromArgb(0xFE, 0xE2, 0xE2);
    public static readonly Color DangerSoftHover = Color.FromArgb(0xFC, 0xD3, 0xD3);
    public static readonly Color DangerSoftPressed = Color.FromArgb(0xFA, 0xC0, 0xC0);
    public static readonly Color ToggleOff = Color.FromArgb(0xD5, 0xD8, 0xE0);
    public static readonly Color GhostHover = Color.FromArgb(0xF3, 0xF4, 0xF7);
    public static readonly Color GhostPressed = Color.FromArgb(0xEA, 0xEC, 0xF0);

    public static readonly Font TitleFont = MakeFont("Segoe UI Variable Display", 15f, FontStyle.Bold);
    public static readonly Font SectionFont = MakeFont("Segoe UI Variable Text", 11f, FontStyle.Bold);
    public static readonly Font BodyFont = MakeFont("Segoe UI Variable Text", 10f, FontStyle.Regular);
    public static readonly Font SmallFont = MakeFont("Segoe UI Variable Text", 8.5f, FontStyle.Regular);
    public static readonly Font TinyFont = MakeFont("Segoe UI Variable Text", 8f, FontStyle.Regular);

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

    /// <summary>Soft layered shadow painted UNDER a card.</summary>
    public static void DrawSoftShadow(Graphics g, Rectangle bounds, int radius)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawShadowLayer(g, bounds, radius + 12, 12, 6);
        DrawShadowLayer(g, bounds, radius + 6, 5, 10);
        DrawShadowLayer(g, bounds, radius + 2, 2, 18);
        g.SmoothingMode = old;
    }

    private static void DrawShadowLayer(Graphics g, Rectangle b, int radius, int dy, int alpha)
    {
        var r = new Rectangle(b.X, b.Y + dy, b.Width, b.Height);
        using var path = RoundedRect(r, radius);
        using var brush = new SolidBrush(Color.FromArgb(alpha, 0x1B, 0x1E, 0x28));
        g.FillPath(brush, path);
    }
}

/// <summary>White card, 16px corners, hairline border.</summary>
public sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 16;
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
/// Rounded button with hover/pressed states, optional left glyph and
/// right-aligned accessory text (hero style). Inherits Control (not Button)
/// on purpose: the native Win32 BUTTON window paints a themed 1px dark edge
/// around custom Regions that no managed setting can fully suppress.
/// Mouse click still raises Click; keyboard activation is unnecessary here
/// (all actions have global hotkeys).
/// </summary>
public sealed class RoundedButton : Control
{
    public enum Kind { Primary, Soft, Ghost, Link, DangerSoft }
    public enum Glyph { None, Grid, Play, Pause, Pin, Unpin }

    public Kind ButtonKind { get; set; } = Kind.Primary;
    public Glyph IconGlyph { get; set; } = Glyph.None;
    public string? RightText { get; set; }
    /// <summary>Hero style: text left-aligned after the glyph instead of centred.</summary>
    public bool AlignLeft { get; set; }
    public int Radius { get; set; } = 12;

    private bool _hover;
    private bool _pressed;

    public RoundedButton()
    {
        Font = Theme.BodyFont;
        Cursor = Cursors.Hand;
        TabStop = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        SetStyle(ControlStyles.Selectable, false);
    }

    /// <summary>
    /// Corners outside the rounded path are painted with BackColor; inheriting
    /// the parent's colour keeps them invisible even on dark-themed systems
    /// where the default Control colour renders almost black.
    /// </summary>
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (Parent != null) BackColor = Parent.BackColor;
    }

    /// <summary>
    /// Clip the control to the rounded shape itself, so nothing the button (or
    /// a quirky theme) paints can leak into the corners – the parent's
    /// background shows through instead.
    /// </summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0 || Height <= 0) return;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Radius);
        Region?.Dispose();
        Region = new Region(path);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
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
            case Kind.Link:
                fill = _pressed ? Theme.GhostPressed : _hover ? Theme.GhostHover : Theme.Card;
                fore = _hover ? Theme.AccentHover : Theme.Accent; break;
            case Kind.DangerSoft:
                fill = _pressed ? Theme.DangerSoftPressed : _hover ? Theme.DangerSoftHover : Theme.DangerSoft;
                fore = Theme.DangerText; break;
            default:
                fill = _pressed ? Theme.GhostPressed : _hover ? Theme.GhostHover : Theme.Card;
                fore = Theme.Text; break;
        }
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        if (ButtonKind == Kind.Ghost)
        {
            using var pen = new Pen(Theme.CardBorder, 1);
            g.DrawPath(pen, path);
        }

        int textLeft = 0;
        if (IconGlyph != Glyph.None)
        {
            var glyphColor = ButtonKind == Kind.Primary ? fore : Theme.TextSecondary;
            DrawGlyph(g, IconGlyph, 18, (Height - 20) / 2, 20, glyphColor);
            textLeft = 30;
        }

        var textRect = new Rectangle(rect.X + textLeft, rect.Y, rect.Width - textLeft, rect.Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= AlignLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        if (AlignLeft) textRect.X += 6;
        TextRenderer.DrawText(g, Text, Font, textRect, fore, flags);

        if (!string.IsNullOrEmpty(RightText))
        {
            var rr = new Rectangle(rect.X, rect.Y, rect.Width - 18, rect.Height);
            using var f = new Font(Font.FontFamily, Font.Size - 1.5f, FontStyle.Regular);
            TextRenderer.DrawText(g, RightText, f, rr, Color.FromArgb(200, fore),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Simple 20x20 vector glyphs drawn in the given colour.</summary>
    internal static void DrawGlyph(Graphics g, Glyph glyph, int x, int y, int s, Color color)
    {
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (glyph)
        {
            case Glyph.Grid:
                FillRound(g, brush, x, y, 9, 20, 2);
                FillRound(g, brush, x + 12, y, 8, 9, 2);
                FillRound(g, brush, x + 12, y + 11, 8, 9, 2);
                break;
            case Glyph.Play:
                g.FillPolygon(brush, new[] { new Point(x + 6, y + 4), new Point(x + 6, y + 16), new Point(x + 16, y + 10) });
                break;
            case Glyph.Pause:
                g.FillRectangle(brush, x + 5, y + 4, 4, 12);
                g.FillRectangle(brush, x + 11, y + 4, 4, 12);
                break;
            case Glyph.Pin:
                g.FillEllipse(brush, x + 6, y + 2, 9, 9);
                g.DrawLine(pen, x + 10, y + 11, x + 10, y + 17);
                g.DrawLine(pen, x + 5, y + 18, x + 15, y + 18);
                break;
            case Glyph.Unpin:
                g.DrawEllipse(pen, x + 4, y + 4, 12, 12);
                g.DrawLine(pen, x + 6, y + 15, x + 15, y + 5);
                break;
        }
    }

    private static void FillRound(Graphics g, Brush b, int x, int y, int w, int h, int r)
    {
        using var path = Theme.RoundedRect(new Rectangle(x, y, w, h), r);
        g.FillPath(b, path);
    }
}

/// <summary>iOS-style toggle switch, 44x24.</summary>
public sealed class ToggleSwitch : Control
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        Size = new Size(44, 24);
        Cursor = Cursors.Hand;
        BackColor = Theme.Card;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(rect, Height / 2))
        using (var brush = new SolidBrush(Checked ? Theme.Accent : Theme.ToggleOff))
            g.FillPath(brush, path);

        int d = Height - 6;
        int kx = Checked ? Width - d - 3 : 3;
        using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            g.FillEllipse(shadow, kx, 4, d, d);
        using (var knob = new SolidBrush(Color.White))
            g.FillEllipse(knob, kx, 3, d, d);
    }
}

/// <summary>
/// Margin input: white rounded box with a grey edge label (左/上/右/下)
/// on the left and a borderless numeric field on the right.
/// v1.4.1: plain TextBox instead of NumericUpDown – the hidden-spin-button
/// hack rendered as black specks on dark-themed systems.
/// </summary>
public sealed class MarginBox : Panel
{
    private const int MaxValue = 800;
    private readonly TextBox _box;
    public event EventHandler? ValueChanged;

    public decimal Value
    {
        get => int.TryParse(_box.Text, out var v) ? v : 0m;
        set
        {
            int v = Math.Clamp((int)value, 0, MaxValue);
            _box.Text = v.ToString();
        }
    }

    public MarginBox(string edgeLabel)
    {
        Height = 36;
        BackColor = Theme.Card;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.ResizeRedraw, true);

        var lab = new Label
        {
            Text = edgeLabel,
            Font = Theme.SmallFont,
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Card,
            AutoSize = true,
            Left = 10,
            Top = 10
        };
        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Card,
            Font = Theme.BodyFont,
            ForeColor = Theme.Text,
            Left = 36,
            Top = 8,
            Width = 44,
            TextAlign = HorizontalAlignment.Right,
            Text = "0"
        };
        _box.KeyPress += (_, e) =>
        {
            // digits and editing keys only – the box holds pixel margins
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != '\b')
                e.Handled = true;
        };
        _box.TextChanged += (s, e) =>
        {
            // live-update the setting while typing; empty text is ignored
            if (int.TryParse(_box.Text, out _)) ValueChanged?.Invoke(s, e);
        };
        _box.Leave += (_, _) =>
        {
            // normalize on the way out: clamp to range, drop leading zeros
            int v = int.TryParse(_box.Text, out var parsed) ? parsed : 0;
            v = Math.Clamp(v, 0, MaxValue);
            if (_box.Text != v.ToString()) _box.Text = v.ToString();
        };
        Controls.Add(lab);
        Controls.Add(_box);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, 8);
        using var pen = new Pen(Color.FromArgb(0xE3, 0xE5, 0xEA), 1);
        e.Graphics.DrawPath(pen, path);
    }
}

/// <summary>
/// Excluded-app list: rows with a two-letter avatar chip, exe name and a
/// per-row delete "x". Scrolls when the content overflows.
/// </summary>
public sealed class BlacklistView : Panel
{
    public event Action<string>? ItemDeleted;

    private const int RowH = 46;
    private List<string> _items = new();

    public BlacklistView()
    {
        BackColor = Theme.Card;
        AutoScroll = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void SetItems(IEnumerable<string> items)
    {
        _items = items.ToList();
        AutoScrollMinSize = new Size(0, _items.Count * RowH);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, "暂无排除应用", Theme.SmallFont,
                new Rectangle(0, 24, Width, 20), Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            return;
        }

        int y0 = AutoScrollPosition.Y;
        for (int i = 0; i < _items.Count; i++)
        {
            var row = new Rectangle(0, y0 + i * RowH, Width, RowH);
            if (row.Bottom < 0 || row.Top > Height) continue;

            if (i > 0)
            {
                using var pen = new Pen(Theme.Divider, 1);
                g.DrawLine(pen, 48, row.Top, Width - 8, row.Top);
            }

            // avatar chip with two-letter initials
            var chip = new Rectangle(8, row.Top + 8, 30, 30);
            using (var path = Theme.RoundedRect(chip, 9))
            using (var brush = new SolidBrush(Theme.AccentSoft))
                g.FillPath(brush, path);
            var initials = _items[i].Length <= 2 ? _items[i].ToUpperInvariant()
                                                 : _items[i].Substring(0, 2).ToUpperInvariant();
            TextRenderer.DrawText(g, initials, Theme.TinyFont, chip, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // exe name
            TextRenderer.DrawText(g, _items[i], Theme.BodyFont, new Point(48, row.Top + 15),
                Theme.Text, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            // delete x
            int cx = Width - 21, cy = row.Top + RowH / 2;
            using var xpen = new Pen(Theme.TextSecondary, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(xpen, cx - 4, cy - 4, cx + 4, cy + 4);
            g.DrawLine(xpen, cx - 4, cy + 4, cx + 4, cy - 4);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int idx = (e.Y - AutoScrollPosition.Y) / RowH;
        if (idx < 0 || idx >= _items.Count) return;
        var delRect = new Rectangle(Width - 36, AutoScrollPosition.Y + idx * RowH + 5, 28, 36);
        if (delRect.Contains(e.Location)) ItemDeleted?.Invoke(_items[idx]);
    }
}

/// <summary>App-icon badge: gradient rounded square with the tiling grid.</summary>
public sealed class BadgeView : Control
{
    public BadgeView()
    {
        Size = new Size(54, 54);
        BackColor = Theme.Background;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(rect, 15))
        using (var brush = new LinearGradientBrush(rect,
                   Color.FromArgb(0x38, 0xBD, 0xF8), Color.FromArgb(0x25, 0x63, 0xEB), 45f))
            g.FillPath(brush, path);

        int u = Width / 54; // scale unit (1 at 54px)
        using var tile = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
        FillRound(g, tile, 13 * u, 13 * u, 15 * u, 28 * u, 4);
        FillRound(g, tile, 31 * u, 13 * u, 10 * u, 12 * u, 3);
        FillRound(g, tile, 31 * u, 28 * u, 10 * u, 13 * u, 3);
    }

    private static void FillRound(Graphics g, Brush b, int x, int y, int w, int h, int r)
    {
        using var path = Theme.RoundedRect(new Rectangle(x, y, w, h), r);
        g.FillPath(b, path);
    }
}

/// <summary>Small light-grey pill (version tag).</summary>
public sealed class PillLabel : Control
{
    public PillLabel()
    {
        Size = new Size(46, 22);
        BackColor = Theme.Background;
        Font = Theme.SmallFont;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(rect, Height / 2))
        using (var brush = new SolidBrush(Color.FromArgb(0xEF, 0xF0, 0xF3)))
            g.FillPath(brush, path);
        TextRenderer.DrawText(g, Text, Font, rect, Theme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Tiny status dot in a configurable colour.</summary>
public sealed class StatusDot : Control
{
    private Color _dotColor = Theme.Green;
    public Color DotColor
    {
        get => _dotColor;
        set { _dotColor = value; Invalidate(); }
    }

    public StatusDot()
    {
        Size = new Size(8, 8);
        BackColor = Theme.Background;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var halo = new SolidBrush(Color.FromArgb(60, _dotColor));
        g.FillEllipse(halo, -2, -2, Width + 4, Height + 4);
        using var core = new SolidBrush(_dotColor);
        g.FillEllipse(core, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>
/// Window-chrome button for the borderless form: thin line glyphs
/// (min / max / close), Win11-style hover background, close turns red.
/// </summary>
public sealed class CaptionButton : Control
{
    public enum CapGlyph { Min, Max, Close }
    public CapGlyph Glyph { get; set; } = CapGlyph.Close;

    private static readonly Color CloseHover = Color.FromArgb(0xE8, 0x11, 0x23);
    private bool _hover;
    private bool _pressed;

    public CaptionButton()
    {
        Size = new Size(46, 32);
        BackColor = Theme.Background;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_pressed || _hover)
        {
            Color bg = Glyph == CapGlyph.Close
                ? (_pressed ? Color.FromArgb(0xC4, 0x0E, 0x1D) : CloseHover)
                : (_pressed ? Color.FromArgb(0x1F, 0x1B, 0x1E, 0x28) : Color.FromArgb(0x12, 0x1B, 0x1E, 0x28));
            using var brush = new SolidBrush(bg);
            g.FillRectangle(brush, ClientRectangle);
        }

        Color fore = _hover && Glyph == CapGlyph.Close ? Color.White : Theme.Text;
        using var pen = new Pen(fore, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = Width / 2, cy = Height / 2;
        switch (Glyph)
        {
            case CapGlyph.Min:
                g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                break;
            case CapGlyph.Max:
                g.DrawRectangle(pen, cx - 4, cy - 4, 9, 9);
                break;
            default:
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(pen, cx - 5, cy + 5, cx + 5, cy - 5);
                break;
        }
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
