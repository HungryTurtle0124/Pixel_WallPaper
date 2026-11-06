using System.Runtime.InteropServices;
using System.IO;

namespace PixelWallpaper.Shared;

// Linked into both executables: only scalar values cross the process boundary.
// Commands execute on the renderer's message thread; no pointers or JSON IPC.
internal static class RendererProtocol
{
    public const string RendererMutex = "Local\\PixelWallpaper.Renderer.Instance";
    public const string WindowClass = "PixelWallpaper.RendererControl";
    public const uint Apply = 0x8020;
    public const uint ReadOptions = 0x8021;
    public const uint ReadFrameCount = 0x8022;
    public const uint ReadPauseReasons = 0x8023;
    public const uint ReadAllocationCount = 0x8024;
    public const uint ReadScene = 0x8025;
    public const uint ReadStatus = 0x8026;
    public const uint Identify = 0x8027;
    public const uint ApplyChecked = 0x8028;
    public const uint Signature = 0x50575008; // Pixel Wallpaper, lifecycle protocol 8.
    public const uint Close = 0x0010;
    public static readonly uint ReadyMessage = RegisterWindowMessageW("PixelWallpaper.Renderer.Ready.v1");

    // Zero is the unsupported message response from a Milestone 5 renderer (Rainy Forest).
    public static string SceneFromCode(nuint code) => code switch
    {
        0 or 1 => WallpaperSettings.RainyForest,
        2 => WallpaperSettings.Campfire,
        3 => WallpaperSettings.MapleTree,
        _ => throw new IOException("The renderer reported an unknown scene.")
    };
    public static int SceneCode(string scene) => scene switch
    {
        WallpaperSettings.RainyForest => 1,
        WallpaperSettings.Campfire => 2,
        WallpaperSettings.MapleTree => 3,
        _ => throw new ArgumentException("Unsupported scene.", nameof(scene))
    };

    public static nuint Pack(WallpaperSettings settings) =>
        (nuint)(int)settings.PerformanceMode | (settings.PauseWhileFullscreen ? 0x100u : 0u);

    public static bool TryUnpack(nuint value, out int fps, out bool fullscreen)
    {
        fps = (int)(value & 0xFF);
        fullscreen = (value & 0x100) != 0;
        return (value & ~(nuint)0x1FF) == 0 && fps is 15 or 30 or 60;
    }

    public static nint FindWindow(int processId = 0, string windowClass = WindowClass)
    {
        nint result = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (processId != 0 && owner != processId) return true;
            var name = new System.Text.StringBuilder(128);
            if (GetClassNameW(window, name, name.Capacity) != 0 && name.ToString() == windowClass)
            { result = window; return false; }
            return true;
        }, 0);
        return result;
    }

    public static nuint Send(nint window, uint message, nuint value = 0, nint parameter = 0)
    {
        if (window == 0 || SendMessageTimeoutW(window, message, value, parameter, 0x22, 1000, out nuint result) == 0)
            throw new IOException("The renderer did not respond to the control request.");
        return result;
    }

    public static WallpaperSettings DecodeStatus(nuint status, out uint revision, out bool stopping)
    {
        if (!TryUnpack(status & 0x1FF, out int fps, out bool fullscreen) ||
            (status & 0xF000) != 0 || ((status >> 9) & 3) == 0)
            throw new IOException("The renderer reported invalid status.");
        revision = (uint)(status >> 16);
        stopping = (status & 0x800) != 0;
        return new WallpaperSettings
        {
            Wallpaper = SceneFromCode((status >> 9) & 3),
            PerformanceMode = (PerformanceMode)fps, PauseWhileFullscreen = fullscreen
        };
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowProcedure(nint window, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowProcedure callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nuint value,
        nint parameter, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint window, uint message, nuint value, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);
}
