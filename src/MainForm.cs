using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinLayoutAssistant;

/// <summary>
/// Tray app. The form itself is the settings window (hidden to tray on close).
/// Hotkeys: Ctrl+Alt+L = tile now, Ctrl+Alt+U = unpin all.
/// v1.4 restyle: airy light page (hero button, toggle switches, avatar-chip
/// exclusion list) following the approved mockup. Behaviour is unchanged:
/// same hotkeys, tiling, reflow, snap, blacklist and tray logic.
/// </summary>
public sealed class MainForm : Form
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;

    private bool _reallyExit; // set only by tray "退出" -> OnFormClosing must not cancel

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HashSet<IntPtr> _pinned = new();
    private readonly DragMonitor _drag;
    private readonly NotifyIcon _tray;

    private readonly ComboBox _modeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 216, Font = Theme.BodyFont, FlatStyle = FlatStyle.Flat };
    private readonly NumericUpDown _threshold = new() { Minimum = 8, Maximum = 80, Width = 72, Font = Theme.BodyFont };
    private readonly ToggleSwitch _startupSw = new();
    private readonly ToggleSwitch _topmostSw = new();
    private readonly RoundedButton _autoBtn = new() { ButtonKind = RoundedButton.Kind.Ghost, Font = Theme.SmallFont };
    private readonly Label _status = new() { AutoSize = false, Width = 316, Height = 18, ForeColor = Theme.TextSecondary, Font = Theme.SmallFont, BackColor = Theme.Background };
    private readonly Label _autoStatus = new() { AutoSize = false, Width = 116, Height = 18, ForeColor = Theme.TextSecondary, Font = Theme.SmallFont, BackColor = Theme.Background, TextAlign = ContentAlignment.TopRight };
    private readonly StatusDot _autoDot = new();
    private readonly StatusDot _statusDot = new();
    private readonly List<RoundedPanel> _cards = new();
    private readonly MarginBox _marginL = new("左");
    private readonly MarginBox _marginT = new("上");
    private readonly MarginBox _marginR = new("右");
    private readonly MarginBox _marginB = new("下");
    private readonly BlacklistView _blacklistView = new();
    private readonly Label _blacklistCount = new() { AutoSize = false, Width = 80, Height = 16, ForeColor = Theme.TextSecondary, Font = Theme.TinyFont, BackColor = Theme.Card, TextAlign = ContentAlignment.TopRight };
    private readonly TextBox _blacklistInput = new() { Width = 264, Font = Theme.BodyFont, PlaceholderText = "输入进程名，例如 WeChat.exe", BorderStyle = BorderStyle.FixedSingle };

    public MainForm()
    {
        Text = "WinLayoutAssistant";
        StartPosition = FormStartPosition.CenterScreen;
        // Borderless window: we draw our own min/max/close caption buttons
        // top-right; the header area acts as the drag region.
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        BackColor = Theme.Background;
        Font = Theme.BodyFont;
        ForeColor = Theme.Text;

        // custom window chrome, top-right (min + close only, no maximize)
        var capMin = new CaptionButton { Glyph = CaptionButton.CapGlyph.Min, Top = 0, Left = 480 - 92 };
        var capClose = new CaptionButton { Glyph = CaptionButton.CapGlyph.Close, Top = 0, Left = 480 - 46 };
        capMin.Click += (_, _) => WindowState = FormWindowState.Minimized;
        capClose.Click += (_, _) => Close(); // hides to tray, like the old X
        Controls.AddRange(new Control[] { capMin, capClose });

        _modeBox.Items.AddRange(new object[] { "BSP 平铺 · 默认，占满全部空间", "网格平铺 (Grid)", "横向分列 (Columns)", "纵向分行 (Rows)" });
        _modeBox.SelectedIndex = (int)_settings.LayoutMode;
        _threshold.Value = _settings.SnapThreshold;
        // set initial toggle states without firing CheckedChanged -> Save()
        _startupSw.Checked = _settings.RunAtStartup;
        _topmostSw.Checked = _settings.TopMost;
        _marginL.Value = _settings.MarginLeft;
        _marginT.Value = _settings.MarginTop;
        _marginR.Value = _settings.MarginRight;
        _marginB.Value = _settings.MarginBottom;
        TopMost = _settings.TopMost;
        RefreshBlacklist();
        UpdateAutoButton();

        // --- header: badge + title + version pill, directly on the page ---
        var badge = new BadgeView { Top = 26, Left = 24 };
        var title = new Label { Text = "WinLayoutAssistant", Top = 28, Left = 94, AutoSize = true, Font = Theme.TitleFont, ForeColor = Theme.Text, BackColor = Theme.Background };
        var sub = new Label { Text = "让每一个窗口，都恰到好处。", Top = 60, Left = 94, AutoSize = true, Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Theme.Background };
        var pill = new PillLabel { Text = "1.4", Top = 62, Left = 408 }; // bottom-right of the header, aligned with the tagline
        // header controls also act as drag region for the borderless window
        badge.MouseDown += DragOnHeader;
        title.MouseDown += DragOnHeader;
        sub.MouseDown += DragOnHeader;
        pill.MouseDown += DragOnHeader;

        // --- section row: 桌面布局 + auto-layout state ---
        var section = new Label { Text = "桌面布局", Top = 102, Left = 24, AutoSize = true, Font = Theme.SectionFont, ForeColor = Theme.Text, BackColor = Theme.Background };
        _autoDot.Top = 107; _autoDot.Left = 326;
        _autoStatus.Top = 102; _autoStatus.Left = 340;

        // --- hero: one-click tile ---
        var hero = new RoundedButton
        {
            Text = "一键平铺所有窗口",
            RightText = "Ctrl + Alt + L",
            IconGlyph = RoundedButton.Glyph.Grid,
            AlignLeft = true,
            ButtonKind = RoundedButton.Kind.Primary,
            Font = Theme.SectionFont,
            Radius = 14,
            Top = 128, Left = 24, Width = 432, Height = 56
        };
        hero.Click += (_, _) => TileAll();

        // --- three ghost buttons ---
        _autoBtn.IconGlyph = RoundedButton.Glyph.Play;
        _autoBtn.Top = 196; _autoBtn.Left = 24; _autoBtn.Width = 136; _autoBtn.Height = 44;
        _autoBtn.Click += (_, _) => ToggleAutoLayout();
        var pinBtn = new RoundedButton { Text = "固定当前窗口", IconGlyph = RoundedButton.Glyph.Pin, ButtonKind = RoundedButton.Kind.Ghost, Font = Theme.SmallFont, Top = 196, Left = 172, Width = 136, Height = 44 };
        pinBtn.Click += (_, _) => PinActiveWindow();
        var unpinBtn = new RoundedButton { Text = "取消全部固定", IconGlyph = RoundedButton.Glyph.Unpin, ButtonKind = RoundedButton.Kind.Ghost, Font = Theme.SmallFont, Top = 196, Left = 320, Width = 136, Height = 44 };
        unpinBtn.Click += (_, _) => UnpinAll();

        // --- card: layout preferences ---
        var card2 = new RoundedPanel { Top = 252, Left = 24, Width = 432, Height = 304 };
        _cards.Add(card2);
        card2.Controls.Add(new Label { Text = "布局偏好", Top = 16, Left = 20, AutoSize = true, Font = Theme.SectionFont, ForeColor = Theme.Text, BackColor = Theme.Card });
        card2.Controls.Add(new Label { Text = "LAYOUT", Top = 23, Left = 352, Width = 60, Font = Theme.TinyFont, ForeColor = Theme.TextSecondary, BackColor = Theme.Card, TextAlign = ContentAlignment.TopRight });
        card2.Controls.Add(new Label { Text = "平铺方式", Top = 62, Left = 20, AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.Text, BackColor = Theme.Card });
        _modeBox.Top = 56; _modeBox.Left = 196;
        card2.Controls.Add(Divider(100));
        card2.Controls.Add(new Label { Text = "磁吸距离", Top = 116, Left = 20, AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.Text, BackColor = Theme.Card });
        card2.Controls.Add(new Label { Text = "靠近屏幕分线时自动吸附", Top = 136, Left = 20, AutoSize = true, Font = Theme.TinyFont, ForeColor = Theme.TextSecondary, BackColor = Theme.Card });
        _threshold.Top = 112; _threshold.Left = 324;
        card2.Controls.Add(new Label { Text = "px", Top = 118, Left = 402, AutoSize = true, Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Theme.Card });
        card2.Controls.Add(Divider(160));
        card2.Controls.Add(new Label { Text = "屏幕边距", Top = 176, Left = 20, AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.Text, BackColor = Theme.Card });
        card2.Controls.Add(new Label { Text = "px", Top = 178, Left = 396, AutoSize = true, Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Theme.Card });
        _marginL.Top = 202; _marginL.Left = 20; _marginL.Width = 90;
        _marginT.Top = 202; _marginT.Left = 121; _marginT.Width = 90;
        _marginR.Top = 202; _marginR.Left = 222; _marginR.Width = 90;
        _marginB.Top = 202; _marginB.Left = 323; _marginB.Width = 90;
        card2.Controls.Add(Divider(250));
        card2.Controls.Add(new Label { Text = "开机自动启动", Top = 268, Left = 20, AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.Text, BackColor = Theme.Card });
        _startupSw.Top = 264; _startupSw.Left = 180;
        card2.Controls.Add(new Label { Text = "保持窗口置顶", Top = 268, Left = 250, AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.Text, BackColor = Theme.Card });
        _topmostSw.Top = 264; _topmostSw.Left = 368;
        card2.Controls.AddRange(new Control[] { _modeBox, _threshold, _marginL, _marginT, _marginR, _marginB, _startupSw, _topmostSw });

        // --- card: excluded apps ---
        var card3 = new RoundedPanel { Top = 576, Left = 24, Width = 432, Height = 344 };
        _cards.Add(card3);
        card3.Controls.Add(new Label { Text = "排除应用", Top = 16, Left = 20, AutoSize = true, Font = Theme.SectionFont, ForeColor = Theme.Text, BackColor = Theme.Card });
        _blacklistCount.Top = 22; _blacklistCount.Left = 332;
        _blacklistView.Top = 52; _blacklistView.Left = 12; _blacklistView.Width = 408; _blacklistView.Height = 184;
        _blacklistInput.Top = 252; _blacklistInput.Left = 20;
        var blAddBtn = new RoundedButton { Text = "+ 添加", ButtonKind = RoundedButton.Kind.Soft, Top = 248, Left = 316, Width = 96, Height = 34 };
        blAddBtn.Click += (_, _) => AddBlacklist(_blacklistInput.Text);
        var blActiveBtn = new Label
        {
            Text = "+ 将当前活动窗口加入排除列表",
            Font = Theme.SmallFont, ForeColor = Theme.Accent, BackColor = Theme.Card,
            AutoSize = false, Top = 294, Left = 20, Width = 392, Height = 32,
            TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand
        };
        blActiveBtn.Click += (_, _) => AddActiveWindowToBlacklist();
        blActiveBtn.MouseEnter += (_, _) => blActiveBtn.ForeColor = Theme.AccentHover;
        blActiveBtn.MouseLeave += (_, _) => blActiveBtn.ForeColor = Theme.Accent;
        card3.Controls.AddRange(new Control[] { _blacklistCount, _blacklistView, _blacklistInput, blAddBtn, blActiveBtn });

        // --- status row + footer band ---
        _statusDot.Top = 938; _statusDot.Left = 24;
        _status.Top = 934; _status.Left = 40;
        // prominent soft-red chip so the real exit is easy to find
        var exitLink = new RoundedButton
        {
            Text = "退出程序",
            ButtonKind = RoundedButton.Kind.DangerSoft,
            Font = Theme.SmallFont,
            Top = 926, Left = 366, Width = 90, Height = 30
        };
        exitLink.Click += (_, _) => ExitApp();
        var band = new Panel { Top = 964, Left = 0, Width = 480, Height = 46, BackColor = Theme.Well };
        band.Controls.Add(new Label
        {
            Text = "拖动即整理　靠近 ½、⅓ 或 ⅙ 分线即可磁吸，松手固定窗口，其余窗口自动重排。",
            Top = 16, Left = 0, Width = 480, Height = 16,
            Font = Theme.SmallFont, ForeColor = Theme.TextSecondary,
            BackColor = Theme.Well, TextAlign = ContentAlignment.TopCenter
        });

        Controls.AddRange(new Control[]
        {
            badge, title, sub, pill, section, _autoDot, _autoStatus,
            hero, _autoBtn, pinBtn, unpinBtn,
            card2, card3, _statusDot, _status, exitLink, band
        });
        ClientSize = new Size(480, 1010);
        // anchor only after the final size is set, otherwise the offset is
        // computed against the default size and the buttons end up off-screen
        capMin.Anchor = capClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        ActiveControl = null; // no focus rectangle on the link button at startup

        _modeBox.SelectedIndexChanged += (_, _) => { _settings.LayoutMode = (TileEngine.Mode)_modeBox.SelectedIndex; _settings.Save(); };
        _threshold.ValueChanged += (_, _) => { _settings.SnapThreshold = (int)_threshold.Value; _settings.Save(); };
        _startupSw.CheckedChanged += (_, _) => { _settings.RunAtStartup = _startupSw.Checked; _settings.Save(); ApplyStartup(); };
        _topmostSw.CheckedChanged += (_, _) => { _settings.TopMost = _topmostSw.Checked; TopMost = _topmostSw.Checked; _settings.Save(); };
        _marginL.ValueChanged += (_, _) => { _settings.MarginLeft = (int)_marginL.Value; _settings.Save(); };
        _marginT.ValueChanged += (_, _) => { _settings.MarginTop = (int)_marginT.Value; _settings.Save(); };
        _marginR.ValueChanged += (_, _) => { _settings.MarginRight = (int)_marginR.Value; _settings.Save(); };
        _marginB.ValueChanged += (_, _) => { _settings.MarginBottom = (int)_marginB.Value; _settings.Save(); };

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
        UpdateStatus("已就绪，正在监听窗口拖拽");
    }

    /// <summary>1px hairline divider used inside cards.</summary>
    private static Panel Divider(int top) =>
        new() { Top = top, Left = 20, Width = 392, Height = 1, BackColor = Theme.Divider };

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
            ? "自动布局已开启：拖动会磁吸，松手后其余窗口重排"
            : "自动布局已停止：拖动不再磁吸/重排，手动平铺仍可用");
    }

    private void UpdateAutoButton()
    {
        bool on = _settings.AutoLayoutEnabled;
        _autoBtn.Text = on ? "停止自动布局" : "开启自动布局";
        _autoBtn.IconGlyph = on ? RoundedButton.Glyph.Pause : RoundedButton.Glyph.Play;
        _autoBtn.Invalidate();
        _autoStatus.Text = on ? "自动布局运行中" : "自动布局已暂停";
        _autoDot.DotColor = on ? Theme.Green : Theme.Amber;
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

    private void RefreshBlacklist()
    {
        _blacklistView.SetItems(_settings.BlacklistExeNames);
        _blacklistCount.Text = $"{_settings.BlacklistExeNames.Count} 个应用";
    }

    private void AddBlacklist(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        if (_settings.BlacklistExeNames.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        _settings.BlacklistExeNames.Add(name);
        _settings.Save();
        RefreshBlacklist();
        _blacklistInput.Clear();
        UpdateStatus($"已加入黑名单：{name}，下次平铺不再排布它");
    }

    private void RemoveBlacklist(string name)
    {
        _settings.BlacklistExeNames.RemoveAll(b => b.Equals(name, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        RefreshBlacklist();
        UpdateStatus($"已从黑名单移除：{name}");
    }

    private void AddActiveWindowToBlacklist()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Handle)
        {
            UpdateStatus("先点一下要拉黑的窗口，让它成为活动窗口，再点这个按钮");
            return;
        }
        string exe = WindowHelper.GetExeName(fg);
        if (string.IsNullOrEmpty(exe))
        {
            UpdateStatus($"取不到活动窗口的 exe 名（标题：{WindowHelper.GetTitle(fg)}），请手动输入 exe 名");
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
        if (wins.Count == 0) { UpdateStatus("没有可平铺的窗口（可能都被黑名单排除了）"); return; }
        var ordered = SortByTopLeft(wins);
        foreach (var group in ordered.GroupBy(w => _settings.ApplyMargins(WindowHelper.GetWorkArea(w))))
        {
            var rects = TileEngine.Tile(group.Key, group.Count(), _settings.LayoutMode);
            int i = 0;
            foreach (var w in group) WindowHelper.MoveWindowTo(w, rects[i++]);
        }
        UpdateStatus($"已平铺 {wins.Count} 个窗口（{_settings.LayoutMode}，按左上角从左到右、从上到下）");
    }

    private void PinActiveWindow()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Handle) { UpdateStatus("没有可固定的活动窗口"); return; }
        _pinned.Add(fg);
        UpdateStatus($"已固定：{WindowHelper.GetTitle(fg)}（共 {_pinned.Count} 个固定窗口）");
    }

    private void UnpinAll()
    {
        _pinned.Clear();
        UpdateStatus("已取消全部固定。下一次平铺将重新排列所有窗口");
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
        if (rest.Count == 0) { UpdateStatus($"已固定 {_pinned.Count} 个窗口，没有剩余窗口需要重排"); return; }

        var area = _settings.ApplyMargins(WindowHelper.GetWorkArea(rest[0]));
        var pinnedRects = _pinned
            .Where(h => WindowHelper.TryGetRect(h, out _))
            .Select(h => { WindowHelper.TryGetRect(h, out var r); return r; })
            .ToList();
        var free = TileEngine.LargestFree(area, pinnedRects);
        if (free.IsEmpty) { UpdateStatus("剩余空间不足，未重排其余窗口"); return; }

        var rects = TileEngine.Tile(free, rest.Count, _settings.LayoutMode);
        for (int i = 0; i < rest.Count; i++) WindowHelper.MoveWindowTo(rest[i], rects[i]);
        UpdateStatus($"已固定 1 个窗口，其余 {rest.Count} 个已在剩余空间按原顺序重排");
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

    /// <summary>Soft shadows under the cards, painted before the cards.</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        foreach (var card in _cards)
            Theme.DrawSoftShadow(e.Graphics, card.Bounds, card.Radius);
    }

    /// <summary>Drop shadow for the borderless window (DWM class style).</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x20000;
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    /// <summary>Drag the borderless window by its header area.</summary>
    private void DragOnHeader(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        // header strip (above the hero button) doubles as the drag region
        if (e.Y <= 100) DragOnHeader(this, e);
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
