using PixelWallpaper.Renderer.Rendering;
using PixelWallpaper.Renderer.Runtime;
using PixelWallpaper.Renderer.Scenes;
using PixelWallpaper.Renderer.Timing;
using PixelWallpaper.Renderer.Windows;

namespace PixelWallpaper.Renderer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--stop")
                return DesktopHost.RequestStop() ? 0 : 1;
            bool windowed = args.Length == 1 && args[0] == "--windowed";
            if (args.Length != 0 && !windowed)
                throw new ArgumentException("Options: --windowed (normal window), --stop (clean shutdown), or no arguments (desktop wallpaper).");
            using var instance = new Mutex(true, "Local\\PixelWallpaper.Renderer.Instance", out bool firstInstance);
            if (!firstInstance) throw new InvalidOperationException("Pixel Wallpaper is already running. Use --stop before starting another instance.");

            DesktopHostInfo? hostInfo = windowed ? null : DesktopHostInfo.Discover();
            // Reverse disposal: pacer -> scene -> renderer -> runtime -> host -> owned HWND.
            using var window = new RenderWindow(hostInfo?.DpiContext ?? 0);
            using var desktop = hostInfo is null ? null : new DesktopHost(window, hostInfo);
            using var runtime = new RuntimeStateManager(window);
            using var renderer = new D3D11Renderer(window.Handle, window.Width, window.Height);
            string backgroundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Backgrounds", "rainy-forest.png");
            using IScene scene = new RainyForestScene(renderer, backgroundPath);
            using var pacer = new FramePacer();
            window.Show();

            while (pacer.WaitForFrame(window, renderer.FrameLatencyHandle, runtime))
            {
                renderer.Resize(window.Width, window.Height);
                scene.Update(pacer.BeginFrame());
                renderer.Render(scene);
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            NativeMethods.MessageBoxW(0,
                "The renderer stopped and released its resources.\n\n" + error,
                "Pixel Wallpaper - rendering error", 0x10);
            return 1;
        }
    }
}
