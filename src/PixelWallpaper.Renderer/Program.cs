using PixelWallpaper.Renderer.Rendering;
using PixelWallpaper.Renderer.Runtime;
using PixelWallpaper.Renderer.Scenes;
using PixelWallpaper.Renderer.Timing;
using PixelWallpaper.Renderer.Windows;
using PixelWallpaper.Renderer.Control;
using PixelWallpaper.Shared;

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
            var options = RendererArguments.Parse(args);
            using var instance = new Mutex(true, RendererProtocol.RendererMutex, out bool firstInstance);
            if (!firstInstance) { Console.Error.WriteLine("Pixel Wallpaper is already running."); return 2; }

            DesktopHostInfo? hostInfo = options.Windowed ? null : DesktopHostInfo.Discover();
            // Reverse disposal: control -> pacer -> scene -> D3D -> runtime -> host -> HWND.
            using var window = new RenderWindow(hostInfo?.DpiContext ?? 0);
            using var desktop = hostInfo is null ? null : new DesktopHost(window, hostInfo);
            using var runtime = new RuntimeStateManager(window);
            runtime.SetPauseWhileFullscreen(options.Settings.PauseWhileFullscreen);
            using var renderer = new D3D11Renderer(window.Handle, window.Width, window.Height);
            using IScene scene = SceneFactory.Create(renderer, options.Settings.Wallpaper);
            using var pacer = new FramePacer();
            pacer.SetFrameRate((int)options.Settings.PerformanceMode);
            using var control = new RendererControlWindow(window, pacer, runtime, options.Settings.Wallpaper);
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
            if (args.Contains("--background")) RendererDiagnostics.Write(error);
            else NativeMethods.MessageBoxW(0,
                "The renderer stopped and released its resources.\n\n" + error,
                "Pixel Wallpaper - rendering error", 0x10);
            return 1;
        }
    }
}
