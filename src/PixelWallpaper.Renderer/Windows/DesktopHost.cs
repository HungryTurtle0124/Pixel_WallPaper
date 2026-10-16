using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PixelWallpaper.Renderer.Windows;

internal sealed class DesktopHost : IDisposable
{
    internal const string ControlClass = "PixelWallpaper.DesktopControl";
    private readonly RenderWindow _window;
    private readonly DesktopHostInfo _info;
    private readonly NativeMethods.WindowProcedure _controlProcedure;
    private readonly DesktopNative.WinEventProcedure _eventProcedure;
    private readonly nint _instance = NativeMethods.GetModuleHandleW(null);
    private readonly nint _originalParent;
    private readonly uint _originalStyle;
    private readonly uint _originalExtendedStyle;
    private readonly NativeMethods.Rect _originalRect;
    private nint _control;
    private nint _destroyHook;
    private nint _locationHook;
    private uint _taskbarCreated;
    private bool _registered;
    private bool _attached;
    private bool _disposed;
    private bool _layoutPending;

    public bool HotKeyAvailable { get; private set; }
    public string? StopReason { get; private set; }
    public string Description => _info.Describe();

    public DesktopHost(RenderWindow window, DesktopHostInfo info)
    {
        _window = window;
        _info = info;
        _controlProcedure = ControlProcedure;
        _eventProcedure = OnDesktopEvent;
        _originalParent = DesktopNative.GetParent(window.Handle);
        _originalStyle = DesktopHostInfo.GetStyle(window.Handle, -16);
        _originalExtendedStyle = DesktopHostInfo.GetStyle(window.Handle, -20);
        if (!DesktopNative.GetWindowRect(window.Handle, out _originalRect))
            throw new Win32Exception();

        try
        {
            if (!DesktopNative.AreDpiAwarenessContextsEqual(
                DesktopNative.GetWindowDpiAwarenessContext(window.Handle), info.DpiContext))
                throw new InvalidOperationException("Renderer and desktop host DPI contexts differ. Refusing cross-process DPI reset.");
            CreateControlWindow();
            // Subscribe before attaching, so a disappearing host cannot go unnoticed.
            _destroyHook = DesktopNative.SetWinEventHook(DesktopNative.ObjectDestroy, DesktopNative.ObjectDestroy,
                0, _eventProcedure, info.ExplorerProcessId, 0, 0);
            _locationHook = DesktopNative.SetWinEventHook(DesktopNative.ObjectLocationChange, DesktopNative.ObjectLocationChange,
                0, _eventProcedure, info.ExplorerProcessId, 0, 0);
            if (_destroyHook == 0 || _locationHook == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot subscribe to Explorer desktop lifetime events.");

            uint style = (_originalStyle & ~(NativeMethods.WsOverlappedWindow | DesktopNative.Popup | 0x21000000u))
                | DesktopNative.Child | 0x04000000; // WS_CLIPSIBLINGS
            uint extended = (_originalExtendedStyle & ~0x00040301u) | 0x08000080u | 0x20u;
            if (info.IsRaisedDesktop) extended |= DesktopNative.Layered;
            SetStyle(window.Handle, -16, style);
            SetStyle(window.Handle, -20, extended);
            if (info.IsRaisedDesktop && !DesktopNative.SetLayeredWindowAttributes(window.Handle, 0, 255, 2))
                throw new Win32Exception();
            SetParentChecked(window.Handle, info.Parent);
            _attached = true;
            window.IsDesktopHosted = true;
            PositionWindow();
            ValidateAttachment();
            // MOD_NOREPEAT | MOD_CONTROL | MOD_ALT | MOD_SHIFT, Q. Optional if occupied.
            HotKeyAvailable = DesktopNative.RegisterHotKey(_control, 1, 0x4007, 0x51);
            Console.Error.WriteLine("Desktop host: " + Description);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static bool RequestStop()
    {
        nint target = DesktopNative.FindWindowW(ControlClass, null);
        if (target == 0)
            target = DesktopNative.FindWindowW(RenderWindow.ClassName, null);
        return target != 0 && DesktopNative.PostMessageW(target, DesktopNative.Close, 0, 0);
    }

    private void CreateControlWindow()
    {
        // Hidden TOP-LEVEL window, not HWND_MESSAGE: receives display changes and
        // TaskbarCreated broadcasts even while the render HWND is an Explorer child.
        var windowClass = new NativeMethods.WindowClass
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
            Procedure = _controlProcedure,
            Instance = _instance,
            ClassName = ControlClass
        };
        if (NativeMethods.RegisterClassExW(in windowClass) == 0)
            throw new Win32Exception();
        _registered = true;
        _taskbarCreated = DesktopNative.RegisterWindowMessageW("TaskbarCreated");
        if (_taskbarCreated == 0) throw new Win32Exception();
        _control = NativeMethods.CreateWindowExW(0x08000080, ControlClass,
            "Pixel Wallpaper control", 0, 0, 0, 0, 0, 0, 0, _instance, 0);
        if (_control == 0) throw new Win32Exception();
    }

    private nint ControlProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == DesktopNative.Close || message == DesktopNative.HotKey)
            {
                _window.RequestClose();
                return 0;
            }
            if (message == DesktopNative.HostLost)
            {
                StopForHostLoss("Explorer destroyed or replaced a desktop host window. Restart the renderer after Explorer is ready.");
                return 0;
            }
            if (message == DesktopNative.HostMoved || message == DesktopNative.DisplayChange ||
                message == DesktopNative.SettingChange || message == _taskbarCreated)
            {
                _layoutPending = false;
                if (_attached && !_disposed)
                {
                    ValidateAttachment();
                    PositionWindow();
                }
                return 0;
            }
        }
        catch (Exception error)
        {
            // Never let a managed exception escape a WNDPROC callback.
            StopForHostLoss("Desktop integration stopped: " + error.Message);
            return 0;
        }
        return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }

    private void OnDesktopEvent(nint hook, uint eventType, nint window, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        if (_disposed || objectId != 0 || childId != 0 ||
            (window != _info.Parent && window != _info.Progman && window != _info.IconView && window != _info.WallpaperWorker))
            return;
        // OUTOFCONTEXT callbacks arrive on the installing thread. Only post work here;
        // no reparenting or Explorer calls from a potentially reentrant event callback.
        if (eventType == DesktopNative.ObjectDestroy)
            DesktopNative.PostMessageW(_control, DesktopNative.HostLost, 0, 0);
        else if (!_layoutPending)
        {
            _layoutPending = true;
            DesktopNative.PostMessageW(_control, DesktopNative.HostMoved, 0, 0);
        }
    }

    private void ValidateAttachment()
    {
        if (!DesktopHostInfo.OwnedBy(_info.Progman, _info.ExplorerProcessId) ||
            !DesktopHostInfo.OwnedBy(_info.Parent, _info.ExplorerProcessId) ||
            !DesktopHostInfo.OwnedBy(_info.IconView, _info.ExplorerProcessId) ||
            !DesktopHostInfo.OwnedBy(_info.WallpaperWorker, _info.ExplorerProcessId) ||
            DesktopNative.GetShellWindow() != _info.Progman ||
            DesktopNative.GetParent(_window.Handle) != _info.Parent)
            throw new InvalidOperationException("The validated Explorer host is no longer available.");
        if (_info.IsRaisedDesktop &&
            (!DesktopHostInfo.IsBelow(_window.Handle, _info.IconView) ||
             !DesktopHostInfo.IsBelow(_info.WallpaperWorker, _window.Handle)))
            throw new InvalidOperationException("Explorer changed the wallpaper/icon Z-order.");
    }

    private void PositionWindow()
    {
        // Primary monitor only. Full monitor bounds include the area under the taskbar;
        // the taskbar is a separate shell window that stays above this child surface.
        var monitor = new DesktopNative.MonitorInfo { Size = (uint)Marshal.SizeOf<DesktopNative.MonitorInfo>() };
        if (!DesktopNative.GetMonitorInfoW(DesktopNative.MonitorFromPoint(default, 1), ref monitor))
            throw new Win32Exception();
        var bounds = monitor.Monitor;
        Marshal.SetLastPInvokeError(0);
        int mapped = DesktopNative.MapWindowPoints(0, _info.Parent, ref bounds, 2);
        if (mapped == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
        nint insertAfter = _info.IsRaisedDesktop ? _info.IconView : 1; // HWND_BOTTOM for legacy child.
        if (!NativeMethods.SetWindowPos(_window.Handle, insertAfter, bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x0010 | 0x0020))
            throw new Win32Exception(); // SWP_NOACTIVATE | SWP_FRAMECHANGED
        _window.RefreshClientSize();
    }

    private void StopForHostLoss(string reason)
    {
        if (StopReason is null)
        {
            StopReason = reason;
            Console.Error.WriteLine(reason);
        }
        if (DesktopNative.IsWindow(_window.Handle)) NativeMethods.ShowWindow(_window.Handle, 0);
        _window.RequestClose();
    }

    private static void SetStyle(nint window, int index, uint style)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = DesktopNative.SetWindowLongPtr(window, index, (nint)(long)style);
        if (previous == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
    }

    private static void SetParentChecked(nint window, nint parent)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = DesktopNative.SetParent(window, parent);
        // NULL reparents to the desktop. Until WS_CHILD is cleared afterwards,
        // GetParent can return the desktop HWND rather than NULL.
        if ((previous == 0 && Marshal.GetLastWin32Error() != 0) ||
            (parent != 0 && DesktopNative.GetParent(window) != parent))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetParent failed or returned an unexpected parent.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_destroyHook != 0) { DesktopNative.UnhookWinEvent(_destroyHook); _destroyHook = 0; }
        if (_locationHook != 0) { DesktopNative.UnhookWinEvent(_locationHook); _locationHook = 0; }
        if (HotKeyAvailable) DesktopNative.UnregisterHotKey(_control, 1);

        if (DesktopNative.IsWindow(_window.Handle))
        {
            // Hide first so detaching cannot flash a desktop-sized top-level window.
            NativeMethods.ShowWindow(_window.Handle, 0);
            try
            {
                SetParentChecked(_window.Handle, _originalParent);
                SetStyle(_window.Handle, -16, _originalStyle);
                SetStyle(_window.Handle, -20, _originalExtendedStyle);
                if (DesktopNative.GetParent(_window.Handle) != _originalParent)
                    throw new InvalidOperationException("Original window parent was not restored.");
                if (!NativeMethods.SetWindowPos(_window.Handle, 0, _originalRect.Left, _originalRect.Top,
                    _originalRect.Right - _originalRect.Left, _originalRect.Bottom - _originalRect.Top,
                    0x0004 | 0x0010 | 0x0020)) // NOZORDER | NOACTIVATE | FRAMECHANGED
                    throw new Win32Exception();
                _window.IsDesktopHosted = false;
            }
            catch (Exception error)
            {
                // Shutdown must continue releasing D3D/owned HWND resources even if
                // Explorer is gone. RenderWindow.Dispose still destroys our hidden HWND.
                Console.Error.WriteLine("Desktop detach: " + error.Message);
            }
        }
        _attached = false;
        if (_control != 0) { NativeMethods.DestroyWindow(_control); _control = 0; }
        if (_registered) { NativeMethods.UnregisterClassW(ControlClass, _instance); _registered = false; }
        GC.KeepAlive(_eventProcedure);
        GC.KeepAlive(_controlProcedure);
    }
}
