namespace PixelWallpaper.Renderer.Windows;

internal static class FullscreenDetector
{
    public static bool IsForegroundFullscreen()
    {
        nint window = RuntimeNative.GetForegroundWindow();
        if (window == 0 || window == DesktopNative.GetShellWindow() ||
            !DesktopNative.IsWindowVisible(window) || RuntimeNative.IsIconic(window))
            return false;
        DesktopNative.GetWindowThreadProcessId(window, out uint processId);
        if (processId == Environment.ProcessId) return false;
        if (IsShellSurface(window)) return false;
        if (RuntimeNative.GetCloaked(window, 14, out uint cloaked, sizeof(uint)) < 0 || cloaked != 0)
            return false;

        nint monitor = DesktopNative.MonitorFromPoint(default, 1); // Wallpaper's primary monitor.
        if (RuntimeNative.MonitorFromWindow(window, 2) != monitor) return false;
        // DWM frame bounds omit invisible resize borders; client bounds exclude a
        // normal title bar. Both must cover the complete monitor, not its work area.
        nint previous = DesktopNative.SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
        if (previous == 0) return false;
        try
        {
            var info = new DesktopNative.MonitorInfo { Size = 40 };
            if (!DesktopNative.GetMonitorInfoW(monitor, ref info) ||
                RuntimeNative.GetFrameBounds(window, 9, out var frame, 16) < 0 ||
                !DesktopNative.GetClientRect(window, out var client)) return false;
            System.Runtime.InteropServices.Marshal.SetLastPInvokeError(0);
            int mapped = DesktopNative.MapWindowPoints(window, 0, ref client, 2);
            if (mapped == 0 && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 0) return false;
            return Covers(frame, info.Monitor) && Covers(client, info.Monitor);
        }
        finally { DesktopNative.SetThreadDpiAwarenessContext(previous); }
    }

    private static unsafe bool IsShellSurface(nint window)
    {
        char* name = stackalloc char[64];
        int length = RuntimeNative.GetClassNameW(window, name, 64);
        var value = new ReadOnlySpan<char>(name, length);
        return value.SequenceEqual("Progman") || value.SequenceEqual("WorkerW") ||
            value.SequenceEqual("Shell_TrayWnd") || value.SequenceEqual("Shell_SecondaryTrayWnd");
    }

    private static bool Covers(NativeMethods.Rect bounds, NativeMethods.Rect monitor) =>
        bounds.Left <= monitor.Left && bounds.Top <= monitor.Top &&
        bounds.Right >= monitor.Right && bounds.Bottom >= monitor.Bottom;
}
