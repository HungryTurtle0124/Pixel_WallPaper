using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace PixelWallpaper.Shared;

// Explicit commands use a bounded, cross-process gate. Idle status uses Process.Exited,
// controller activation and the startup broadcast; there is no recurring scan.
internal sealed class RendererClient : IDisposable
{
    private sealed record Snapshot(int ProcessId, long Started, uint Revision, bool Stopping, WallpaperSettings Settings);
    private readonly object _stateLock = new();
    private readonly string _executable;
    private readonly SettingsStore? _store;
    private WallpaperSettings? _preferences;
    private Process? _process;
    private Snapshot? _snapshot;
    private nint _endpoint;
    private bool _disposed;

    public RendererClient(string? executable = null, SettingsStore? store = null)
    {
        _executable = executable ?? Path.Combine(AppContext.BaseDirectory, "Renderer", "PixelWallpaper.Renderer.exe");
        _store = store;
    }
    public event Action? StateChanged;
    public bool IsRunning { get { lock (_stateLock) return _process is not null && !_process.HasExited; } }
    public bool IsStopping { get { lock (_stateLock) return _process is not null && !_process.HasExited && _snapshot?.Stopping == true; } }
    public WallpaperSettings? CurrentSettings { get { lock (_stateLock) return _snapshot?.Settings ?? _preferences; } }

    public Task<WallpaperSettings?> ConnectAsync() => CommandAsync(() =>
    {
        Snapshot? current = Discover();
        if (current is null) Forget();
        if (current is not null) Remember(current.Settings);
        else if (_store is not null) LoadPreferences();
        return current?.Settings;
    });

    public Task<WallpaperSettings> StartAsync(WallpaperSettings settings) => CommandAsync(() =>
    {
        settings.Validate();
        Snapshot? current = Discover();
        // Another Start wins by existence. Never overwrite its live settings with our defaults.
        if (current is not null && !current.Stopping) return Remember(current.Settings);
        if (current is not null) WaitForExit();
        return Remember(Launch(_store is null ? settings : LoadPreferences()));
    });

    public Task<WallpaperSettings> ApplyAsync(WallpaperSettings settings) => CommandAsync(() =>
    {
        settings.Validate();
        Snapshot? expected;
        lock (_stateLock) expected = _snapshot;
        Snapshot? current = Discover();
        if (current is null)
        {
            Forget();
            if (expected is not null) throw new IOException("The renderer exited. Start Wallpaper to run it again.");
            if (_store is not null)
            {
                WallpaperSettings? observed;
                lock (_stateLock) observed = _preferences;
                if (observed != LoadPreferences())
                    throw new IOException("Saved preferences changed elsewhere. Current settings have been refreshed; please retry your change.");
            }
            return Remember(settings); // Stopped: save preferences without launching anything.
        }
        if (expected != current)
            throw new IOException("The wallpaper state changed elsewhere. Current settings have been refreshed; please retry your change.");
        if (current.Stopping) throw new IOException("The wallpaper is stopping. Please try again after it exits.");
        if (current.Settings.Wallpaper != settings.Wallpaper)
        {
            StopCurrent();
            return Remember(Launch(settings)); // One gate covers stop/start and persistence.
        }
        ValidateEndpoint();
        nuint result = RendererProtocol.Send(_endpoint, RendererProtocol.ApplyChecked,
            RendererProtocol.Pack(settings), (nint)current.Revision);
        Snapshot? applied = Discover();
        if (result != 1 || applied is null || applied.Settings != settings)
            throw new IOException("The renderer state changed or the command was not accepted. Refresh and retry.");
        return Remember(applied.Settings);
    });

    public Task<WallpaperSettings?> StopAsync() => CommandAsync<WallpaperSettings?>(() =>
    {
        Snapshot? expected;
        lock (_stateLock) expected = _snapshot;
        Snapshot? current = Discover();
        if (current is null) { Forget(); return null; }
        if (expected is not null && (expected.ProcessId != current.ProcessId || expected.Started != current.Started))
            throw new IOException("A replacement renderer is running. Its state has been refreshed; retry Stop Wallpaper.");
        StopCurrent();
        return Remember(current.Settings);
    });

    private Task<T> CommandAsync<T>(Func<T> command) => Task.Run(() => RendererCommandGate.Run(() =>
    {
        lock (_stateLock) ObjectDisposedException.ThrowIf(_disposed, this);
        return command();
    }));

    private WallpaperSettings LoadPreferences()
    {
        var settings = _store!.Load(out _);
        lock (_stateLock) _preferences = settings;
        return settings;
    }

    // Call only under the command gate, so a later command cannot save before this one.
    private WallpaperSettings Remember(WallpaperSettings settings)
    {
        lock (_stateLock) _preferences = settings;
        if (_store is not null && (_store.Load(out var warning) != settings || warning is not null))
        {
            _store.Save(settings);
            RendererProtocol.Broadcast(RendererProtocol.PreferencesChangedMessage);
        }
        return settings;
    }

