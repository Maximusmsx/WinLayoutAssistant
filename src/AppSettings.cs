using System.Text.Json;

namespace WinLayoutAssistant;

public sealed class AppSettings
{
    public TileEngine.Mode LayoutMode { get; set; } = TileEngine.Mode.Bsp;
    public int SnapThreshold { get; set; } = 24;          // px: how close to a 1/n line counts as "snap"
    public bool RunAtStartup { get; set; } = false;
    public bool TopMost { get; set; } = true;            // v1.2: this window stays on top while running
    public bool AutoLayoutEnabled { get; set; } = true;  // v1.2: false = pause drag snap + reflow
    public string TileHotkey { get; set; } = "Ctrl+Alt+L"; // one-click tile
    public string UnpinHotkey { get; set; } = "Ctrl+Alt+U"; // unpin all

    /// <summary>Exe names (case-insensitive, one per line in the UI) that are
    /// never tiled / never reflowed. Matched against the process exe file name,
    /// e.g. "TextInputHost.exe".</summary>
    public List<string> BlacklistExeNames { get; set; } = DefaultBlacklist();

    public static List<string> DefaultBlacklist() => new()
    {
        // Input methods / IME
        "ctfmon.exe", "TextInputHost.exe", "ChsIME.exe", "SogouInput.exe",
        "QQPinyin.exe", "WeChatInput.exe", "BaiduInput.exe", "SogouTS.exe",
        // Windows shell helpers (not user windows)
        "SearchHost.exe", "StartMenuExperienceHost.exe", "ShellExperienceHost.exe",
        "Widgets.exe", "WidgetBoard.exe",
        // Common floating widgets / bubbles
        "Rainmeter.exe", "DesktopWidget.exe",
    };

    public bool IsBlacklisted(string? exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return false;
        var name = exeName.Trim().ToLowerInvariant();
        return BlacklistExeNames.Any(b => b.Trim().ToLowerInvariant() == name);
    }

    private static string Path =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinLayoutAssistant", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
                s.BlacklistExeNames ??= DefaultBlacklist();
                return s;
            }
        }
        catch { /* corrupt file -> defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are best-effort */ }
    }
}
