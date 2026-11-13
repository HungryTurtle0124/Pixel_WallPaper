using System.Diagnostics;
using Microsoft.Win32;
using PixelWallpaper.App;
using PixelWallpaper.Renderer.Windows;
using PixelWallpaper.Shared;

internal static class StartupVerification
{
    public static async Task RunAsync(string renderer)
    {
        CheckRegistration(Path.GetFullPath(renderer));
        CheckDesktopRetry();
        Require(RendererProtocol.FindWindow() == 0, "Stop wallpaper before testing stopped preferences");
        string path = Path.Combine(Path.GetTempPath(), $"PixelWallpaper.{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            using var settings = new RendererClient(renderer, store);
            using var tray = new RendererClient(renderer, store);
            await settings.ConnectAsync();
            await tray.ConnectAsync();
            var selected = new WallpaperSettings { Wallpaper = WallpaperSettings.MapleTree };
            await settings.ApplyAsync(selected);
            try
            {
                await tray.ApplyAsync(new WallpaperSettings { PerformanceMode = PerformanceMode.Smooth });
                throw new InvalidOperationException("A stale stopped-state write was accepted.");
            }
            catch (IOException) { }
            Require(tray.CurrentSettings == selected && store.Load(out _) == selected, "Stale client must refresh and preserve newer scene");
            var final = selected with { PerformanceMode = PerformanceMode.Smooth, PauseWhileFullscreen = false };
            await tray.ApplyAsync(final);
            await settings.ConnectAsync();
            Require(settings.CurrentSettings == final && store.Load(out _) == final, "Second client should observe the saved preferences");
            Console.WriteLine("PASS: stopped clients synchronize and reject stale preference writes.");
        }
        finally { File.Delete(path); }
    }

    private static void CheckRegistration(string renderer)
    {
        string keyPath = $@"Software\PixelWallpaper.Verification.{Guid.NewGuid():N}";
        try
        {
            var startup = new StartupRegistration(renderer, keyPath);
            Require(!startup.IsRegistered, "Startup must default off");
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue("OtherApplication", "preserved");
            startup.SetEnabled(true);
            Require(startup.IsRegistered && key.GetValueKind(StartupRegistration.ValueName) == RegistryValueKind.String &&
                Equals(key.GetValue(StartupRegistration.ValueName), $"\"{renderer}\" --startup"), "Startup must register the quoted renderer only");
            startup.SetEnabled(false);
            startup.SetEnabled(false);
            Require(!startup.IsRegistered && Equals(key.GetValue("OtherApplication"), "preserved"), "Disable must delete only the wallpaper entry");
            Console.WriteLine("PASS: per-user registration defaults off, uses a quoted direct renderer command, and disables cleanly.");
        }
        finally { Registry.CurrentUser.DeleteSubKey(keyPath, throwOnMissingSubKey: false); }
    }

    private static void CheckDesktopRetry()
    {
        int attempts = 0;
        TimeSpan interval = TimeSpan.FromMilliseconds(50);
        int result = StartupDesktop.WaitForHost(() => ++attempts == 3 ? 42 :
            throw new InvalidOperationException("Explorer is initializing."), TimeSpan.FromSeconds(2), interval);
        Require(result == 42 && attempts == 3, "Desktop discovery must retry transient failures");

        attempts = 0;
        var elapsed = Stopwatch.StartNew();
        try
        {
            StartupDesktop.WaitForHost<int>(() =>
            {
                attempts++;
                throw new InvalidOperationException("Explorer is unavailable.");
            }, TimeSpan.FromMilliseconds(150), interval);
            throw new InvalidOperationException("Unavailable Explorer did not time out.");
        }
        catch (TimeoutException error)
        {
            Require(error.InnerException is InvalidOperationException && elapsed.Elapsed < TimeSpan.FromSeconds(2) &&
                attempts is >= 2 and <= 6, "Readiness failure must be bounded and retain the underlying diagnostic");
        }
        Console.WriteLine("PASS: desktop discovery waits, retries, and times out with a diagnostic.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
