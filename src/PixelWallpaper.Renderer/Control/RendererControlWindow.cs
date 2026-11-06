using System.ComponentModel;
using System.Runtime.InteropServices;
using PixelWallpaper.Renderer.Runtime;
using PixelWallpaper.Renderer.Timing;
using PixelWallpaper.Renderer.Windows;
using PixelWallpaper.Shared;

namespace PixelWallpaper.Renderer.Control;

internal sealed class RendererControlWindow : IDisposable
{
    private readonly NativeMethods.WindowProcedure _procedure;
    private readonly RenderWindow _renderWindow;
    private readonly FramePacer _pacer;
    private readonly RuntimeStateManager _runtime;
    private readonly nint _instance = NativeMethods.GetModuleHandleW(null);
    private nint _handle;
    private bool _registered;
    private readonly int _sceneCode;
    private uint _revision = 1;

    public RendererControlWindow(RenderWindow window, FramePacer pacer, RuntimeStateManager runtime,
        string wallpaper = WallpaperSettings.RainyForest)
    {
        _renderWindow = window; _pacer = pacer; _runtime = runtime;
        _sceneCode = RendererProtocol.SceneCode(wallpaper);
        _procedure = WindowProcedure;
        try
        {
            var windowClass = new NativeMethods.WindowClass
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(), Procedure = _procedure,
                Instance = _instance, ClassName = RendererProtocol.WindowClass
            };
            if (NativeMethods.RegisterClassExW(in windowClass) == 0) throw new Win32Exception();
            _registered = true;
            _handle = NativeMethods.CreateWindowExW(0x08000080, RendererProtocol.WindowClass,
                "Pixel Wallpaper renderer control", 0, 0, 0, 0, 0, 0, 0, _instance, 0);
            if (_handle == 0) throw new Win32Exception();
            // One startup notification lets an already-open controller discover a
            // standalone renderer without a recurring process scan.
            if (RendererProtocol.ReadyMessage != 0)
                RendererProtocol.PostMessageW(0xFFFF, RendererProtocol.ReadyMessage, (nuint)Environment.ProcessId, 0);
        }
        catch { Dispose(); throw; }
    }

    private nint WindowProcedure(nint window, uint message, nuint value, nint parameter)
    {
        try
        {
            switch (message)
            {
                case RendererProtocol.Apply:
                case RendererProtocol.ApplyChecked:
                    if (_renderWindow.CloseRequested) return 0;
                    if (message == RendererProtocol.ApplyChecked && (uint)parameter != _revision) return 2;
                    if (!RendererProtocol.TryUnpack(value, out int fps, out bool fullscreen)) return 0;
                    bool changed = fps != _pacer.FramesPerSecond || fullscreen != _runtime.PauseWhileFullscreen;
                    _pacer.SetFrameRate(fps);
                    _runtime.SetPauseWhileFullscreen(fullscreen);
                    if (changed) _revision = unchecked(_revision + 1);
                    return 1;
                case RendererProtocol.Identify: return (nint)RendererProtocol.Signature;
                case RendererProtocol.ReadStatus:
                    return (nint)(((ulong)_revision << 16) | ((uint)_sceneCode << 9) |
                        (uint)_pacer.FramesPerSecond | (_runtime.PauseWhileFullscreen ? 0x100u : 0) |
                        (_renderWindow.CloseRequested ? 0x800u : 0));
                case RendererProtocol.ReadOptions:
                    return _pacer.FramesPerSecond | (_runtime.PauseWhileFullscreen ? 0x100 : 0);
                case RendererProtocol.ReadFrameCount: return (nint)_pacer.FrameCount;
                case RendererProtocol.ReadPauseReasons: return (nint)(int)_runtime.Reasons;
                case RendererProtocol.ReadAllocationCount: return (nint)GC.GetAllocatedBytesForCurrentThread();
                case RendererProtocol.ReadScene: return _sceneCode;
                case RendererProtocol.Close: _renderWindow.RequestClose(); return 1;
            }
        }
        catch { _renderWindow.RequestClose(); return 0; }
        return NativeMethods.DefWindowProcW(window, message, value, parameter);
    }

    public void Dispose()
    {
        if (_handle != 0) { NativeMethods.DestroyWindow(_handle); _handle = 0; }
        if (_registered) { NativeMethods.UnregisterClassW(RendererProtocol.WindowClass, _instance); _registered = false; }
        GC.KeepAlive(_procedure);
    }
}
