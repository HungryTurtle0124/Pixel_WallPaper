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
        bool startup = args is ["--startup"];
        try
        {
            if (args.Length == 1 && args[0] == "--stop")
                return DesktopHost.RequestStop() ? 0 : 1;
            using var instance = new Mutex(true, RendererProtocol.RendererMutex, out bool firstInstance);
            if (!firstInstance) { Console.Error.WriteLine("Pixel Wallpaper is already running."); return 2; }

            var options = startup ? null : RendererArguments.Parse(args);
            DesktopHostInfo? hostInfo = options?.Windowed == true ? null : startup
                ? StartupDesktop.WaitForHost(DesktopHostInfo.Discover, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(500))
                : DesktopHostInfo.Discover();
            // Reverse disposal: control -> pacer -> scene -> D3D -> runtime -> host -> HWND.
            using var window = new RenderWindow(hostInfo?.DpiContext ?? 0);
            // Finish any stopped-state preference write already in progress before loading.
            var settings = options?.Settings ?? RendererCommandGate.Run(() => new SettingsStore().Load(out _));
            using var desktop = hostInfo is null ? null : new DesktopHost(window, hostInfo);
            using var runtime = new RuntimeStateManager(window);
            runtime.SetPauseWhileFullscreen(settings.PauseWhileFullscreen);
            using var renderer = new D3D11Renderer(window.Handle, window.Width, window.Height);
            using IScene scene = SceneFactory.Create(renderer, settings.Wallpaper);
            using var pacer = new FramePacer();
            pacer.SetFrameRate((int)settings.PerformanceMode);
            using var control = new RendererControlWindow(window, pacer, runtime, settings.Wallpaper);
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
            if (startup || args.Contains("--background")) RendererDiagnostics.Write(error);
            else NativeMethods.MessageBoxW(0,
                "The renderer stopped and released its resources.\n\n" + error,
                "Pixel Wallpaper - rendering error", 0x10);
            return 1;
        }
    }
}
