using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using PixelWallpaper.Shared;

namespace PixelWallpaper.App;

// Called serially from the UI. Process exit is an event, not a recurring status poll.
internal sealed class RendererClient : IDisposable
{
    private Process? _process;
    private Task<string>? _errorOutput;
    private nint _endpoint;
    private nint _legacyStop;
    private string _wallpaper = WallpaperSettings.RainyForest;
    private readonly string _executable;
    public RendererClient(string? executable = null) => _executable = executable ??
        Path.Combine(AppContext.BaseDirectory, "Renderer", "PixelWallpaper.Renderer.exe");
    public event Action? StateChanged;
    public bool IsRunning => _process is not null && !_process.HasExited;
    public bool CanApplySettings => IsRunning && _endpoint != 0;
    public bool StartedHere { get; private set; }

    public void AttachExisting()
    {
        if (IsRunning) return;
        ReleaseExitedProcess();
        nint endpoint = RendererProtocol.FindWindow();
        nint legacy = endpoint == 0 ? FindWindowW("PixelWallpaper.DesktopControl", null) : 0;
        if (endpoint == 0 && legacy == 0) legacy = FindWindowW("PixelWallpaper.RenderWindow", null);
        nint target = endpoint != 0 ? endpoint : legacy;
        if (target == 0) return;
        RendererProtocol.GetWindowThreadProcessId(target, out uint id);
        Process candidate = Process.GetProcessById((int)id);
        try
        {
            if (candidate.ProcessName != "PixelWallpaper.Renderer" || candidate.HasExited)
            { candidate.Dispose(); return; }
            _process = candidate; _endpoint = endpoint; _legacyStop = legacy;
            _wallpaper = endpoint == 0 ? WallpaperSettings.RainyForest :
                RendererProtocol.SceneFromCode(RendererProtocol.Send(endpoint, RendererProtocol.ReadScene));
            StartedHere = false;
            Watch(candidate);
        }
        catch { candidate.Dispose(); _process = null; throw; }
    }

    public async Task StartAsync(WallpaperSettings settings)
    {
        settings.Validate();
        AttachExisting();
        if (IsRunning) { await ApplyAsync(settings); return; }
        string executable = _executable;
        if (!File.Exists(executable)) throw new FileNotFoundException("The renderer is missing. Rebuild the full solution.", executable);
        using var owner = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add("--owner");
        start.ArgumentList.Add(owner.Id.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(owner.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--fps");
        start.ArgumentList.Add(((int)settings.PerformanceMode).ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--scene");
        start.ArgumentList.Add(settings.Wallpaper);
        if (!settings.PauseWhileFullscreen) start.ArgumentList.Add("--no-fullscreen-pause");
        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new IOException("The renderer did not start.");
            _process = process; StartedHere = true; _wallpaper = settings.Wallpaper;
            _errorOutput = process.StandardError.ReadToEndAsync();
            Watch(process);
            // Bounded startup-only discovery. There is no timer once connected.
            long deadline = Environment.TickCount64 + 7000;
            while (!process.HasExited && Environment.TickCount64 < deadline)
            {
                _endpoint = await Task.Run(() => RendererProtocol.FindWindow(process.Id));
                if (_endpoint != 0)
                {
                    if (RendererProtocol.SceneFromCode(RendererProtocol.Send(_endpoint, RendererProtocol.ReadScene)) != settings.Wallpaper)
                        throw new IOException("The renderer did not start the requested scene. Rebuild the full solution.");
                    await ApplyAsync(settings); return;
                }
                await Task.Delay(100);
            }
            string detail = process.HasExited && _errorOutput is not null ? await _errorOutput : "Startup timed out.";
            throw new IOException("Unable to start the wallpaper. " + detail.Trim());
        }
        catch
        {
            if (_process != process) { process.Dispose(); throw; }
            // Keep a live process owned/tracked if cleanup fails; never drop it.
            if (_process == process && !process.HasExited)
            {
                nint target = _endpoint != 0 ? _endpoint : RendererProtocol.FindWindow(process.Id, "PixelWallpaper.DesktopControl");
                _legacyStop = target;
                if (target != 0) RendererProtocol.PostMessageW(target, RendererProtocol.Close, 0, 0);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { }
            }
            if (process.HasExited) ReleaseExitedProcess();
            throw;
        }
    }

    public async Task ApplyAsync(WallpaperSettings settings)
    {
        settings.Validate();
        if (!IsRunning) return;
        if (_wallpaper != settings.Wallpaper)
        {
            // Scene resources have one owner for the entire renderer lifetime.
            // Stop completely before starting the replacement; never run two scenes/processes.
            await StopAsync();
            await StartAsync(settings);
            return;
        }
        if (_endpoint == 0) return;
        nuint packed = RendererProtocol.Pack(settings);
        nint endpoint = _endpoint;
        int expectedOwner = _process!.Id;
        await Task.Run(() =>
        {
            RendererProtocol.GetWindowThreadProcessId(endpoint, out uint owner);
            if (owner != expectedOwner) throw new IOException("The renderer connection has changed.");
            if (RendererProtocol.Send(endpoint, RendererProtocol.Apply, packed) != 1 ||
                RendererProtocol.Send(endpoint, RendererProtocol.ReadOptions) != packed)
                throw new IOException("The running renderer did not accept the settings.");
        });
    }

    public async Task StopAsync()
    {
        if (!IsRunning) { ReleaseExitedProcess(); return; }
        Process process = _process!;
        nint target = _endpoint != 0 ? _endpoint : _legacyStop;
        if (target == 0) target = await Task.Run(() => RendererProtocol.FindWindow(process.Id, "PixelWallpaper.DesktopControl"));
        RendererProtocol.GetWindowThreadProcessId(target, out uint owner);
        if (owner != process.Id) throw new IOException("The renderer connection has changed.");
        if (target == 0 || !RendererProtocol.PostMessageW(target, RendererProtocol.Close, 0, 0))
            throw new IOException("The wallpaper could not be asked to stop. Please try again.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        { throw new IOException("The wallpaper has not stopped yet. This window will remain open; try Stop again."); }
        ReleaseExitedProcess();
    }

    private void Watch(Process process)
    {
        process.Exited += OnExited;
        process.EnableRaisingEvents = true;
    }
    private void OnExited(object? sender, EventArgs args) => StateChanged?.Invoke();
    private void ReleaseExitedProcess()
    {
        if (_process is null) return;
        if (!_process.HasExited) return;
        _process.Exited -= OnExited;
        _process.Dispose(); _process = null; _endpoint = _legacyStop = 0;
        _errorOutput = null; StartedHere = false;
    }
    public void Dispose()
    {
        if (_process is not null)
        {
            _process.Exited -= OnExited;
            _process.Dispose(); _process = null;
        }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string? title);
}
