using System.ComponentModel;
using System.Runtime.InteropServices;
using PixelWallpaper.Renderer.Windows;

namespace PixelWallpaper.Renderer.Runtime;

// All state and callbacks live on the message/render thread. No worker, tasks,
// recurring managed timers, delegates per event, or per-frame Windows queries.
internal sealed class RuntimeStateManager : IDisposable
{
    internal const string WindowClass = "PixelWallpaper.RuntimeState";
    private readonly RenderWindow _renderWindow;
    private readonly NativeMethods.WindowProcedure _procedure;
    private readonly DesktopNative.WinEventProcedure _foregroundProcedure;
    private readonly nint _instance = NativeMethods.GetModuleHandleW(null);
    private nint _window;
    private nint _foregroundHook;
    private nint _displayNotification;
    private bool _registered;
    private bool _sessionRegistered;
    private bool _timerRunning;
    private bool _refreshPending;
    private bool _disposed;
    private uint _sessionId;
    private Exception? _callbackError;

    public PauseReason Reasons { get; private set; }
    public bool IsPaused => Reasons != PauseReason.None;
    public uint TimingVersion { get; private set; }
    public bool PauseWhileFullscreen { get; private set; } = true;

    public void SetPauseWhileFullscreen(bool enabled)
    {
        if (PauseWhileFullscreen == enabled) return;
        PauseWhileFullscreen = enabled;
        RefreshFullscreen();
    }

