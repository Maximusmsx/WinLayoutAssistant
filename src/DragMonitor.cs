using System.Runtime.InteropServices;

namespace WinLayoutAssistant;

/// <summary>
/// Watches user move/size of any window via WinEvents (no global mouse hook):
///   EVENT_SYSTEM_MOVESIZESTART (0x000A) / MOVESIZEEND (0x000B)
/// While a move is in progress, polls the window rect, shows the 1/n overlay
/// and live-snaps the preview. On release, applies the snap, marks the window
/// pinned, and reflows the remaining (unpinned) windows into the free space.
/// </summary>
public sealed class DragMonitor : IDisposable
{
    public event Action<IntPtr, Rectangle>? DragEnded; // (hwnd, finalRect)

    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private readonly WinEventDelegate _proc;
    private readonly IntPtr _hookStart, _hookEnd;
    private readonly System.Windows.Forms.Timer _poll;
    private readonly SnapOverlay _overlay = new();
    private readonly Func<IntPtr> _selfHandle;
    private readonly AppSettings _settings;
    private readonly HashSet<IntPtr> _pinned;

    private IntPtr _dragHwnd;
    private Rectangle _dragArea;
    private Rectangle _dragStartRect;
    private bool _dragWasResize;

    /// <summary>v1.2: false pauses all automatic layout - no snap preview,
    /// no snap on release, no reflow. Manual TileAll still works.</summary>
    public bool Enabled { get; set; } = true;

    public DragMonitor(Func<IntPtr> selfHandle, AppSettings settings, HashSet<IntPtr> pinned)
    {
        _selfHandle = selfHandle; _settings = settings; _pinned = pinned;
        _proc = OnWinEvent;
        _hookStart = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZESTART, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _hookEnd = SetWinEventHook(EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _poll = new System.Windows.Forms.Timer { Interval = 80 };
        _poll.Tick += (_, _) => PollDrag();
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (!Enabled) return;
        if (hwnd == IntPtr.Zero || hwnd == _selfHandle() || idObject != 0) return;
        if (!WindowHelper.IsWindowVisible(hwnd)) return;
        if (eventType == EVENT_SYSTEM_MOVESIZESTART)
        {
            _dragHwnd = hwnd;
            _dragArea = WindowHelper.GetWorkArea(hwnd);
            if (!WindowHelper.TryGetRect(hwnd, out _dragStartRect)) _dragStartRect = Rectangle.Empty;
            _dragWasResize = false;
            _poll.Start();
        }
        else if (eventType == EVENT_SYSTEM_MOVESIZEEND && hwnd == _dragHwnd)
        {
            _poll.Stop();
            _overlay.Hide();
            if (!WindowHelper.TryGetRect(hwnd, out var rect)) { _dragHwnd = IntPtr.Zero; return; }
            // Resize (size changed) vs move (only position changed).
            _dragWasResize = rect.Width != _dragStartRect.Width || rect.Height != _dragStartRect.Height;
            var snapped = _dragWasResize
                ? TileEngine.SnapResize(rect, _dragStartRect, _dragArea, _settings.SnapThreshold)
                : TileEngine.SnapToFraction(rect, _dragArea, _settings.SnapThreshold).rect;
            bool changed = snapped != rect;
            if (changed) WindowHelper.MoveWindowTo(hwnd, snapped);
            _pinned.Add(hwnd);
            _dragHwnd = IntPtr.Zero;
            // A resize only adjusts that one window: do NOT reflow the rest.
            // A move pins the window and reflows the rest into the free space.
            if (!_dragWasResize) DragEnded?.Invoke(hwnd, snapped);
        }
    }

    private void PollDrag()
    {
        if (_dragHwnd == IntPtr.Zero || !WindowHelper.TryGetRect(_dragHwnd, out var rect)) return;
        bool resizing = rect.Width != _dragStartRect.Width || rect.Height != _dragStartRect.Height;
        var snapped = resizing
            ? TileEngine.SnapResize(rect, _dragStartRect, _dragArea, _settings.SnapThreshold)
            : TileEngine.SnapToFraction(rect, _dragArea, _settings.SnapThreshold).rect;
        _overlay.ShowFor(snapped); // no-op when the rect did not change
    }

    public void Dispose()
    {
        _poll.Stop();
        if (_hookStart != IntPtr.Zero) UnhookWinEvent(_hookStart);
        if (_hookEnd != IntPtr.Zero) UnhookWinEvent(_hookEnd);
        _overlay.Dispose();
    }
}
