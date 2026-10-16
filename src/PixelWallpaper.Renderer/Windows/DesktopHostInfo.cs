using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PixelWallpaper.Renderer.Windows;

internal sealed record DesktopHostInfo(nint Progman, nint Parent, nint IconView,
    nint WallpaperWorker, uint ExplorerProcessId, bool IsRaisedDesktop, nint DpiContext)
{
    public static DesktopHostInfo Discover()
    {
        nint progman = DesktopNative.FindWindowW("Progman", null);
        if (progman == 0 || progman != DesktopNative.GetShellWindow())
            throw new InvalidOperationException("The Explorer desktop shell is not available. Start Explorer, then try again or use --windowed.");
        DesktopNative.GetWindowThreadProcessId(progman, out uint explorerPid);
        using (var process = Process.GetProcessById((int)explorerPid))
        {
            if (!string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The desktop is not owned by Windows Explorer.");
        }

        // Undocumented shell request, not a public wallpaper API. Use a bounded,
        // abort-if-hung send and validate the resulting hierarchy, never guess an HWND.
        // Modern-layout background transparency can also depend on this request.
        if (DesktopNative.SendMessageTimeoutW(progman, 0x052C, 0xD, 1, 0x0002 | 0x0020, 1000, out _) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Explorer did not complete the bounded desktop-host request.");

        bool raised = (GetStyle(progman, -20) & DesktopNative.NoRedirectionBitmap) != 0;
        nint iconView = 0, parent = 0, wallpaper = 0;
        if (raised)
        {
            iconView = DesktopNative.FindWindowExW(progman, 0, "SHELLDLL_DefView", null);
            wallpaper = DesktopNative.FindWindowExW(progman, 0, "WorkerW", null);
            parent = progman;
        }
        else
        {
            DesktopNative.EnumWindows((top, _) =>
            {
                nint view = DesktopNative.FindWindowExW(top, 0, "SHELLDLL_DefView", null);
                if (view == 0 || !OwnedBy(top, explorerPid))
                    return true;
                nint candidate = DesktopNative.FindWindowExW(0, top, "WorkerW", null);
                if (candidate == 0 || !OwnedBy(candidate, explorerPid) ||
                    DesktopNative.FindWindowExW(candidate, 0, "SHELLDLL_DefView", null) != 0)
                    return true;
                iconView = view;
                parent = wallpaper = candidate;
                return false;
            }, 0);
        }

        if (parent == 0 || iconView == 0 || wallpaper == 0 ||
            !OwnedBy(parent, explorerPid) || !OwnedBy(iconView, explorerPid) || !OwnedBy(wallpaper, explorerPid))
            throw new InvalidOperationException("Explorer's desktop hierarchy is unsupported. No window was attached; use --windowed to run the scene normally.");
        if (raised && ((GetStyle(iconView, -20) & DesktopNative.Layered) == 0 ||
            DesktopNative.GetParent(iconView) != progman || DesktopNative.GetParent(wallpaper) != progman))
            throw new InvalidOperationException("Explorer's raised desktop does not have the expected layered icon view and wallpaper sibling.");
        if (!IsBelow(wallpaper, raised ? iconView : DesktopNative.GetParent(iconView)))
            throw new InvalidOperationException("The wallpaper host is not below the desktop icons in Z-order.");

        return new(progman, parent, iconView, wallpaper, explorerPid, raised,
            DesktopNative.GetWindowDpiAwarenessContext(parent));
    }

    internal static bool OwnedBy(nint window, uint processId)
    {
        DesktopNative.GetWindowThreadProcessId(window, out uint owner);
        return DesktopNative.IsWindow(window) && owner == processId;
    }

    internal static uint GetStyle(nint window, int index) => unchecked((uint)(long)DesktopNative.GetWindowLongPtr(window, index));

    internal static bool IsBelow(nint window, nint above)
    {
        // GW_HWNDNEXT walks siblings downward. This is discovery/validation, not polling.
        for (nint item = DesktopNative.GetWindow(above, 2); item != 0; item = DesktopNative.GetWindow(item, 2))
            if (item == window) return true;
        return false;
    }

    public string Describe() => $"{(IsRaisedDesktop ? "Raised Progman" : "Legacy WorkerW")}; Explorer PID {ExplorerProcessId}; " +
        $"parent=0x{Parent:X}, icons=0x{IconView:X}, wallpaper=0x{WallpaperWorker:X}";
}
