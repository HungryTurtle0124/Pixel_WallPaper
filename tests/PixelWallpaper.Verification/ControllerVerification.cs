using System.Diagnostics;
using System.Runtime.InteropServices;
using PixelWallpaper.App;
using PixelWallpaper.Renderer.Runtime;
using PixelWallpaper.Shared;

internal static class ControllerVerification
{
    public static async Task RunAsync(string executable, string wallpaper = WallpaperSettings.RainyForest)
    {
        CheckSettings();
        using var client = new RendererClient(executable);
        var options = new WallpaperSettings { Wallpaper = wallpaper, PauseWhileFullscreen = false };
        try
        {
            await client.StartAsync(options);
            nint endpoint = RendererProtocol.FindWindow();
            RendererProtocol.GetWindowThreadProcessId(endpoint, out uint id);
            if (endpoint == 0) throw new Exception("No renderer endpoint.");
            using var renderer = Process.GetProcessById((int)id);
            Require(RendererProtocol.SceneFromCode(RendererProtocol.Send(endpoint, RendererProtocol.ReadScene)) == wallpaper, "Selected scene");
            foreach (PerformanceMode mode in Enum.GetValues<PerformanceMode>())
            {
                options = options with { PerformanceMode = mode };
                await client.ApplyAsync(options);
                await Task.Delay(1000);
                long allocated = (long)RendererProtocol.Send(endpoint, RendererProtocol.ReadAllocationCount);
                long frames = (long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount);
                var clock = Stopwatch.StartNew();
                await Task.Delay(3000);
                double fps = ((long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount) - frames) / clock.Elapsed.TotalSeconds;
                long recurring = (long)RendererProtocol.Send(endpoint, RendererProtocol.ReadAllocationCount) - allocated;
                Console.WriteLine($"{wallpaper} / {mode}: {fps:F3} FPS; {recurring} steady renderer-thread allocated bytes.");
                Require(fps >= (int)mode * .85 && fps <= (int)mode * 1.05, "Frame rate for " + mode);
                Require(recurring == 0, "Steady renderer allocations for " + mode);
                Require(RendererProtocol.Send(endpoint, RendererProtocol.ReadOptions) == RendererProtocol.Pack(options), "Mode acknowledgement");
            }
            using (var duplicate = Process.Start(new ProcessStartInfo(executable)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true })!)
            {
                await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Require(duplicate.ExitCode == 2 && !renderer.HasExited, "Duplicate renderer prevention");
                Console.WriteLine("Duplicate renderer exited with code 2; original renderer kept running.");
            }
            // Test-only labelled fullscreen fixture. No browser/app access required.
            using (var fixture = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--hold-fullscreen")
            { UseShellExecute = false, CreateNoWindow = true })!)
            {
                try
                {
                    await Task.Delay(1500);
                    await client.ApplyAsync(options with { PauseWhileFullscreen = true });
                    await Task.Delay(1500);
                    Require(RendererProtocol.Send(endpoint, RendererProtocol.ReadPauseReasons) == (nuint)PauseReason.Fullscreen, "Fullscreen pause enabled");
                    long before = (long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount);
                    await Task.Delay(1000);
                    Require((long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount) == before, "Fullscreen must stop frames");
                    await client.ApplyAsync(options with { PauseWhileFullscreen = false });
                    await Task.Delay(250);
                    before = (long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount);
                    await Task.Delay(1000);
                    Require((long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount) > before + 30, "Disabled toggle must keep rendering fullscreen");
                    Require(RendererProtocol.Send(endpoint, RendererProtocol.ReadPauseReasons) == 0, "Fullscreen reason cleared");
                    Console.WriteLine("Fullscreen toggle: enabled = zero frames; disabled = rendering continues.");
                    nint runtime = RendererProtocol.FindWindow((int)id, RuntimeStateManager.WindowClass);
                    int session = Process.GetCurrentProcess().SessionId;
                    SendMessageW(runtime, 0x02B1, 7, session);
                    await Task.Delay(100);
                    Require(RendererProtocol.Send(endpoint, RendererProtocol.ReadPauseReasons) == (nuint)PauseReason.Locked, "Lock must remain enabled");
                    before = (long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount);
                    await Task.Delay(500);
                    Require((long)RendererProtocol.Send(endpoint, RendererProtocol.ReadFrameCount) == before, "Lock must stop frames");
                    SendMessageW(runtime, 0x02B1, 8, session);
                    SendMessageW(runtime, 0x0218, 4, 0);
                    Require(RendererProtocol.Send(endpoint, RendererProtocol.ReadPauseReasons) == (nuint)PauseReason.Suspended, "Suspend must remain enabled");
                    SendMessageW(runtime, 0x0218, 18, 0);
                    Console.WriteLine("Synthetic lock/suspend still pause with fullscreen pausing disabled.");
                }
                finally
                {
                    fixture.CloseMainWindow();
                    await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            await client.StopAsync();
            Require(renderer.HasExited && !client.IsRunning, "Clean controller stop");
            Console.WriteLine("Controller client start/stop and live settings passed.");
        }
        finally { if (client.IsRunning) await client.StopAsync(); }

        // An owner process exits without stopping/disposal; the renderer must close itself.
        var probeStart = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        probeStart.ArgumentList.Add("--owner-exit"); probeStart.ArgumentList.Add(executable);
        using var probe = Process.Start(probeStart)!;
        string? line = await probe.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        int ownedId = int.Parse(line ?? throw new Exception("Owner probe failed."));
        Process? owned = null;
        try { owned = Process.GetProcessById(ownedId); } catch (ArgumentException) { }
        await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (owned is not null)
        {
            using (owned) await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Console.WriteLine("Owner process exit: renderer shut down automatically.");
        Console.WriteLine($"PASS: control/settings integration for {wallpaper}.");
    }

    public static async Task ExitOwnerAsync(string executable)
    {
        var client = new RendererClient(executable);
        await client.StartAsync(new WallpaperSettings { PauseWhileFullscreen = false });
        nint endpoint = RendererProtocol.FindWindow();
        RendererProtocol.GetWindowThreadProcessId(endpoint, out uint id);
        Console.WriteLine(id);
        Console.Out.Flush();
        Environment.Exit(0); // Deliberately skip Stop/Dispose to exercise owner monitoring.
    }

    public static void CheckSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PixelWallpaper.Verification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            Require(store.Load(out _) == new WallpaperSettings(), "Default settings");
            var saved = new WallpaperSettings { PerformanceMode = PerformanceMode.Eco, PauseWhileFullscreen = false };
            store.Save(saved);
            Require(new SettingsStore(path).Load(out string? warning) == saved && warning is null, "Settings persistence");
            store.Save(saved with { PerformanceMode = PerformanceMode.Smooth });
            Require(store.Load(out _).PerformanceMode == PerformanceMode.Smooth && !File.Exists(path + ".tmp"), "Atomic settings replacement");
            store.Save(saved with { Wallpaper = WallpaperSettings.Campfire });
            Require(new SettingsStore(path).Load(out warning) == saved with { Wallpaper = WallpaperSettings.Campfire } && warning is null,
                "Campfire selection persistence");
            store.Save(saved with { Wallpaper = WallpaperSettings.MapleTree });
            Require(new SettingsStore(path).Load(out warning) == saved with { Wallpaper = WallpaperSettings.MapleTree } && warning is null,
                "Maple Tree selection persistence");
            File.WriteAllText(path, "invalid json");
            Require(store.Load(out warning) == new WallpaperSettings() && warning is not null && File.ReadAllText(path) == "invalid json", "Malformed settings fallback");
            foreach (string invalid in new[] { "null", new string('x', 17000), "{\"Wallpaper\":\"missing\"}", "{\"PerformanceMode\":\"Ultra\"}" })
            {
                File.WriteAllText(path, invalid);
                Require(store.Load(out warning) == new WallpaperSettings() && warning is not null &&
                    File.ReadAllText(path) == invalid, "Invalid/null/oversized settings fallback");
            }
            Console.WriteLine("Settings defaults, persistence, atomic replace and malformed/null/oversized/unknown-value fallback passed.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            Directory.Delete(directory);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint window, uint message, nuint value, nint parameter);
}
