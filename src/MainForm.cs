using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinLayoutAssistant;

/// <summary>
/// Tray app. The form itself is the settings window (hidden to tray on close).
/// Hotkeys: Ctrl+Alt+L = tile now, Ctrl+Alt+U = unpin all.
/// v1.1 fixes: tray "退出" really exits (v1.0 bug: Close() was cancelled by
/// OnFormClosing and only hid to tray, forcing a Task Manager kill);
/// default layout is now BSP (fills 100%, no empty grid cells);
/// exe-name blacklist with a one-click "add active window" button.
/// </summary>
public sealed class MainForm : Form
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2;
    private const int WM_HOTKEY = 0x0312;

    private bool _reallyExit; // set only by tray "退出" -> OnFormClosing must not cancel

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HashSet<IntPtr> _pinned = new();
    private readonly DragMonitor _drag;
    private readonly NotifyIcon _tray;
    private readonly ComboBox _modeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, Font = Theme.BodyFont, FlatStyle = FlatStyle.Flat };
    private readonly NumericUpDown _threshold = new() { Minimum = 8, Maximum = 80, Width = 80, Font = Theme.BodyFont };
    private readonly CheckBox _startup = new() { Text = "开机自启动", AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.Text, BackColor = Theme.Card };
    private readonly CheckBox _topmost = new() { Text = "本窗口始终置顶", AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.Text, BackColor = Theme.Card };
    private readonly RoundedButton _autoBtn = new() { Width = 396, Height = 44, ButtonKind = RoundedButton.Kind.Soft };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Theme.TextSecondary, Font = Theme.SmallFont, BackColor = Theme.Background };
    private readonly List<RoundedPanel> _cards = new();
    private readonly ListBox _blacklistBox = new() { Width = 396, Height = 92, Font = Theme.BodyFont, BorderStyle = BorderStyle.None, BackColor = Theme.Well };
    private readonly TextBox _blacklistInput = new() { Width = 220, Font = Theme.BodyFont, PlaceholderText = "例如: WeChat.exe", BorderStyle = BorderStyle.FixedSingle };

    public MainForm()
    {
        Text = "WinLayoutAssistant";
        Width = 460; Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Theme.Background;
        Font = Theme.BodyFont;
        ForeColor = Theme.Text;

        _modeBox.Items.AddRange(new object[] { "BSP 平铺 · 默认，占满全部空间", "网格平铺 (Grid)", "横向分列 (Columns)", "纵向分行 (Rows)" });
        _modeBox.SelectedIndex = (int)_settings.LayoutMode;
        _threshold.Value = _settings.SnapThreshold;
        _startup.Checked = _settings.RunAtStartup;
        _topmost.Checked = _settings.TopMost;
        TopMost = _settings.TopMost; // v1.2: stay on top while running, switchable below
        RefreshBlacklistBox();
        UpdateAutoButton();

        // --- header: navy→indigo gradient, icon badge, title, version pill ---
        var header = new GradientHeader
        {
            Top = 0, Left = 0, Width = 460, Height = 96,
            TitleText = "WinLayoutAssistant",
            SubText = "桌面布局小助手  ·  拖拽磁吸 1/n  ·  松手自动重排",
            VersionText = "v1.4"
        };
        var accentBar = new Panel { Top = 96, Left = 0, Width = 460, Height = 3, BackColor = Theme.Accent };

        // --- card 1: actions ---
        var card1 = new RoundedPanel { Top = 118, Left = 24, Width = 412, Height = 168 };
        _cards.Add(card1);
        var tileBtn = new RoundedButton { Text = "一键平铺所有窗口    Ctrl+Alt+L", Width = 380, Height = 48, Top = 16, Left = 16, ButtonKind = RoundedButton.Kind.Primary };
        tileBtn.Click += (_, _) => TileAll();
        _autoBtn.Top = 76; _autoBtn.Left = 16;
        _autoBtn.Click += (_, _) => ToggleAutoLayout();
        var pinBtn = new RoundedButton { Text = "固定当前窗口", Width = 184, Height = 36, Top = 132, Left = 16, ButtonKind = RoundedButton.Kind.Ghost };
        pinBtn.Click += (_, _) => PinActiveWindow();
        var unpinBtn = new RoundedButton { Text = "取消全部固定  Ctrl+Alt+U", Width = 184, Height = 36, Top = 132, Left = 212, ButtonKind = RoundedButton.Kind.Ghost };
        unpinBtn.Click += (_, _) => UnpinAll();
        card1.Controls.AddRange(new Control[] { tileBtn, _autoBtn, pinBtn, unpinBtn });

        // --- card 2: layout settings ---
        var card2 = new RoundedPanel { Top = 302, Left = 24, Width = 412, Height = 132 };
        _cards.Add(card2);
        card2.Controls.Add(new SectionLabel("平铺方式") { Top = 16, Left = 16 });
        _modeBox.Top = 40; _modeBox.Left = 16;
        card2.Controls.Add(new Label { Text = "磁吸距离", Top = 78, Left = 16, AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.Text, BackColor = Theme.Card });
        _threshold.Top = 74; _threshold.Left = 100;
        card2.Controls.Add(new Label { Text = "px", Top = 78, Left = 188, AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.TextSecondary, BackColor = Theme.Card });
        _startup.Top = 76; _startup.Left = 230;
        _topmost.Top = 100; _topmost.Left = 16;
        card2.Controls.AddRange(new Control[] { _modeBox, _threshold, _startup, _topmost });

        // --- card 3: blacklist ---
        var card3 = new RoundedPanel { Top = 450, Left = 24, Width = 412, Height = 196 };
        _cards.Add(card3);
        card3.Controls.Add(new SectionLabel("黑名单 · 按 exe 名，不参与排布") { Top = 14, Left = 16 });
        _blacklistBox.Top = 40; _blacklistBox.Left = 16;
        _blacklistInput.Top = 140; _blacklistInput.Left = 16;
        var blAddBtn = new RoundedButton { Text = "添加", Width = 72, Height = 28, Top = 138, Left = 244, ButtonKind = RoundedButton.Kind.Soft };
        blAddBtn.Click += (_, _) => AddBlacklist(_blacklistInput.Text);
        var blDelBtn = new RoundedButton { Text = "删除选中", Width = 84, Height = 28, Top = 138, Left = 324, ButtonKind = RoundedButton.Kind.Ghost };
        blDelBtn.Click += (_, _) => RemoveSelectedBlacklist();
        var blActiveBtn = new RoundedButton { Text = "把当前活动窗口加入黑名单", Width = 380, Height = 32, Top = 172, Left = 16, ButtonKind = RoundedButton.Kind.Ghost };
        blActiveBtn.Click += (_, _) => AddActiveWindowToBlacklist();
        card3.Controls.AddRange(new Control[] { _blacklistBox, _blacklistInput, blAddBtn, blDelBtn, blActiveBtn });

        // --- footer: status + exit ---
        var dot = new GlowDot { Top = 662, Left = 24, Width = 12, Height = 12 };
        _status.Top = 660; _status.Left = 42; _status.Width = 380;
        var hint = new Label
        {
            Text = "拖动窗口靠近 1/2、1/3 … 1/9 的线会磁吸，松手后该窗口固定，其余窗口在剩余空间重排。\r\n点 × 最小化到托盘；托盘右键 → 退出 才是真正退出。一键平铺按窗口左上角从左到右、从上到下排序。",
            Top = 684, Left = 24, Width = 412, Height = 36, ForeColor = Theme.TextSecondary, Font = Theme.SmallFont, BackColor = Theme.Background
        };
        var exitBtn = new RoundedButton { Text = "退出程序", Width = 412, Height = 36, Top = 724, Left = 24, ButtonKind = RoundedButton.Kind.DangerSoft };
        exitBtn.Click += (_, _) => ExitApp();
        Controls.AddRange(new Control[] { header, accentBar, card1, card2, card3, dot, _status, hint, exitBtn });
        ClientSize = new Size(460, 772);

        _modeBox.SelectedIndexChanged += (_, _) => { _settings.LayoutMode = (TileEngine.Mode)_modeBox.SelectedIndex; _settings.Save(); };
        _threshold.ValueChanged += (_, _) => { _settings.SnapThreshold = (int)_threshold.Value; _settings.Save(); };
        _startup.CheckedChanged += (_, _) => { _settings.RunAtStartup = _startup.Checked; _settings.Save(); ApplyStartup(); };
        _topmost.CheckedChanged += (_, _) => { _settings.TopMost = _topmost.Checked; TopMost = _topmost.Checked; _settings.Save(); };

        // v1.4: use the packaged app icon (Assets/AppIcon.ico -> exe icon)
        // for both the title bar and the tray; fall back to the stock icon
        // when running unpackaged (e.g. dotnet run).
        Icon appIcon;
        try { appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { appIcon = SystemIcons.Application; }
        Icon = appIcon;

        _tray = new NotifyIcon
        {
            Icon = appIcon,
            Text = "WinLayoutAssistant - 桌面布局小助手",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

        _drag = new DragMonitor(() => Handle, _settings, _pinned);
        _drag.Enabled = _settings.AutoLayoutEnabled;
        _drag.DragEnded += (_, _) => ReflowUnpinned();

        RegisterHotKey(Handle, 1, MOD_CONTROL | MOD_ALT, (uint)Keys.L);
        RegisterHotKey(Handle, 2, MOD_CONTROL | MOD_ALT, (uint)Keys.U);
        RegisterHotKey(Handle, 3, MOD_CONTROL | MOD_ALT, (uint)Keys.P);
        UpdateStatus("就绪：已监听窗口拖拽，磁吸 1/n 开启。");
    }

    private void ExitApp()
    {
        _reallyExit = true;
        _tray.Visible = false;
        Application.Exit();
    }

    /// <summary>v1.2: start/stop automatic layout. Paused = no drag snap,
    /// no snap on release, no reflow; the tray icon stays running and
    /// manual one-click tile (button / Ctrl+Alt+L) still works.</summary>
    private void ToggleAutoLayout()
    {
        _settings.AutoLayoutEnabled = !_settings.AutoLayoutEnabled;
        _settings.Save();
        ApplyAutoLayout();
    }

    private void ApplyAutoLayout()
    {
        _drag.Enabled = _settings.AutoLayoutEnabled;
        UpdateAutoButton();
        UpdateStatus(_settings.AutoLayoutEnabled
            ? "自动布局已开启：拖动会磁吸，松手后其余窗口重排。"
            : "自动布局已停止：程序仍在托盘运行，拖动不再磁吸/重排；手动一键平铺仍可用。");
    }

    private void UpdateAutoButton()
    {
        _autoBtn.Text = _settings.AutoLayoutEnabled ? "●  自动布局运行中，点击停止  (Ctrl+Alt+P)" : "○  自动布局已停止，点击开始  (Ctrl+Alt+P)";
        _autoBtn.ButtonKind = _settings.AutoLayoutEnabled ? RoundedButton.Kind.Soft : RoundedButton.Kind.Ghost;
        _autoBtn.Invalidate();
    }

    /// <summary>
    /// Reading order by each window's top-left corner: group into rows first
    /// (top edges within 150px count as the same row), then left-to-right
    /// inside a row; rows top-to-bottom. This is what one-click tile uses
    /// instead of Z-order, so the result matches what's on screen now.
    /// </summary>
    public static List<IntPtr> SortByTopLeft(IEnumerable<IntPtr> windows)
    {
        var rects = windows
            .Select(w => (w, ok: WindowHelper.TryGetRect(w, out var r), r))
            .Where(t => t.ok)
            .OrderBy(t => t.r.Top).ThenBy(t => t.r.Left)
            .ToList();
        var rows = new List<List<(IntPtr w, Rectangle r)>>();
        foreach (var item in rects)
        {
            var row = rows.FirstOrDefault(r => Math.Abs(item.r.Top - r[0].r.Top) <= 150);
            if (row == null) { row = new List<(IntPtr, Rectangle)>(); rows.Add(row); }
            row.Add((item.w, item.r));
        }
        return rows.SelectMany(r => r.OrderBy(t => t.r.Left).Select(t => t.w)).ToList();
    }

    private ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new MenuColorTable()) };
        m.Items.Add("一键平铺所有窗口", null, (_, _) => TileAll());
        var autoItem = new ToolStripMenuItem("自动布局：开启", null, (_, _) => ToggleAutoLayout());
        autoItem.Checked = _settings.AutoLayoutEnabled;
        autoItem.Click += (_, _) => { autoItem.Checked = _settings.AutoLayoutEnabled; };
        m.Items.Add(autoItem);
        m.Items.Add("固定当前活动窗口", null, (_, _) => PinActiveWindow());
        m.Items.Add("取消全部固定", null, (_, _) => UnpinAll());
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("打开设置", null, (_, _) => { Show(); Activate(); });
        m.Items.Add("退出", null, (_, _) => ExitApp());
        return m;
    }

    private void RefreshBlacklistBox()
    {
        _blacklistBox.Items.Clear();
        foreach (var b in _settings.BlacklistExeNames) _blacklistBox.Items.Add(b);
    }

    private void AddBlacklist(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        if (_settings.BlacklistExeNames.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        _settings.BlacklistExeNames.Add(name);
        _settings.Save();
        RefreshBlacklistBox();
        _blacklistInput.Clear();
        UpdateStatus($"已加入黑名单：{name}，下次平铺不再排布它。");
    }

    private void RemoveSelectedBlacklist()
    {
        if (_blacklistBox.SelectedItem is not string sel) return;
        _settings.BlacklistExeNames.RemoveAll(b => b.Equals(sel, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        RefreshBlacklistBox();
        UpdateStatus($"已从黑名单移除：{sel}");
    }

    private void AddActiveWindowToBlacklist()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Handle)
        {
            UpdateStatus("先点一下要拉黑的窗口，让它成为活动窗口，再点这个按钮。");
            return;
        }
        string exe = WindowHelper.GetExeName(fg);
        if (string.IsNullOrEmpty(exe))
        {
            UpdateStatus($"取不到活动窗口的 exe 名（标题：{WindowHelper.GetTitle(fg)}），请手动输入 exe 名。");
            return;
        }
        AddBlacklist(exe);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            if (m.WParam.ToInt32() == 1) TileAll();
            else if (m.WParam.ToInt32() == 2) UnpinAll();
            else if (m.WParam.ToInt32() == 3) ToggleAutoLayout();
        }
        base.WndProc(ref m);
    }

    /// <summary>One-click tile: clear pins, tile every window on its own monitor.
    /// Order is by each window's current top-left corner (rows top-to-bottom,
    /// left-to-right inside a row), not Z-order, so the result matches the
    /// layout you see on screen right now.</summary>
    public void TileAll()
    {
        _pinned.Clear();
        var wins = WindowHelper.GetTileableWindows(Handle, _settings);
        if (wins.Count == 0) { UpdateStatus("没有可平铺的窗口（可能都被黑名单排除了）。"); return; }
        var ordered = SortByTopLeft(wins);
        foreach (var group in ordered.GroupBy(w => WindowHelper.GetWorkArea(w)))
        {
            var rects = TileEngine.Tile(group.Key, group.Count(), _settings.LayoutMode);
            int i = 0;
            foreach (var w in group) WindowHelper.MoveWindowTo(w, rects[i++]);
        }
        UpdateStatus($"已平铺 {wins.Count} 个窗口（{_settings.LayoutMode}，已占满，按左上角从左到右、从上到下）。");
    }

    private void PinActiveWindow()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Handle) { UpdateStatus("没有可固定的活动窗口。"); return; }
        _pinned.Add(fg);
        UpdateStatus($"已固定：{WindowHelper.GetTitle(fg)}（共 {_pinned.Count} 个固定窗口）");
    }

    private void UnpinAll()
    {
        _pinned.Clear();
        UpdateStatus("已取消全部固定。下一次平铺将重新排列所有窗口。");
    }

    /// <summary>
    /// After a manual drag: the dragged window is pinned; tile the rest,
    /// in their previous (Z) order, into the largest free rectangle left
    /// after subtracting all pinned windows from the work area.
    /// </summary>
    private void ReflowUnpinned()
    {
        _pinned.RemoveWhere(h => !WindowHelper.IsWindowVisible(h));
        var all = WindowHelper.GetTileableWindows(Handle, _settings);
        var rest = SortByTopLeft(all.Where(w => !_pinned.Contains(w)));
        if (rest.Count == 0) { UpdateStatus($"已固定 {_pinned.Count} 个窗口，没有剩余窗口需要重排。"); return; }

        var area = WindowHelper.GetWorkArea(rest[0]);
        var pinnedRects = _pinned
            .Where(h => WindowHelper.TryGetRect(h, out _))
            .Select(h => { WindowHelper.TryGetRect(h, out var r); return r; })
            .ToList();
        var free = TileEngine.LargestFree(area, pinnedRects);
        if (free.IsEmpty) { UpdateStatus("剩余空间不足，未重排其余窗口。"); return; }

        var rects = TileEngine.Tile(free, rest.Count, _settings.LayoutMode);
        for (int i = 0; i < rest.Count; i++) WindowHelper.MoveWindowTo(rest[i], rects[i]);
        UpdateStatus($"已固定 1 个窗口，其余 {rest.Count} 个已在剩余空间按原顺序重排。");
    }

    private void ApplyStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (_settings.RunAtStartup) key.SetValue("WinLayoutAssistant", Application.ExecutablePath);
            else key.DeleteValue("WinLayoutAssistant", false);
        }
        catch { /* startup registration is best-effort */ }
    }

    private void UpdateStatus(string s) { _status.Text = s; }

    /// <summary>Soft shadows under the three cards, painted before the cards.</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        foreach (var card in _cards)
            Theme.DrawSoftShadow(e.Graphics, card.Bounds, card.Radius);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // v1.0 bug: every Close() was cancelled here, so tray "退出" only hid
        // the form and the process could only be killed in Task Manager.
        // Now: only the window X button hides to tray; a real exit goes through.
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(1500, "WinLayoutAssistant", "已最小化到托盘，拖拽磁吸仍在运行。托盘右键 → 退出 才是真正退出。", ToolTipIcon.Info);
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnregisterHotKey(Handle, 1);
            UnregisterHotKey(Handle, 2);
            UnregisterHotKey(Handle, 3);
            _drag.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
