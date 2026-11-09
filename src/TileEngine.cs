namespace WinLayoutAssistant;

/// <summary>
/// Pure geometry: no Win32 calls here, so the rules are testable in isolation.
/// Work area = the monitor rectangle minus the taskbar (Screen.WorkingArea).
/// </summary>
public static class TileEngine
{
    public enum Mode { Bsp, Grid, Columns, Rows }

    /// <summary>Tile <paramref name="windows"/> (in the order given) into <paramref name="area"/>.</summary>
    public static List<Rectangle> Tile(Rectangle area, int count, Mode mode)
    {
        if (mode == Mode.Bsp) return TileBsp(area, count);
        var outRects = new List<Rectangle>();
        if (count <= 0) return outRects;
        int cols, rows;
        switch (mode)
        {
            case Mode.Columns: cols = count; rows = 1; break;
            case Mode.Rows: cols = 1; rows = count; break;
            default:
                cols = (int)Math.Ceiling(Math.Sqrt(count));
                rows = (int)Math.Ceiling((double)count / cols);
                break;
        }
        int cw = area.Width / cols, ch = area.Height / rows;
        for (int i = 0; i < count; i++)
        {
            int c = i % cols, r = i / cols;
            int x = area.X + c * cw;
            int y = area.Y + r * ch;
            int w = (c == cols - 1) ? area.Right - x : cw;   // last cell takes the remainder
            int h = (r == rows - 1) ? area.Bottom - y : ch;
            outRects.Add(new Rectangle(x, y, w, h));
        }
        return outRects;
    }

    /// <summary>
    /// BSP tiling: recursively split the longest side, proportional to how many
    /// windows go on each side. Every leaf gets exactly one window, so the
    /// work area is always 100% filled - no empty grid cells, for any count.
    /// 1 window = whole area, 2 = halves, 3 = 1/2 + 1/4 + 1/4, 5 = 2/5 + 3/5 split, etc.
    /// </summary>
    public static List<Rectangle> TileBsp(Rectangle area, int count)
    {
        var outRects = new List<Rectangle>();
        if (count <= 0) return outRects;
        TileBspRec(area, count, outRects);
        return outRects;
    }

    private static void TileBspRec(Rectangle area, int count, List<Rectangle> outRects)
    {
        if (count <= 1) { outRects.Add(area); return; }
        int leftCount = count / 2, rightCount = count - leftCount;
        bool splitVertically = area.Width >= area.Height; // split the longer side
        if (splitVertically)
        {
            int w1 = area.Width * leftCount / count;
            TileBspRec(new Rectangle(area.X, area.Y, w1, area.Height), leftCount, outRects);
            TileBspRec(new Rectangle(area.X + w1, area.Y, area.Width - w1, area.Height), rightCount, outRects);
        }
        else
        {
            int h1 = area.Height * leftCount / count;
            TileBspRec(new Rectangle(area.X, area.Y, area.Width, h1), leftCount, outRects);
            TileBspRec(new Rectangle(area.X, area.Y + h1, area.Width, area.Height - h1), rightCount, outRects);
        }
    }

    /// <summary>
    /// Subtract <paramref name="cut"/> from <paramref name="area"/> and return the
    /// remaining rectangles (0..4). Used to find free space after pinned windows.
    /// </summary>
    public static List<Rectangle> Subtract(Rectangle area, Rectangle cut)
    {
        var inter = Rectangle.Intersect(area, cut);
        var result = new List<Rectangle>();
        if (inter.IsEmpty) { result.Add(area); return result; }
        if (inter.Top > area.Top) result.Add(new Rectangle(area.X, area.Y, area.Width, inter.Top - area.Top));
        if (inter.Bottom < area.Bottom) result.Add(new Rectangle(area.X, inter.Bottom, area.Width, area.Bottom - inter.Bottom));
        if (inter.Left > area.Left) result.Add(new Rectangle(area.X, inter.Top, inter.Left - area.X, inter.Height));
        if (inter.Right < area.Right) result.Add(new Rectangle(inter.Right, inter.Top, area.Right - inter.Right, inter.Height));
        return result.Where(r => r.Width >= 50 && r.Height >= 50).ToList();
    }

    public static Rectangle LargestFree(Rectangle area, IEnumerable<Rectangle> pinned)
    {
        var free = new List<Rectangle> { area };
        foreach (var p in pinned)
        {
            var next = new List<Rectangle>();
            foreach (var f in free) next.AddRange(Subtract(f, p));
            free = next;
        }
        return free.OrderByDescending(r => (long)r.Width * r.Height).FirstOrDefault();
    }

