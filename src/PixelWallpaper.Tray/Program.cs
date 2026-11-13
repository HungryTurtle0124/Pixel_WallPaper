namespace PixelWallpaper.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, "Local\\PixelWallpaper.Tray.Instance", out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        using var context = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        using var tray = new TrayControls(context);
        Application.Run(tray);
    }
}
