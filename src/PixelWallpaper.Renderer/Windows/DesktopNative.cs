using System.Runtime.InteropServices;
using System.Text;

namespace PixelWallpaper.Renderer.Windows;

// Explorer-specific interop stays here, separate from both D3D and scene code.
internal static class DesktopNative
{
    public const uint Child = 0x40000000;
    public const uint Popup = 0x80000000;
    public const uint Layered = 0x00080000;
    public const uint NoRedirectionBitmap = 0x00200000;
    public const uint ObjectDestroy = 0x8001;
    public const uint ObjectLocationChange = 0x800B;
    public const uint Close = 0x0010;
    public const uint DisplayChange = 0x007E;
    public const uint SettingChange = 0x001A;
    public const uint HotKey = 0x0312;
    public const uint HostLost = 0x8001;
    public const uint HostMoved = 0x8002;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public delegate bool EnumWindowProcedure(nint window, nint parameter);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void WinEventProcedure(nint hook, uint eventType, nint window,
        int objectId, int childId, uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public uint Size;
        public NativeMethods.Rect Monitor;
        public NativeMethods.Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindowW(string className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindowExW(nint parent, nint after, string? className, string? title);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowProcedure callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    public static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    public static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetParent(nint window, nint newParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint window, out NativeMethods.Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(nint window, out NativeMethods.Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int MapWindowPoints(nint from, nint to, ref NativeMethods.Rect rect, uint count);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(NativeMethods.Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint SendMessageTimeoutW(nint window, uint message, nuint wParam,
        nint lParam, uint flags, uint timeout, out nuint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    public static extern nint GetWindowDpiAwarenessContext(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        WinEventProcedure callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint window, int id);
}