    public RuntimeStateManager(RenderWindow renderWindow)
    {
        _renderWindow = renderWindow;
        _procedure = WindowProcedure;
        _foregroundProcedure = OnForegroundChanged;
        try
        {
            var windowClass = new NativeMethods.WindowClass
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(), Procedure = _procedure,
                Instance = _instance, ClassName = WindowClass
            };
            if (NativeMethods.RegisterClassExW(in windowClass) == 0) throw new Win32Exception();
            _registered = true;
            // A hidden top-level HWND receives power broadcasts in preview and desktop modes.
            _window = NativeMethods.CreateWindowExW(0x08000080, WindowClass, "Pixel Wallpaper runtime",
                0, 0, 0, 0, 0, 0, 0, _instance, 0);
            if (_window == 0) throw new Win32Exception();
            if (!RuntimeNative.WTSRegisterSessionNotification(_window, 0)) throw new Win32Exception();
            _sessionRegistered = true;
            ReadInitialSession();
            _displayNotification = RuntimeNative.RegisterPowerSettingNotification(_window,
                in RuntimeNative.SessionDisplayStatus, 0);
            if (_displayNotification == 0) throw new Win32Exception();
            // EVENT_SYSTEM_FOREGROUND, OUTOFCONTEXT | SKIPOWNPROCESS.
            _foregroundHook = DesktopNative.SetWinEventHook(3, 3, 0, _foregroundProcedure, 0, 0, 2);
            if (_foregroundHook == 0) throw new Win32Exception();
            RefreshFullscreen();
        }
        catch { Dispose(); throw; }
    }

    private void ReadInitialSession()
    {
        // WTS_CURRENT_SESSION / WTSSessionInfoEx: don't assume unlocked at startup.
        if (!RuntimeNative.WTSQuerySessionInformationW(0, uint.MaxValue, 25, out nint buffer, out uint bytes))
            throw new Win32Exception();
        try
        {
            if (bytes < Marshal.SizeOf<RuntimeNative.SessionInfoPrefix>())
                throw new InvalidOperationException("Incomplete Windows session information.");
            var info = Marshal.PtrToStructure<RuntimeNative.SessionInfoPrefix>(buffer);
            if (info.Level != 1) throw new InvalidOperationException("Unsupported Windows session information level.");
            _sessionId = info.SessionId;
            // WTSActive = 0; WTS_SESSIONSTATE_UNLOCK = 1 on supported Windows 10/11.
            SetReason(PauseReason.Locked, info.Flags != 1);
            SetReason(PauseReason.Disconnected, info.ConnectionState != 0);
        }
        finally { RuntimeNative.WTSFreeMemory(buffer); }
    }

    private void OnForegroundChanged(nint hook, uint eventType, nint window, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        if (_disposed || _refreshPending) return;
        _refreshPending = DesktopNative.PostMessageW(_window, RuntimeNative.ForegroundChanged, 0, 0);
    }

    private unsafe nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == RuntimeNative.ForegroundChanged || (message == RuntimeNative.Timer && wParam == 1) ||
                message == DesktopNative.DisplayChange || message == DesktopNative.SettingChange)
            {
                _refreshPending = false;
                RefreshFullscreen();
                return 0;
            }
            if (message == RuntimeNative.SessionChange && (uint)(long)lParam == _sessionId)
            {
                switch (wParam)
                {
                    case 7: SetReason(PauseReason.Locked, true); break;
                    case 8: SetReason(PauseReason.Locked, false); break;
                    case 2: case 4: case 6: SetReason(PauseReason.Disconnected, true); break;
                    case 1: case 3: case 5: SetReason(PauseReason.Disconnected, false); break;
                }
                RefreshFullscreen();
                return 0;
            }
            if (message == RuntimeNative.PowerBroadcast)
            {
                switch (wParam)
                {
                    case 4: SetReason(PauseReason.Suspended, true); break; // PBT_APMSUSPEND
                    case 6: case 7: case 18: SetReason(PauseReason.Suspended, false); break;
                    case RuntimeNative.PowerSettingChange when lParam != 0:
                        // POWERBROADCAST_SETTING belongs to Windows for this call only.
                        // Read its blittable fields directly: PtrToStructure<T> boxes
                        // this struct on .NET 8, allocating on every notification.
                        var setting = (RuntimeNative.PowerSetting*)lParam;
                        if (setting->Setting == RuntimeNative.SessionDisplayStatus && setting->Length == sizeof(uint))
                            SetReason(PauseReason.DisplayOff, setting->Value == 0); // Dim (2) stays active.
                        break;
                }
                RefreshFullscreen();
                return 1; // TRUE: notification processed, no suspend veto.
            }
        }
        catch (Exception error)
        {
            _callbackError = error;
            _renderWindow.RequestClose(); // No exception crosses an unmanaged callback.
        }
        return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }

    private void SetReason(PauseReason reason, bool paused)
    {
        var updated = paused ? Reasons | reason : Reasons & ~reason;
        if (updated == Reasons) return;
        Reasons = updated;
        TimingVersion = unchecked(TimingVersion + 1);
    }

    private void RefreshFullscreen()
    {
        if (_disposed) return;
        bool canInspect = (Reasons & ~PauseReason.Fullscreen) == PauseReason.None;
        if (!PauseWhileFullscreen) SetReason(PauseReason.Fullscreen, false);
        else if (canInspect) SetReason(PauseReason.Fullscreen, FullscreenDetector.IsForegroundFullscreen());
        // Poll geometry only once a second while interactive. Foreground events
        // react promptly; this catches in-place F11/resize/transition changes.
        if (canInspect && PauseWhileFullscreen && !_timerRunning)
        {
            if (RuntimeNative.SetTimer(_window, 1, 1000, 0) == 0) throw new Win32Exception();
            _timerRunning = true;
        }
        else if ((!canInspect || !PauseWhileFullscreen) && _timerRunning)
        {
            RuntimeNative.KillTimer(_window, 1);
            _timerRunning = false;
        }
    }

    public void ThrowIfFailed()
    {
        if (_callbackError is not null)
            throw new InvalidOperationException("Windows runtime notification failed.", _callbackError);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_foregroundHook != 0) DesktopNative.UnhookWinEvent(_foregroundHook);
        if (_timerRunning) RuntimeNative.KillTimer(_window, 1);
        if (_displayNotification != 0) RuntimeNative.UnregisterPowerSettingNotification(_displayNotification);
        if (_sessionRegistered) RuntimeNative.WTSUnRegisterSessionNotification(_window);
        if (_window != 0) NativeMethods.DestroyWindow(_window);
        if (_registered) NativeMethods.UnregisterClassW(WindowClass, _instance);
        GC.KeepAlive(_foregroundProcedure);
        GC.KeepAlive(_procedure);
    }
}
