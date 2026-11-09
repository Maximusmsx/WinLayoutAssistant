namespace WinLayoutAssistant;

/// <summary>
/// v1.1 fix for the pink full-screen flicker:
///   * No TransparencyKey / magenta full-screen form any more.
///   * The overlay is ONLY as big as the snapped rectangle itself:
///     a small translucent blue box with a solid border, nothing else.
///   * Double-buffered, and only repainted when the snapped rect actually
///     changes (see DragMonitor.PollDrag). No grid lines are drawn at all -
///     the blue box IS the preview of where the window will land.
///   * Click-through + never takes focus.
/// v1.4: colour aligned with the app accent + white corner ticks.
/// </summary>
public sealed class SnapOverlay : Form
{
    private Rectangle _lastRect = Rectangle.Empty;

    public SnapOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Accent;
        Opacity = 0.20; // translucent fill; border is drawn opaque in OnPaint
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000020 /* WS_EX_TRANSPARENT: clicks pass through */ |
                          0x08000000 /* WS_EX_NOACTIVATE */;
            return cp;
        }
    }

    /// <summary>Returns false when nothing changed (caller can skip repaint).</summary>
    public bool ShowFor(Rectangle snapRect)
    {
        if (snapRect.IsEmpty) { Hide(); _lastRect = Rectangle.Empty; return false; }
        if (snapRect == _lastRect) return false; // no change -> no repaint, no flicker
        _lastRect = snapRect;
        Bounds = snapRect;
        if (!Visible) Show();
        Invalidate();
        return true;
    }

    public new void Hide()
    {
        _lastRect = Rectangle.Empty;
        base.Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Fill is the form's translucent accent background; draw the border.
        using (var pen = new Pen(Color.FromArgb(255, Theme.Accent), 2))
            g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);

        // White corner ticks, FancyZones-style.
        const int o = 5, L = 22; // inset, tick length
        using var tick = new Pen(Color.FromArgb(220, 255, 255, 255), 3)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        // top-left
        g.DrawLine(tick, o, o + L, o, o); g.DrawLine(tick, o, o, o + L, o);
        // top-right
        g.DrawLine(tick, Width - o - L, o, Width - o, o); g.DrawLine(tick, Width - o, o, Width - o, o + L);
        // bottom-left
        g.DrawLine(tick, o, Height - o - L, o, Height - o); g.DrawLine(tick, o, Height - o, o + L, Height - o);
        // bottom-right
        g.DrawLine(tick, Width - o - L, Height - o, Width - o, Height - o); g.DrawLine(tick, Width - o, Height - o, Width - o, Height - o - L);
    }
}
