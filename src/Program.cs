namespace WinLayoutAssistant;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new MainForm();
        // Start hidden in the tray; double-click the tray icon to open settings.
        Application.Run(form);
    }
}
