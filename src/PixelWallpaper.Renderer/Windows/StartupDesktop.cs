using System.ComponentModel;
using System.Diagnostics;

namespace PixelWallpaper.Renderer.Windows;

// Used only at sign-in, before creating any windows, subscriptions, or GPU resources.
internal static class StartupDesktop
{
    public static T WaitForHost<T>(Func<T> discover, TimeSpan timeout, TimeSpan interval)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { return discover(); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception or ArgumentException)
            {
                var remaining = timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException($"Windows startup could not find a usable Explorer desktop after {timeout.TotalSeconds:g} seconds. Open Settings and try Start Wallpaper. Last error: {error.Message}", error);
                Thread.Sleep(remaining < interval ? remaining : interval);
            }
        }
    }
}
