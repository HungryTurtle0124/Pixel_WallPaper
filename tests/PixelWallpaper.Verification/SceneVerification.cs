using System.Diagnostics;
using System.Text.Json;
using PixelWallpaper.App;
using PixelWallpaper.Renderer.Scenes.Layers;
using PixelWallpaper.Shared;

internal static class SceneVerification
{
    public static void CheckAnimationTiming()
    {
        foreach (int fps in new[] { 15, 30, 60 })
        {
            var clock = new SpriteAnimationClock(8, 8);
            int changes = 0, previous = 0;
            for (int i = 0; i < fps * 10; i++)
            {
                clock.Update(1f / fps);
                if (clock.Frame != previous) changes++;
                previous = clock.Frame;
            }
            Require(changes == 80 && clock.Frame == 0, $"8 FPS animation at {fps} renderer FPS");
            clock.Update(0);
            Require(clock.Frame == 0, "Pause/resume zero delta must not advance animation");
            var sway = new SpriteAnimationClock(64, 8);
            int swayTicks = 0;
            for (int i = 0; i < fps * 8; i++)
            {
                int before = sway.Frame;
                sway.Update(1f / fps);
                if (sway.Frame != before) swayTicks++;
            }
            Require(swayTicks == 64 && sway.Frame == 0, $"8-second sway cycle at {fps} renderer FPS");
        }
        var irregular = new SpriteAnimationClock(8, 8);
        irregular.Update(.375f);
        Require(irregular.Frame == 3, "Animation advances correctly across late updates");
        Console.WriteLine("Animation clock: 8 FPS sprite animation and 8-second sway cycle at 15/30/60 render FPS; paused time does not advance either.");
    }

    public static async Task RunAsync(string executable)
    {
        CheckAnimationTiming();
        await ControllerVerification.RunAsync(executable, WallpaperSettings.RainyForest);
        await ControllerVerification.RunAsync(executable, WallpaperSettings.Campfire);
        await ControllerVerification.RunAsync(executable, WallpaperSettings.MapleTree);
        using var client = new RendererClient(executable);
        var options = new WallpaperSettings { PauseWhileFullscreen = false };
        try
        {
            await client.StartAsync(options);
            for (int cycle = 0; cycle < 6; cycle++)
            {
                nint oldEndpoint = RendererProtocol.FindWindow();
                RendererProtocol.GetWindowThreadProcessId(oldEndpoint, out uint oldId);
                using var old = Process.GetProcessById((int)oldId);
                options = options with { Wallpaper = (cycle % 3) switch
                {
                    0 => WallpaperSettings.Campfire, 1 => WallpaperSettings.MapleTree, _ => WallpaperSettings.RainyForest
                } };
                await client.ApplyAsync(options);
                nint current = RendererProtocol.FindWindow();
                RendererProtocol.GetWindowThreadProcessId(current, out uint newId);
                Require(old.HasExited && oldId != newId, "Old renderer must exit before replacement starts");
                Require(RendererProtocol.SceneFromCode(RendererProtocol.Send(current, RendererProtocol.ReadScene)) == options.Wallpaper,
                    "Replacement scene identity");
                Require(RendererProtocol.Send(current, RendererProtocol.ReadOptions) == RendererProtocol.Pack(options), "Options retained across scene switch");
                using var observer = new RendererClient(executable);
                await observer.ConnectAsync();
                await observer.ApplyAsync(options); // Same scene: attach/apply must not restart it.
                RendererProtocol.GetWindowThreadProcessId(RendererProtocol.FindWindow(), out uint attachedId);
                Require(attachedId == newId, "Reattaching same scene must keep the renderer process");
            }
            await client.StopAsync();
            Require(RendererProtocol.FindWindow() == 0, "Scene-switch shutdown cleanup");
        }
        finally { if (client.IsRunning) await client.StopAsync(); }
        Console.WriteLine("PASS: six switches across three scenes; old process exits, options persist, reattach is stable, final clean shutdown.");
    }

    public static async Task SampleAsync(int seconds)
    {
        if (seconds is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(seconds));
        nint endpoint = RendererProtocol.FindWindow();
        Require(endpoint != 0, "Start a renderer before sampling");
        RendererProtocol.GetWindowThreadProcessId(endpoint, out uint id);
        using var process = Process.GetProcessById((int)id);
        long frames = (long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount);
        long allocated = (long)RendererProtocol.Send(endpoint, RendererProtocol.ReadAllocationCount);
        var timer = Stopwatch.StartNew();
        await Task.Delay(seconds * 1000);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Scene = RendererProtocol.SceneFromCode(RendererProtocol.Send(endpoint, RendererProtocol.ReadScene)),
            FramesPerSecond = ((long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount) - frames) / timer.Elapsed.TotalSeconds,
            RendererThreadAllocatedBytes = (long)RendererProtocol.Send(endpoint, RendererProtocol.ReadAllocationCount) - allocated,
            PauseReasons = (int)RendererProtocol.Send(endpoint, RendererProtocol.ReadPauseReasons)
        }));
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
