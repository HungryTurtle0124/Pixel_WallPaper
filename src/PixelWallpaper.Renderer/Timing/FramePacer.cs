using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using PixelWallpaper.Renderer.Windows;
using PixelWallpaper.Renderer.Runtime;

namespace PixelWallpaper.Renderer.Timing;

internal sealed class FramePacer : IDisposable
{
    private readonly SafeWaitHandle _timer;
    private long _interval = (long)Math.Ceiling(Stopwatch.Frequency / 30.0);
    private readonly uint _tolerableDelay;
    private long _nextFrame;
    private long _previousStart;
    private bool _slotReady;
    private bool _timerArmed;
    private uint _runtimeTimingVersion;
    public int FramesPerSecond { get; private set; } = 30;
    public long FrameCount { get; private set; }

    public void SetFrameRate(int framesPerSecond)
    {
        if (framesPerSecond is not (15 or 30 or 60)) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        if (FramesPerSecond == framesPerSecond) return;
        FramesPerSecond = framesPerSecond;
        _interval = (long)Math.Ceiling(Stopwatch.Frequency / (double)framesPerSecond);
        CancelTimer();
        _nextFrame = Stopwatch.GetTimestamp() + _interval;
        _previousStart = 0;
    }

    public FramePacer()
    {
        // Auto-reset, high-resolution ONE-SHOT timer, armed only while rendering. The
        // ordinary timer can quantize a 33.33 ms deadline to ~46.9 ms (roughly 21 FPS).
        // This does not change timer resolution through timeBeginPeriod or busy-wait.
        _timer = NativeMethods.CreateWaitableTimerExW(0, null, 0x00000002, 0x00100002);
        if (_timer.IsInvalid && System.Runtime.InteropServices.Marshal.GetLastWin32Error() == 87)
        {
            // The high-resolution flag requires Windows 10 1803. Older systems can
            // still run with an ordinary timer, accepting a lower/jitterier frame rate.
            _timer.Dispose();
            _timer = NativeMethods.CreateWaitableTimerExW(0, null, 0, 0x00100002);
            _tolerableDelay = 1;
        }
        if (_timer.IsInvalid)
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            _timer.Dispose();
            throw new Win32Exception(error);
        }
    }

    public unsafe bool WaitForFrame(RenderWindow window, nint frameLatencyHandle, RuntimeStateManager? runtime = null)
    {
        // Allocate once per call, not repeatedly inside the message/wait loop.
        nint* handles = stackalloc nint[2];
        while (true)
        {
            window.PumpMessages();
            runtime?.ThrowIfFailed();
            // Even suspend/resume messages drained in one batch must reset time.
            if (runtime is not null && _runtimeTimingVersion != runtime.TimingVersion)
            {
                _runtimeTimingVersion = runtime.TimingVersion;
                _previousStart = _nextFrame = 0;
            }
            if (window.CloseRequested)
                return false;

            if (window.IsMinimized || runtime?.IsPaused == true)
            {
                CancelTimer();
                _previousStart = _nextFrame = 0;
                // Exclude BOTH timer and DXGI handles: they must not wake a paused renderer.
                Wait(0, handles);
                continue;
            }

            long remaining = _nextFrame - Stopwatch.GetTimestamp();
            bool deadlineReady = remaining <= 0;
            if (deadlineReady && _slotReady)
            {
                CancelTimer();
                return true;
            }

            uint count = 0;
            int slotIndex = -1;
            if (!deadlineReady)
            {
                // Negative due times are relative 100 ns units. Round up, never spin to finish.
                long due = -(long)Math.Ceiling(remaining * (10_000_000.0 / Stopwatch.Frequency));
                // High-resolution timers require zero coalescing tolerance.
                if (!NativeMethods.SetWaitableTimerEx(_timer, in due, 0, 0, 0, 0, _tolerableDelay))
                    throw new Win32Exception();
                _timerArmed = true;
                handles[count++] = _timer.DangerousGetHandle();
            }
            else
            {
                CancelTimer();
            }
            if (!_slotReady)
            {
                slotIndex = (int)count;
                handles[count++] = frameLatencyHandle;
            }

            uint result = Wait(count, handles);
            if (slotIndex >= 0 && result == (uint)slotIndex)
                _slotReady = true;
            // A consumed DXGI signal stays latched until BeginFrame. Waiting on it again
            // before Present could block indefinitely; a signaled timer could cause a spin.
        }
    }

    public float BeginFrame()
    {
        long now = Stopwatch.GetTimestamp();
        float delta = _previousStart == 0 ? 0 : Math.Min((now - _previousStart) / (float)Stopwatch.Frequency, 0.1f);
        _previousStart = now;
        _nextFrame = now + _interval; // No catch-up bursts after a late frame.
        _slotReady = false;
        FrameCount++;
        return delta;
    }

    private static unsafe uint Wait(uint count, nint* handles)
    {
        const uint AllInput = 0x04FF;
        const uint InputAvailable = 0x0004;
        uint result = NativeMethods.MsgWaitForMultipleObjectsEx(count, handles,
            NativeMethods.Infinite, AllInput, InputAvailable);
        if (result == NativeMethods.WaitFailed)
            throw new Win32Exception();
        return result;
    }

    private void CancelTimer()
    {
        if (!_timerArmed)
            return;
        if (!NativeMethods.CancelWaitableTimer(_timer))
            throw new Win32Exception();
        _timerArmed = false;
    }

    public void Dispose()
    {
        if (_timer.IsClosed)
            return;
        NativeMethods.CancelWaitableTimer(_timer);
        _timer.Dispose();
    }
}