    private Snapshot? Discover()
    {
        nint endpoint = RendererProtocol.FindWindow();
        if (endpoint == 0)
        {
            if (RendererProtocol.FindWindow(windowClass: "PixelWallpaper.DesktopControl") != 0 ||
                RendererProtocol.FindWindow(windowClass: "PixelWallpaper.RenderWindow") != 0)
                throw new IOException("An older or initializing renderer is present. Wait for startup, or use --stop before upgrading it.");
            return null;
        }
        RendererProtocol.GetWindowThreadProcessId(endpoint, out uint id);
        Process candidate;
        try { candidate = Process.GetProcessById((int)id); }
        catch (ArgumentException) { return null; }
        try
        {
            _ = candidate.Handle; // Retain the real process object, not just a reusable PID.
            if (candidate.HasExited) return null;
            using var self = Process.GetCurrentProcess();
            if (candidate.SessionId != self.SessionId ||
                !string.Equals(Path.GetFileName(candidate.MainModule!.FileName), "PixelWallpaper.Renderer.exe", StringComparison.OrdinalIgnoreCase) ||
                RendererProtocol.Send(endpoint, RendererProtocol.Identify) != RendererProtocol.Signature)
                throw new IOException("The renderer endpoint is incompatible. Stop the older renderer with --stop, then rebuild/restart.");
            var settings = RendererProtocol.DecodeStatus(RendererProtocol.Send(endpoint, RendererProtocol.ReadStatus),
                out uint revision, out bool stopping);
            var snapshot = new Snapshot(candidate.Id, candidate.StartTime.ToUniversalTime().Ticks, revision, stopping, settings);
            lock (_stateLock)
            {
                if (_snapshot is not null && _snapshot.ProcessId == snapshot.ProcessId && _snapshot.Started == snapshot.Started &&
                    _process is not null && !_process.HasExited)
                {
                    _snapshot = snapshot; _endpoint = endpoint;
                }
                else
                {
                    Forget();
                    _process = candidate; candidate = null!;
                    _snapshot = snapshot; _endpoint = endpoint;
                    _process.Exited += OnExited;
                    _process.EnableRaisingEvents = true;
                }
            }
            return snapshot;
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        { throw new IOException("The renderer exited or cannot be inspected. Refresh and try again.", error); }
        finally { candidate?.Dispose(); }
    }

    private WallpaperSettings Launch(WallpaperSettings settings)
    {
        Forget();
        if (!File.Exists(_executable)) throw new FileNotFoundException("The renderer is missing. Rebuild the full solution.", _executable);
        // ShellExecute launches a standalone WinExe without inherited controller stderr pipes.
        var start = new ProcessStartInfo(_executable) { UseShellExecute = true };
        start.ArgumentList.Add("--background");
        start.ArgumentList.Add("--fps");
        start.ArgumentList.Add(((int)settings.PerformanceMode).ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--scene"); start.ArgumentList.Add(settings.Wallpaper);
        if (!settings.PauseWhileFullscreen) start.ArgumentList.Add("--no-fullscreen-pause");
        using var launched = Process.Start(start) ?? throw new IOException("Windows did not start the renderer.");
        long started = launched.StartTime.ToUniversalTime().Ticks;
        long deadline = Environment.TickCount64 + 7000;
        do
        {
            // Only startup has a bounded 100 ms retry; it ends on connection/exit/timeout.
            if (RendererProtocol.FindWindow() != 0)
            {
                Snapshot? current = Discover();
                if (current is not null && !current.Stopping) return current.Settings;
            }
            if (launched.HasExited)
            {
                if (launched.ExitCode == 2)
                {
                    // A sign-in launch can need this gate to finish initializing.
                    // Release it promptly when that launch owns the instance mutex.
                    Snapshot? current = Discover();
                    if (current is not null && !current.Stopping) return current.Settings;
                    throw new IOException("Another wallpaper renderer started first. Wait briefly, then try again.");
                }
                throw new IOException("The renderer exited during startup. " +
                    (RendererDiagnostics.Read(launched.Id, started) ?? $"Exit code {launched.ExitCode}; check desktop host, GPU, and assets."));
            }
            Thread.Sleep(100);
        } while (Environment.TickCount64 < deadline);
        // Leave a slow-starting renderer independent and discoverable. Never force-kill it.
        throw new IOException("Renderer startup has not completed. It may still be initializing. Reopen the controller to reconnect, or use --stop.");
    }

    private void ValidateEndpoint()
    {
        lock (_stateLock)
        {
            RendererProtocol.GetWindowThreadProcessId(_endpoint, out uint id);
            if (_process is null || _process.HasExited || id != _process.Id)
                throw new IOException("The renderer connection was lost. Refresh and retry.");
        }
    }
    private void StopCurrent()
    {
        ValidateEndpoint();
        RendererProtocol.Send(_endpoint, RendererProtocol.Close);
        WaitForExit();
    }
    private void WaitForExit()
    {
        Process process;
        lock (_stateLock) process = _process ?? throw new IOException("Renderer connection was lost.");
        if (!process.WaitForExit(5000)) throw new IOException("The renderer has not stopped. Retry Stop Wallpaper or use --stop.");
        Forget();
    }
    private void OnExited(object? sender, EventArgs args) => StateChanged?.Invoke();
    private void Forget()
    {
        lock (_stateLock)
        {
            if (_process is not null) { _process.Exited -= OnExited; _process.Dispose(); _process = null; }
            _snapshot = null; _endpoint = 0;
        }
    }
    public void Dispose()
    {
        lock (_stateLock) { _disposed = true; Forget(); }
    }
}
