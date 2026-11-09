using System.Runtime.InteropServices;
using System.Text;

namespace WinLayoutAssistant;

/// <summary>Thin Win32 wrapper: enumerate / move / resize top-level windows.</summary>
public static class WindowHelper
{
    public const int SW_RESTORE = 9;
    public const int SWP_NOZORDER = 0x0004;
    public const int SWP_NOACTIVATE = 0x0010;
    public const int SWP_SHOWWINDOW = 0x0040;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    public static Rectangle RectToRectangle(RECT r) => Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    public static RECT RectangleToRect(Rectangle r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };

    public static string GetTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Exe file name of the process owning hWnd, e.g. "notepad.exe".
    /// Returns "" when it cannot be determined (elevated / protected process).</summary>
    public static string GetExeName(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return "";
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            var path = proc.MainModule?.FileName;
            return string.IsNullOrEmpty(path) ? proc.ProcessName + ".exe" : System.IO.Path.GetFileName(path);
        }
        catch { return ""; }
    }

    public static Rectangle GetWorkArea(IntPtr hWnd)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        var hMon = MonitorFromWindow(hWnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (hMon != IntPtr.Zero && GetMonitorInfo(hMon, ref mi)) return RectToRectangle(mi.rcWork);
        return Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
    }

    public static Rectangle GetWorkAreaAt(Point p)
    {
        var r = new RECT { Left = p.X, Top = p.Y, Right = p.X + 1, Bottom = p.Y + 1 };
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        var hMon = MonitorFromRect(ref r, 2);
        if (hMon != IntPtr.Zero && GetMonitorInfo(hMon, ref mi)) return RectToRectangle(mi.rcWork);
        return Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
    }

    /// <summary>All user windows worth tiling, in Z-order (topmost first).
    /// Skips the shell, invisible / minimized / tool windows, our own forms,
    /// and any window whose exe is on the blacklist.</summary>
    public static List<IntPtr> GetTileableWindows(IntPtr selfHandle, AppSettings? settings = null)
    {
        var list = new List<IntPtr>();
        var shell = GetShellWindow();
        EnumWindows((hWnd, _) =>
        {
            if (hWnd == shell || hWnd == selfHandle) return true;
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
            int ex = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0) return true;
            if (!GetWindowRect(hWnd, out var r) || r.Width < 50 || r.Height < 50) return true;
            string title = GetTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;
            // Skip our overlay / settings windows by title
            if (title is "WinLayoutAssistant" or "WinLayoutAssistant - Snap") return true;
            if (settings != null && settings.IsBlacklisted(GetExeName(hWnd))) return true;
            list.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static void MoveWindowTo(IntPtr hWnd, Rectangle r)
    {
        if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
        SetWindowPos(hWnd, IntPtr.Zero, r.X, r.Y, Math.Max(50, r.Width), Math.Max(50, r.Height),
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public static bool TryGetRect(IntPtr hWnd, out Rectangle r)
    {
        r = Rectangle.Empty;
        if (!GetWindowRect(hWnd, out var rc)) return false;
        r = RectToRectangle(rc);
        return true;
    }
}
