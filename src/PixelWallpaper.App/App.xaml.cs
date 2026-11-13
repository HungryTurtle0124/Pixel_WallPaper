using System.Windows;
using PixelWallpaper.Shared;

namespace PixelWallpaper.App;

public partial class App : Application
{
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, "Local\\PixelWallpaper.App.Instance", out bool first);
        if (!first) { RendererProtocol.Broadcast(RendererProtocol.OpenSettingsMessage); Shutdown(); return; }
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
