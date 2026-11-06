using System.Diagnostics;
using System.Runtime.InteropServices;
using PixelWallpaper.Renderer.Rendering;
using PixelWallpaper.Renderer.Runtime;
using PixelWallpaper.Renderer.Scenes;
using PixelWallpaper.Renderer.Timing;
using PixelWallpaper.Renderer.Windows;

internal static class Verification
{
    private static long _frames;
    private static long _allocated;
    private static long _waitAllocated;
    private static long _zeroDeltas;
    private static RuntimeStateManager _runtime = null!;
    private static nint _runtimeWindow;
    private static nint _renderWindow;
    private static int _session;
    private static Exception? _failure;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] is "--lifecycle" or "--lifecycle-start" or "--lifecycle-hold")
        {
            try
            {
                if (args[0] == "--lifecycle") LifecycleVerification.RunAsync(args[1]).GetAwaiter().GetResult();
                else LifecycleVerification.StartProbeAsync(args[1], args[0] == "--lifecycle-hold").GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 2 && args[0] == "--sample")
        {
            try { SceneVerification.SampleAsync(int.Parse(args[1])).GetAwaiter().GetResult(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--animation")
        {
            try { SceneVerification.CheckAnimationTiming(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 2 && args[0] == "--scenes")
        {
            try { SceneVerification.RunAsync(args[1]).GetAwaiter().GetResult(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--settings")
        {
            try { ControllerVerification.CheckSettings(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--inspect")
        {
            nint endpoint = PixelWallpaper.Shared.RendererProtocol.FindWindow();
            if (endpoint == 0) { Console.WriteLine("Renderer: stopped"); return 0; }
            PixelWallpaper.Shared.RendererProtocol.GetWindowThreadProcessId(endpoint, out uint id);
            nuint options = PixelWallpaper.Shared.RendererProtocol.Send(endpoint, PixelWallpaper.Shared.RendererProtocol.ReadOptions);
            Console.WriteLine($"Renderer PID: {id}; scene: {PixelWallpaper.Shared.RendererProtocol.SceneFromCode(PixelWallpaper.Shared.RendererProtocol.Send(endpoint, PixelWallpaper.Shared.RendererProtocol.ReadScene))}; FPS cap: {options & 0xFF}; pause fullscreen: {(options & 0x100) != 0}; pause reasons: {PixelWallpaper.Shared.RendererProtocol.Send(endpoint, PixelWallpaper.Shared.RendererProtocol.ReadPauseReasons)}");
            return 0;
        }
        if (args.Length == 2 && args[0] is "--controls" or "--owner-exit")
        {
            try
            {
                if (args[0] == "--controls") ControllerVerification.RunAsync(args[1]).GetAwaiter().GetResult();
                else ControllerVerification.ExitOwnerAsync(args[1]).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length == 1 && args[0] == "--fixture") return Fixture.Run();
        if (args.Length == 1 && args[0] == "--hold-fullscreen") return Fixture.Run(true);
        if (args.Length == 1 && args[0] == "--observe") return ObserveSession();
        if (args.Length == 1 && args[0] == "--notifications")
        {
            _session = Process.GetCurrentProcess().SessionId;
            try { CheckNotificationAllocations(); CheckLifetimes(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        try { CheckDetectorAllocations(); Run(); CheckNotificationAllocations(); CheckLifetimes(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run()
    {
        var host = DesktopHostInfo.Discover();
        using var window = new RenderWindow(host.DpiContext);
        using var desktop = new DesktopHost(window, host);
        using var runtime = new RuntimeStateManager(window);
        _runtime = runtime;
        _runtimeWindow = DesktopNative.FindWindowW(RuntimeStateManager.WindowClass, null);
        _renderWindow = window.Handle;
        _session = Process.GetCurrentProcess().SessionId;
        using var renderer = new D3D11Renderer(window.Handle, window.Width, window.Height);
        using var scene = new RainyForestScene(renderer,
            Path.Combine(AppContext.BaseDirectory, "Assets", "Backgrounds", "rainy-forest.png"));
        using var pacer = new FramePacer();
        window.Show();
        long first = 0, last = 0;
        float shortest = float.MaxValue;
        Task? checks = null;
        long waitBaseline = GC.GetAllocatedBytesForCurrentThread();
        while (pacer.WaitForFrame(window, renderer.FrameLatencyHandle, runtime))
        {
            if (Interlocked.Read(ref _frames) >= 20)
                Interlocked.Add(ref _waitAllocated, GC.GetAllocatedBytesForCurrentThread() - waitBaseline);
            renderer.Resize(window.Width, window.Height);
            float delta = pacer.BeginFrame();
            long before = GC.GetAllocatedBytesForCurrentThread();
            scene.Update(delta);
            renderer.Render(scene);
            if (Interlocked.Read(ref _frames) >= 20)
                Interlocked.Add(ref _allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            if (delta == 0) Interlocked.Increment(ref _zeroDeltas);
            long count = Interlocked.Increment(ref _frames);
            if (count == 1) first = Stopwatch.GetTimestamp();
            if (count <= 100 && delta > 0) shortest = Math.Min(shortest, delta);
            if (count == 100)
            {
                last = Stopwatch.GetTimestamp();
                Console.WriteLine($"Active desktop: {99 * (double)Stopwatch.Frequency / (last - first):F3} FPS; shortest {shortest * 1000:F3} ms.");
                checks = Task.Run(CheckTransitions);
            }
            waitBaseline = GC.GetAllocatedBytesForCurrentThread();
        }
        checks?.GetAwaiter().GetResult();
        if (_failure is not null) throw new Exception("Runtime verification failed.", _failure);
        Require(Interlocked.Read(ref _allocated) == 0, "Recurring scene/render allocations");
        Require(shortest >= 1 / 30f - .0001f, "30 FPS cap");
        Console.WriteLine($"Message/pacer allocations across first-use transitions: {_waitAllocated} bytes total.");
        Console.WriteLine("PASS: zero steady frame/message/pacer allocations after notification warmup, zero paused frames, reset resume delta, notification overlaps and clean blocked shutdown.");
    }

    private static void CheckTransitions()
    {
        Process? fixture = null;
        try
        {
            fixture = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fixture")
            { RedirectStandardOutput = true, UseShellExecute = false })!;
            foreach (string expected in new[] { "normal", "maximized", "fullscreen", "restored" })
            {
                string? phase = fixture.StandardOutput.ReadLine();
                Require(phase == expected, "Fixture phase " + expected);
                Thread.Sleep(1500); // Allows the production one-second geometry fallback.
                bool fullscreen = expected == "fullscreen";
                Require(_runtime.IsPaused == fullscreen, expected + " pause classification: " + _runtime.Reasons);
                long before = Interlocked.Read(ref _frames);
                Thread.Sleep(1500);
                long rendered = Interlocked.Read(ref _frames) - before;
                Require(fullscreen ? rendered == 0 : rendered > 25, expected + " frame count");
                Console.WriteLine($"{expected}: reasons={_runtime.Reasons}; frames in 1.5 seconds={rendered}.");
            }
            Require(fixture.WaitForExit(10000) && fixture.ExitCode == 0, "Fixture shutdown");

            Console.WriteLine("Following WTS/power cases use synthetic notifications, not a real lock or sleep.");
            long baselineZero = Interlocked.Read(ref _zeroDeltas);
            Session(7); ExpectPaused(PauseReason.Locked);
            PowerSetting(0); ExpectPaused(PauseReason.Locked | PauseReason.DisplayOff);
            Session(8); ExpectPaused(PauseReason.DisplayOff);
            PowerSetting(2); ExpectActive(); // Dim is still visible.
            Require(Interlocked.Read(ref _zeroDeltas) > baselineZero, "Resume delta must reset to zero");
            Power(4); ExpectPaused(PauseReason.Suspended);
            Session(2); ExpectPaused(PauseReason.Suspended | PauseReason.Disconnected);
            Power(18); ExpectPaused(PauseReason.Disconnected);
            Session(1); ExpectActive();
            // Same-batch suspend/resume must reset time even if no wait observes pause.
            baselineZero = Interlocked.Read(ref _zeroDeltas);
            DesktopNative.PostMessageW(_runtimeWindow, RuntimeNative.PowerBroadcast, 4, 0);
            DesktopNative.PostMessageW(_runtimeWindow, RuntimeNative.PowerBroadcast, 18, 0);
            Thread.Sleep(500);
            Require(Interlocked.Read(ref _zeroDeltas) > baselineZero, "Batched resume timing reset");

            long warmedWaitAllocation = Interlocked.Read(ref _waitAllocated);
            Session(7); ExpectPaused(PauseReason.Locked);
            Session(8); ExpectActive();
            Require(Interlocked.Read(ref _waitAllocated) == warmedWaitAllocation,
                "Message/pacer allocations recur after warmup");

            // Close while blocked; shutdown must not depend on the next frame.
            Session(7); ExpectPaused(PauseReason.Locked);
            DesktopNative.PostMessageW(_renderWindow, NativeMethods.WmClose, 0, 0);
        }
        catch (Exception error)
        {
            _failure = error;
            DesktopNative.PostMessageW(_renderWindow, NativeMethods.WmClose, 0, 0);
        }
        finally
        {
            if (fixture is not null)
            {
                if (!fixture.HasExited) { fixture.CloseMainWindow(); fixture.WaitForExit(5000); }
                fixture.Dispose();
            }
        }
    }

    private static void ExpectPaused(PauseReason expected)
    {
        Thread.Sleep(250);
        Require(_runtime.Reasons == expected, "Expected " + expected + ", got " + _runtime.Reasons);
        long frames = Interlocked.Read(ref _frames);
        Thread.Sleep(1000);
        Require(Interlocked.Read(ref _frames) == frames, "Paused rendering must stop");
        Console.WriteLine($"Synthetic {expected}: zero frames in one second.");
    }

    private static void ExpectActive()
    {
        Thread.Sleep(250);
        Require(!_runtime.IsPaused, "Expected active, got " + _runtime.Reasons);
        long frames = Interlocked.Read(ref _frames);
        Thread.Sleep(1000);
        Require(Interlocked.Read(ref _frames) - frames > 20, "Rendering must resume");
    }

    private static void Session(nuint reason) =>
        SendMessageW(_runtimeWindow, RuntimeNative.SessionChange, reason, _session);
    private static void Power(nuint reason) => SendMessageW(_runtimeWindow, RuntimeNative.PowerBroadcast, reason, 0);
    private static void PowerSetting(uint value)
    {
        var data = new RuntimeNative.PowerSetting { Setting = RuntimeNative.SessionDisplayStatus, Length = 4, Value = value };
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<RuntimeNative.PowerSetting>());
        try
        {
            Marshal.StructureToPtr(data, buffer, false);
            SendMessageW(_runtimeWindow, RuntimeNative.PowerBroadcast, RuntimeNative.PowerSettingChange, buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void Require(bool condition, string check)
    { if (!condition) throw new Exception(check); }

    private static unsafe int ObserveSession()
    {
        // Test-only observer: no device or scene, never installed with the app.
        using var window = new RenderWindow();
        using var runtime = new RuntimeStateManager(window);
        nint handle = window.Handle;
        var monitor = Task.Run(() =>
        {
            PauseReason? previous = null;
            for (int sample = 0; sample < 180; sample++)
            {
                PauseReason current = runtime.Reasons;
                if (current != previous)
                    Console.WriteLine($"{DateTimeOffset.UtcNow:O} observed Windows state: {current}");
                previous = current;
                Thread.Sleep(1000);
            }
            DesktopNative.PostMessageW(handle, NativeMethods.WmClose, 0, 0);
        });
        while (!window.CloseRequested)
        {
            window.PumpMessages();
            runtime.ThrowIfFailed();
            if (!window.CloseRequested)
                NativeMethods.MsgWaitForMultipleObjectsEx(0, null, NativeMethods.Infinite, 0x04FF, 0x0004);
        }
        monitor.GetAwaiter().GetResult();
        return 0;
    }

    private static void CheckDetectorAllocations()
    {
        for (int warmup = 0; warmup < 100; warmup++) FullscreenDetector.IsForegroundFullscreen();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int check = 0; check < 1000; check++) FullscreenDetector.IsForegroundFullscreen();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, "Fullscreen queries allocate " + allocated + " bytes");
        Console.WriteLine("1000 warmed fullscreen queries: zero managed allocations.");
    }

    private static void CheckLifetimes()
    {
        using var window = new RenderWindow();
        using var process = Process.GetCurrentProcess();
        int baseline = 0;
        for (int cycle = 0; cycle < 30; cycle++)
        {
            using (var runtime = new RuntimeStateManager(window)) window.PumpMessages();
            process.Refresh();
            if (cycle == 4) baseline = process.HandleCount;
        }
        Require(process.HandleCount <= baseline + 2, "Runtime registration handle leak");
        Console.WriteLine($"30 runtime registration/disposal cycles: handles {baseline} -> {process.HandleCount}.");
    }

    private static unsafe void CheckNotificationAllocations()
    {
        using var window = new RenderWindow();
        using var runtime = new RuntimeStateManager(window);
        nint target = DesktopNative.FindWindowW(RuntimeStateManager.WindowClass, null);
        window.PumpMessages();
        var setting = new RuntimeNative.PowerSetting { Setting = RuntimeNative.SessionDisplayStatus, Length = 4, Value = 0 };
        for (int batch = 0; batch < 2; batch++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int cycle = 0; cycle < 1000; cycle++)
            {
                SendMessageW(target, RuntimeNative.SessionChange, 7, _session);
                SendMessageW(target, RuntimeNative.PowerBroadcast, RuntimeNative.PowerSettingChange, (nint)(&setting));
                SendMessageW(target, RuntimeNative.SessionChange, 8, _session);
                setting.Value = 1;
                SendMessageW(target, RuntimeNative.PowerBroadcast, RuntimeNative.PowerSettingChange, (nint)(&setting));
                setting.Value = 0;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (batch == 1) Require(allocated == 0, "Notification callbacks allocate " + allocated + " bytes");
            Console.WriteLine($"4000 notification callbacks (batch {batch}): {allocated} managed bytes.");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);

    private static class Fixture
    {
        private static NativeMethods.WindowProcedure? _procedure;
        private static int _phase = -1;
        private static bool _done;
        private static bool _holdFullscreen;
        private static NativeMethods.Rect _bounds;
        private static nint _brush;
        public static int Run(bool holdFullscreen = false)
        {
            _holdFullscreen = holdFullscreen;
            _brush = CreateSolidBrush(0x00232216);
            _procedure = Procedure;
            nint instance = NativeMethods.GetModuleHandleW(null);
            var windowClass = new NativeMethods.WindowClass
            { Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(), Procedure = _procedure,
                Instance = instance, ClassName = "PixelWallpaper.VerificationFixture" };
            NativeMethods.RegisterClassExW(in windowClass);
            nint window = NativeMethods.CreateWindowExW(0, windowClass.ClassName,
                "Pixel Wallpaper fullscreen test fixture", NativeMethods.WsOverlappedWindow,
                200, 200, 700, 400, 0, 0, instance, 0);
            DesktopNative.GetWindowRect(window, out _bounds);
            if (holdFullscreen)
            {
                DesktopNative.SetWindowLongPtr(window, -16, unchecked((nint)(long)DesktopNative.Popup));
                var info = new DesktopNative.MonitorInfo { Size = 40 };
                DesktopNative.GetMonitorInfoW(DesktopNative.MonitorFromPoint(default, 1), ref info);
                NativeMethods.SetWindowPos(window, 0, info.Monitor.Left, info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, 0x20);
            }
            NativeMethods.ShowWindow(window, 5);
            SetForegroundWindow(window);
            RuntimeNative.SetTimer(window, 1, holdFullscreen ? 30000u : 500u, 0);
            while (!_done && GetMessageW(out var message, 0, 0, 0) > 0)
            { NativeMethods.TranslateMessage(in message); NativeMethods.DispatchMessageW(in message); }
            NativeMethods.DestroyWindow(window);
            NativeMethods.UnregisterClassW(windowClass.ClassName, instance);
            DeleteObject(_brush);
            GC.KeepAlive(_procedure);
            return 0;
        }

        private static nint Procedure(nint window, uint message, nuint wParam, nint lParam)
        {
            if (message == NativeMethods.WmPaint)
            {
                nint dc = BeginPaint(window, out var paint);
                DesktopNative.GetClientRect(window, out var area);
                FillRect(dc, in area, _brush);
                SetTextColor(dc, 0x00F3F5EF);
                SetBkMode(dc, 1);
                area.Left += 30; area.Right -= 30; area.Top = Math.Max(30, area.Bottom / 2 - 30);
                DrawTextW(dc, "Pixel Wallpaper fullscreen verification\nThis test window closes automatically.", -1, ref area, 0x11);
                EndPaint(window, in paint);
                return 0;
            }
            if (message == NativeMethods.WmClose) { _done = true; return 0; }
            if (message == RuntimeNative.Timer)
            {
                if (_holdFullscreen) { _done = true; return 0; }
                if (_phase == -1)
                {
                    // Respect Windows foreground activation policy. The test can be
                    // brought to front manually if activation was denied.
                    if (RuntimeNative.GetForegroundWindow() != window) return 0;
                    _phase = 0;
                    RuntimeNative.SetTimer(window, 1, 4500, 0);
                    Console.WriteLine("normal");
                    return 0;
                }
                switch (++_phase)
                {
                    case 1:
                        NativeMethods.ShowWindow(window, 3);
                        Console.WriteLine("maximized");
                        break;
                    case 2:
                        NativeMethods.ShowWindow(window, 9);
                        DesktopNative.SetWindowLongPtr(window, -16, unchecked((nint)(long)(DesktopNative.Popup | 0x10000000u)));
                        var monitor = new DesktopNative.MonitorInfo { Size = 40 };
                        DesktopNative.GetMonitorInfoW(DesktopNative.MonitorFromPoint(default, 1), ref monitor);
                        var bounds = monitor.Monitor;
                        NativeMethods.SetWindowPos(window, 0, bounds.Left, bounds.Top,
                            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x20);
                        Console.WriteLine("fullscreen");
                        break;
                    case 3:
                        DesktopNative.SetWindowLongPtr(window, -16, (nint)(long)(NativeMethods.WsOverlappedWindow | 0x10000000u));
                        NativeMethods.SetWindowPos(window, 0, _bounds.Left, _bounds.Top,
                            _bounds.Right - _bounds.Left, _bounds.Bottom - _bounds.Top, 0x20);
                        Console.WriteLine("restored");
                        break;
                    default: _done = true; return 0;
                }
                SetForegroundWindow(window);
                return 0;
            }
            return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetMessageW(out NativeMethods.Message message, nint window, uint min, uint max);
        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct PaintData
        {
            public nint Dc;
            public int Erase;
            public NativeMethods.Rect Area;
            public int Restore, Update;
            public fixed byte Reserved[32];
        }
        [DllImport("user32.dll")] private static extern nint BeginPaint(nint window, out PaintData paint);
        [DllImport("user32.dll")] private static extern bool EndPaint(nint window, in PaintData paint);
        [DllImport("user32.dll")] private static extern int FillRect(nint dc, in NativeMethods.Rect area, nint brush);
        [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
        [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int DrawTextW(nint dc, string text, int length, ref NativeMethods.Rect area, uint format);
    }
}
