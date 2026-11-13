using System.Diagnostics;
using PixelWallpaper.Shared;

internal static class LifecycleVerification
{
    public static async Task StartProbeAsync(string executable, bool hold)
    {
        using var client = new RendererClient(executable);
        await client.StartAsync(new WallpaperSettings { Wallpaper = WallpaperSettings.MapleTree,
            PerformanceMode = PerformanceMode.Eco, PauseWhileFullscreen = false });
        RendererProtocol.GetWindowThreadProcessId(RendererProtocol.FindWindow(), out uint id);
        if (!hold) { Console.WriteLine(id); return; }
        RendererCommandGate.Run(() =>
        {
            Console.WriteLine(id); Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite); // Parent deliberately kills this test controller with the gate held.
            return true;
        });
    }

    public static async Task RunAsync(string executable)
    {
        Require(RendererProtocol.FindWindow() == 0, "Stop wallpaper before lifecycle tests");
        try
        {
            using var first = Probe(executable, false);
            using var second = Probe(executable, false);
            string?[] ids = await Task.WhenAll(first.StandardOutput.ReadLineAsync(), second.StandardOutput.ReadLineAsync()).WaitAsync(TimeSpan.FromSeconds(20));
            await Task.WhenAll(first.WaitForExitAsync(), second.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(15));
            Require(first.ExitCode == 0 && second.ExitCode == 0 && ids[0] == ids[1] && int.TryParse(ids[0], out _), "Concurrent controllers must adopt one renderer");
            int id = int.Parse(ids[0]!);
            using var renderer = Process.GetProcessById(id);
            Require(!renderer.HasExited, "Normal controller exit must leave renderer running");
            using var client = new RendererClient(executable);
            var live = await client.ConnectAsync();
            Require(live is { Wallpaper: WallpaperSettings.MapleTree, PerformanceMode: PerformanceMode.Eco, PauseWhileFullscreen: false }, "Live state authoritative after reconnect");
            var observed = await client.StartAsync(new WallpaperSettings());
            Require(observed == live && CurrentId() == id, "Start on existing renderer must not apply stale defaults");

            using var stale = new RendererClient(executable);
            await stale.ConnectAsync();
            live = await client.ApplyAsync(live! with { PerformanceMode = PerformanceMode.Smooth });
            bool rejected = false;
            try { await stale.ApplyAsync(stale.CurrentSettings! with { PauseWhileFullscreen = true }); }
            catch (IOException) { rejected = true; }
            Require(rejected && stale.CurrentSettings == live, "Stale full-settings update must fail and refresh actual state");
            live = await stale.ApplyAsync(stale.CurrentSettings! with { PauseWhileFullscreen = true });
            Require(live.PerformanceMode == PerformanceMode.Smooth, "Retry must preserve other controller's FPS choice");
            nint endpoint = RendererProtocol.FindWindow();
            Require(RendererProtocol.Send(endpoint, RendererProtocol.ApplyChecked, 15, -1) == 2, "Renderer rejects wrong revision atomically");
            Require(RendererProtocol.Send(endpoint, RendererProtocol.ReadOptions) == RendererProtocol.Pack(live), "Rejected command must not mutate settings");

            using var self = Process.GetCurrentProcess();
            for (int i = 0; i < 8; i++) { using var connection = new RendererClient(executable); await connection.ConnectAsync(); }
            self.Refresh(); int handles = self.HandleCount;
            for (int i = 0; i < 64; i++) { using var connection = new RendererClient(executable); await connection.ConnectAsync(); }
            self.Refresh(); Require(self.HandleCount - handles <= 4, "Reconnect/dispose handle growth");
            Console.WriteLine($"Concurrent Starts adopted PID {id}; live state won; stale update rejected; 64 reconnects handle delta {self.HandleCount - handles}.");
            await client.ConnectAsync();
            await client.StopAsync();
            Require(renderer.HasExited && RendererProtocol.FindWindow() == 0, "Explicit Stop after reconnect cleans endpoint/resources");

            using var crashing = Probe(executable, true);
            string? line = await crashing.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            using var independent = Process.GetProcessById(int.Parse(line!));
            crashing.Kill(); await crashing.WaitForExitAsync();
            await Task.Delay(1000);
            Require(!independent.HasExited, "Controller crash must not terminate renderer");
            using var recovered = new RendererClient(executable);
            live = await recovered.ConnectAsync(); // Also acquires the abandoned command mutex.
            Require(live?.Wallpaper == WallpaperSettings.MapleTree && CurrentId() == independent.Id, "Reconnect after controller crash/abandoned gate");

            independent.Kill(); await independent.WaitForExitAsync(); // Deliberate test renderer crash.
            Require(await recovered.ConnectAsync() is null && !recovered.IsRunning, "Crashed renderer must appear stopped");
            await recovered.StartAsync(new WallpaperSettings { Wallpaper = WallpaperSettings.Campfire });
            Require(CurrentId() != independent.Id, "Explicit Start recovers from crashed renderer without stale mutex");
            await recovered.StopAsync();
            Console.WriteLine("Controller crash survived; abandoned gate recovered; renderer crash detected; explicit restart/stop succeeded.");
        }
        finally
        {
            using var cleanup = new RendererClient(executable);
            await cleanup.ConnectAsync();
            await cleanup.StopAsync();
        }
        Console.WriteLine("PASS: independent lifecycle, reconnection, concurrent starts, stale state, crash handling and cleanup.");
    }

    private static int CurrentId()
    {
        RendererProtocol.GetWindowThreadProcessId(RendererProtocol.FindWindow(), out uint id);
        return (int)id;
    }
    private static Process Probe(string executable, bool hold)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(hold ? "--lifecycle-hold" : "--lifecycle-start");
        start.ArgumentList.Add(executable);
        return Process.Start(start)!;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