    /// <summary>
    /// Snap a dragged rect to the 1/n grid (n = 2..9) inside <paramref name="area"/>.
    /// Returns the snapped rect and the grid lines it snapped to (for the overlay).
    /// A dimension snaps when it is within <paramref name="threshold"/> px of W/n or H/n,
    /// and an edge snaps when it is within threshold of a k/n grid line.
    /// </summary>
    public static (Rectangle rect, List<int> vLines, List<int> hLines) SnapToFraction(
        Rectangle rect, Rectangle area, int threshold = 24)
    {
        var vLines = new List<int>();
        var hLines = new List<int>();
        int x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;

        // --- width: snap to area.Width / n ---
        for (int n = 2; n <= 9; n++)
        {
            int target = area.Width / n;
            if (Math.Abs(w - target) <= threshold) { w = target; break; }
        }
        // --- height: snap to area.Height / n ---
        for (int n = 2; n <= 9; n++)
        {
            int target = area.Height / n;
            if (Math.Abs(h - target) <= threshold) { h = target; break; }
        }
        // --- left edge: snap to area.X + k * area.Width / n ---
        for (int n = 2; n <= 9; n++)
            for (int k = 0; k <= n; k++)
            {
                int line = area.X + area.Width * k / n;
                if (Math.Abs(x - line) <= threshold) { x = line; vLines.Add(line); goto LeftDone; }
            }
    LeftDone:
        // --- top edge: snap to area.Y + k * area.Height / n ---
        for (int n = 2; n <= 9; n++)
            for (int k = 0; k <= n; k++)
            {
                int line = area.Y + area.Height * k / n;
                if (Math.Abs(y - line) <= threshold) { y = line; hLines.Add(line); goto TopDone; }
            }
    TopDone:
        // --- right edge: snap so right == a grid line (keeps the snapped width) ---
        for (int n = 2; n <= 9; n++)
            for (int k = 0; k <= n; k++)
            {
                int line = area.X + area.Width * k / n;
                if (Math.Abs((x + w) - line) <= threshold) { x = line - w; vLines.Add(line); goto RightDone; }
            }
    RightDone:
        // --- bottom edge ---
        for (int n = 2; n <= 9; n++)
            for (int k = 0; k <= n; k++)
            {
                int line = area.Y + area.Height * k / n;
                if (Math.Abs((y + h) - line) <= threshold) { y = line - h; hLines.Add(line); goto BottomDone; }
            }
    BottomDone:
        // Clamp inside the work area
        x = Math.Max(area.X, Math.Min(x, area.Right - 50));
        y = Math.Max(area.Y, Math.Min(y, area.Bottom - 50));
        return (new Rectangle(x, y, w, h), vLines.Distinct().ToList(), hLines.Distinct().ToList());
    }

    /// <summary>
    /// Resize-aware snap. SnapToFraction treats every drag as a move: it snaps
    /// the left edge, then the right-edge pass moves the window again
    /// (x = line - w), which cancels a left/top-edge resize. So a resize
    /// from the left or top edge effectively never snapped.
    /// This version looks at which edges actually moved vs the drag-start rect
    /// and snaps only those: right edge moves -&gt; snap right edge to a k/n
    /// line, keep left fixed; left edge moves -&gt; snap left edge, keep right
    /// fixed; width/height snap to W/n, H/n when the size itself is near one.
    /// </summary>
    public static Rectangle SnapResize(Rectangle rect, Rectangle startRect, Rectangle area, int threshold = 24)
    {
        int x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;
        bool leftMoved = x != startRect.X, rightMoved = rect.Right != startRect.Right;
        bool topMoved = y != startRect.Y, bottomMoved = rect.Bottom != startRect.Bottom;

        // --- horizontal ---
        if (rightMoved && !leftMoved)
        {
            // Snap the moving right edge to the nearest k/n line, keep left fixed.
            int best = SnapLine(rect.Right, area.X, area.Width, threshold);
            if (best >= 0) w = best - x;
            else if (SnapSize(w, area.Width, threshold, out int tw)) w = tw;
        }
        else if (leftMoved && !rightMoved)
        {
            int right = rect.Right;
            int best = SnapLine(x, area.X, area.Width, threshold);
            if (best >= 0) { x = best; w = right - x; }
            else if (SnapSize(w, area.Width, threshold, out int tw)) { w = tw; x = right - w; }
        }
        else if (w != startRect.Width)
        {
            if (SnapSize(w, area.Width, threshold, out int tw)) w = tw;
        }

        // --- vertical ---
        if (bottomMoved && !topMoved)
        {
            int best = SnapLine(rect.Bottom, area.Y, area.Height, threshold);
            if (best >= 0) h = best - y;
            else if (SnapSize(h, area.Height, threshold, out int th)) h = th;
        }
        else if (topMoved && !bottomMoved)
        {
            int bottom = rect.Bottom;
            int best = SnapLine(y, area.Y, area.Height, threshold);
            if (best >= 0) { y = best; h = bottom - y; }
            else if (SnapSize(h, area.Height, threshold, out int th)) { h = th; y = bottom - h; }
        }
        else if (h != startRect.Height)
        {
            if (SnapSize(h, area.Height, threshold, out int th)) h = th;
        }

        x = Math.Max(area.X, Math.Min(x, area.Right - 50));
        y = Math.Max(area.Y, Math.Min(y, area.Bottom - 50));
        w = Math.Max(50, Math.Min(w, area.Right - x));
        h = Math.Max(50, Math.Min(h, area.Bottom - y));
        return new Rectangle(x, y, w, h);
    }

    private static int SnapLine(int pos, int origin, int size, int threshold)
    {
        for (int n = 2; n <= 9; n++)
            for (int k = 0; k <= n; k++)
            {
                int line = origin + size * k / n;
                if (Math.Abs(pos - line) <= threshold) return line;
            }
        return -1;
    }

    private static bool SnapSize(int value, int total, int threshold, out int snapped)
    {
        for (int n = 2; n <= 9; n++)
        {
            int target = total / n;
            if (Math.Abs(value - target) <= threshold) { snapped = target; return true; }
        }
        snapped = value;
        return false;
    }

    /// <summary>All 1/n grid lines (n=2..9) for drawing the overlay while dragging.</summary>
    public static (List<int> v, List<int> h) GridLines(Rectangle area)
    {
        var v = new HashSet<int>(); var h = new HashSet<int>();
        for (int n = 2; n <= 9; n++)
        {
            for (int k = 1; k < n; k++) { v.Add(area.X + area.Width * k / n); h.Add(area.Y + area.Height * k / n); }
        }
        return (v.OrderBy(i => i).ToList(), h.OrderBy(i => i).ToList());
    }
}
