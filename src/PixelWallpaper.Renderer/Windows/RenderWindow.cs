using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PixelWallpaper.Renderer.Windows;

internal sealed class RenderWindow : IDisposable
{
    internal const string ClassName = "PixelWallpaper.RenderWindow";
    private readonly NativeMethods.WindowProcedure _procedure;
    private readonly nint _instance;
    private bool _registered;

    public nint Handle { get; private set; }
    public int Width { get; private set; } = 960;
    public int Height { get; private set; } = 540;
    public bool IsMinimized { get; private set; }
    public bool CloseRequested { get; private set; }
    public bool IsDesktopHosted { get; internal set; }
    public Exception? CallbackError { get; private set; }

    public RenderWindow(nint dpiContext = 0)
    {
        // Keep the delegate alive for the entire native window lifetime.
        _procedure = WindowProcedure;
        _instance = NativeMethods.GetModuleHandleW(null);
        nint previousDpi = 0;
        try
        {
            if (dpiContext != 0)
            {
                previousDpi = DesktopNative.SetThreadDpiAwarenessContext(dpiContext);
                if (previousDpi == 0) throw new Win32Exception();
            }
            var windowClass = new NativeMethods.WindowClass
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                Procedure = _procedure,
                Instance = _instance,
                Cursor = NativeMethods.LoadCursorW(0, 32512),
                ClassName = ClassName
            };
            if (NativeMethods.RegisterClassExW(in windowClass) == 0)
                throw new Win32Exception();
            _registered = true;

            var bounds = new NativeMethods.Rect { Right = Width, Bottom = Height };
            if (!NativeMethods.AdjustWindowRectExForDpi(ref bounds, NativeMethods.WsOverlappedWindow,
                    false, 0, NativeMethods.GetDpiForSystem()))
                throw new Win32Exception();

            Handle = NativeMethods.CreateWindowExW(0, ClassName,
                "Pixel Wallpaper | Rainy Forest | 480x270 | 30 FPS cap",
                NativeMethods.WsOverlappedWindow, unchecked((int)0x80000000), unchecked((int)0x80000000),
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0, 0, _instance, 0);
            if (Handle == 0)
                throw new Win32Exception();
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            if (previousDpi != 0) DesktopNative.SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    public void Show() => NativeMethods.ShowWindow(Handle, IsDesktopHosted ? 4 : 5);

    public void RequestClose() => CloseRequested = true;

    public void RefreshClientSize()
    {
        if (!DesktopNative.GetClientRect(Handle, out var rect)) throw new Win32Exception();
        Width = rect.Right - rect.Left;
        Height = rect.Bottom - rect.Top;
        IsMinimized = Width == 0 || Height == 0;
    }

    public void PumpMessages()
    {
        while (NativeMethods.PeekMessageW(out var message, 0, 0, 0, 1))
        {
            if (message.Id == NativeMethods.WmQuit)
            {
                CloseRequested = true;
                break;
            }
            NativeMethods.TranslateMessage(in message);
            NativeMethods.DispatchMessageW(in message);
        }
        if (CallbackError is not null)
            throw new InvalidOperationException("The window callback failed.", CallbackError);
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        // Exceptions must never cross the unmanaged callback boundary.
        try
        {
            switch (message)
            {
                case NativeMethods.WmClose:
                    CloseRequested = true;
                    return 0; // Release D3D first; Dispose destroys the HWND afterwards.
                case NativeMethods.WmDestroy:
                    CloseRequested = true;
                    Handle = 0; // Parent destruction may also destroy this cross-process child.
                    NativeMethods.PostQuitMessage(0);
                    return 0;
                case NativeMethods.WmKeyDown when wParam == 27:
                    CloseRequested = true;
                    return 0;
                case NativeMethods.WmSize:
                    Width = (int)((long)lParam & 0xFFFF);
                    Height = (int)(((long)lParam >> 16) & 0xFFFF);
                    IsMinimized = wParam == 1 || Width == 0 || Height == 0;
                    return 0;
                case NativeMethods.WmPaint:
                    NativeMethods.ValidateRect(window, 0);
                    return 0;
                case NativeMethods.WmEraseBackground:
                    return 1; // The renderer covers the client area; avoid a GDI clear/flicker.
                case NativeMethods.WmDpiChanged:
                    if (IsDesktopHosted) return 0; // DesktopHost owns child bounds.
                    var rect = Marshal.PtrToStructure<NativeMethods.Rect>(lParam);
                    if (!NativeMethods.SetWindowPos(window, 0, rect.Left, rect.Top,
                            rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0014))
                        throw new Win32Exception();
                    return 0;
                case 0x0084 when IsDesktopHosted: // WM_NCHITTEST
                    return -1; // HTTRANSPARENT; desktop icons/background own input.
                case 0x0021 when IsDesktopHosted: // WM_MOUSEACTIVATE
                    return 3; // MA_NOACTIVATE
            }
        }
        catch (Exception error)
        {
            CallbackError = error;
            CloseRequested = true;
            return 0;
        }
        return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            NativeMethods.DestroyWindow(Handle);
            Handle = 0;
        }
        if (_registered)
        {
            NativeMethods.UnregisterClassW(ClassName, _instance);
            _registered = false;
        }
        GC.KeepAlive(_procedure);
    }
}
