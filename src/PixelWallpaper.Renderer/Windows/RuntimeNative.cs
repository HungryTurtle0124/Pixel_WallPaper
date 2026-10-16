using System.Runtime.InteropServices;

namespace PixelWallpaper.Renderer.Windows;

internal static class RuntimeNative
{
    public static readonly Guid SessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    public const uint ForegroundChanged = 0x8003; // Private WM_APP message, not a WinEvent.
    public const uint SessionChange = 0x02B1;
    public const uint PowerBroadcast = 0x0218;
    public const uint PowerSettingChange = 0x8013;
    public const uint Timer = 0x0113;

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern unsafe int GetClassNameW(nint window, char* name, int capacity);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint window);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int GetFrameBounds(nint window, uint attribute, out NativeMethods.Rect bounds, uint size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int GetCloaked(nint window, uint attribute, out uint cloaked, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool KillTimer(nint window, nuint id);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSUnRegisterSessionNotification(nint window);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSQuerySessionInformationW(nint server, uint session, int infoClass,
        out nint buffer, out uint bytes);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(nint buffer);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint RegisterPowerSettingNotification(nint window, in Guid setting, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterPowerSettingNotification(nint notification);

    // WTSINFOEX's union has 8-byte alignment on this x64 executable. Read only
    // its fixed prefix; no marshaled user/domain strings or retained WTS buffer.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct SessionInfoPrefix
    {
        [FieldOffset(0)] public uint Level;
        [FieldOffset(8)] public uint SessionId;
        [FieldOffset(12)] public int ConnectionState;
        [FieldOffset(16)] public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PowerSetting
    {
        public Guid Setting;
        public uint Length;
        public uint Value;
    }
}
