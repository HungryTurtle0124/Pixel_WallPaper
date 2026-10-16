using System.Diagnostics;
using PixelWallpaper.Renderer.Windows;

namespace PixelWallpaper.Renderer.Control;

// A retained OS process handle detects owner exit without a heartbeat or PID polling.
internal sealed class ControllerLifetime : IDisposable
{
    private readonly Process _owner;
    private readonly nint _renderWindow;
    private volatile bool _disposed;

    public ControllerLifetime(int processId, long startTimeUtcTicks, nint renderWindow)
    {
        _renderWindow = renderWindow;
        _owner = Process.GetProcessById(processId);
        try
        {
            if (_owner.StartTime.ToUniversalTime().Ticks != startTimeUtcTicks || _owner.HasExited)
                throw new InvalidOperationException("The controller process is no longer available.");
            _owner.Exited += OnOwnerExited;
            _owner.EnableRaisingEvents = true;
            if (_owner.HasExited) OnOwnerExited(_owner, EventArgs.Empty);
        }
        catch { _owner.Dispose(); throw; }
    }

    private void OnOwnerExited(object? sender, EventArgs args)
    {
        if (!_disposed) DesktopNative.PostMessageW(_renderWindow, NativeMethods.WmClose, 0, 0);
    }

    public void Dispose()
    {
        _disposed = true;
        _owner.Exited -= OnOwnerExited;
        _owner.Dispose();
    }
}
