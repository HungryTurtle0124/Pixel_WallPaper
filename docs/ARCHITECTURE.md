# Architecture notes

Implementation details for the Milestones 1–4 prototype. Paths below are relative to the repository root. See [README](../README.md) for build/run instructions and current status.

## Desktop host and recovery

Desktop integration is isolated under `Windows/`. `RainyForestScene`, its layers,
`D3D11Renderer`, shaders, and `FramePacer` have no Explorer knowledge and were
unchanged for Milestone 3. Milestone 4 adds a small runtime manager and a pause
input to the pacer; the renderer, scenes, and desktop host remain unchanged.
No dependency was added.

Explorer's hierarchy is inspected at startup, rather than inferred from an OS
version number. On the tested Windows 11 raised-desktop layout, `Progman` owns a
layered `SHELLDLL_DefView` icon surface and a `WorkerW` wallpaper sibling. Our
opaque layered child is parented to `Progman`, below the icons and above that
wallpaper sibling. On the older layout, discovery looks for the Explorer-owned
top-level `WorkerW` immediately following the icon host in Z-order and parents
the renderer there. Every selected host is checked against Explorer's process.
Input-transparent/no-activate styles keep desktop input with Explorer.

Discovery uses `FindWindow`, `FindWindowEx`, `EnumWindows`, and a one-second
`SendMessageTimeout` for Explorer's undocumented `0x052C` request. Attachment uses
`SetParent`, explicit child/window styles, and `SetWindowPos`. The render HWND is
created with the host's DPI awareness context to avoid a cross-process DPI reset.
The selected primary monitor's full bounds are mapped to the parent's coordinates,
including the area underneath the taskbar. Display/settings and host-location
messages update bounds; the existing resize path updates the swap-chain buffers.
This does not enumerate or render additional monitors.

Two Explorer-scoped WinEvent hooks report host destruction and location changes.
A hidden top-level control HWND receives shutdown, display/settings, and
`TaskbarCreated` messages. Events post work to the same message/render thread;
there is no host polling thread, periodic host scan, or per-frame discovery.

Shutdown releases the pacer, scene, and D3D resources first, unregisters runtime
notifications and destroys their hidden window, then removes desktop hooks and
the hotkey, hides/detaches the owned render window, restores its original parent,
styles and bounds, and destroys the owned windows. Partial initialization also
cleans up. The application does not hide/destroy Explorer windows, change Explorer
styles, or change the saved wallpaper. A shell-created WorkerW remains Explorer-owned.

If discovery/attachment fails, the application releases its resources and reports
the error; `--windowed` is an explicit fallback. If Explorer destroys/replaces the
host during a run, rendering stops and the process exits. **After restarting
Explorer, launch the renderer again manually.** Automatic reattachment is deferred.

### Compatibility limits

The raised-desktop branch was tested on Windows 11 25H2.
The legacy WorkerW branch is implemented but has not been verified on Windows 10.
Custom shells and unrecognized hierarchies are rejected. The shell request and
animated-wallpaper placement are undocumented contracts and may change in future
Windows updates. Changing themes/desktop modes can recreate host windows and
therefore require a manual relaunch too. Automatic device-loss recovery is also
outside this milestone. Normal or maximized application windows continue to allow
rendering; completely covering the desktop with ordinary windows is not an
additional pause condition.

The approach follows the observed modern hierarchy discussed by the
[Lively maintainer](https://github.com/lively-community/lively/issues/2074), with
our own checked attachment/lifetime code. Microsoft's
[SetParent documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)
documents the style and DPI constraints. The documented
[IDesktopWallpaper API](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper)
controls images/slideshows rather than hosting a continuously rendered HWND.

## Runtime pausing and suspension

`RuntimeStateManager` owns a separate hidden top-level notification HWND. It runs
on the existing message/render thread in both desktop and preview modes. It has
no scene or D3D references, worker threads, task loops, or managed timers.
`PauseReason` is a small flags enum:

| Reason | Source / clearing condition |
| --- | --- |
| `None` (active) | No pause reasons remain. |
| `Fullscreen` | Foreground application covers the wallpaper's primary monitor; clears after it leaves fullscreen or loses foreground. |
| `Locked` | WTS lock/unlock messages for this process's Windows session. |
| `DisplayOff` | `GUID_SESSION_DISPLAY_STATUS`: off (0) pauses; on (1) or dim (2) clears. |
| `Suspended` | `PBT_APMSUSPEND`; cleared by critical, normal, or automatic resume notifications. |
| `Disconnected` | WTS console/remote disconnect or logoff; cleared on connect/logon. |

Reasons overlap. Unlocking does not resume while display-off is still set, and
a power-resume notification does not clear a session lock. Registration occurs
before reading initial `WTSSessionInfoEx`, so startup does not assume an unlocked,
connected session. The native WTS buffer is freed immediately. Registration or
initial-session-query failures report an error and clean up rather than silently
disabling required suspension behavior.

### Fullscreen decision

`FullscreenDetector` checks only the foreground top-level window. It excludes
the renderer's process, desktop/taskbar surfaces, minimized, invisible, and DWM
cloaked windows. The foreground window must be on the wallpaper's primary monitor.
Both DWM's visible frame bounds (without invisible resize borders) and the client
rectangle mapped to screen coordinates must cover the **full monitor bounds**,
rather than just its work area. Queries use per-monitor DPI awareness for consistent
coordinates and reuse stack/value data. An ordinary maximized window normally
fails because the client area excludes its title bar and/or taskbar space.

An out-of-context `EVENT_SYSTEM_FOREGROUND` hook queues a coalesced refresh when
focus changes. One native `WM_TIMER` check every **1 second** catches F11, resizing,
and fullscreen transitions within the same foreground HWND. No fullscreen query
runs each frame. This timer is removed while locked, disconnected, suspended, or
display-off, and reinstated when interactive again. While paused for fullscreen
alone, the one-second check stays available so in-place exits are detected.

This is a practical visibility heuristic, not an authoritative query of a game's
exclusive swap-chain mode. Borderless content covering the full screen counts as
fullscreen. A custom borderless maximized application whose client area covers the
entire monitor is indistinguishable from borderless fullscreen and also pauses.
Foreground overlays can cause rendering to resume even while a game remains
underneath. A window on another monitor does not pause this primary-monitor
wallpaper; multi-monitor policy remains out of scope. If geometry/DWM information
cannot be read, the detector conservatively leaves rendering active. Transitions
within one HWND may take about a second to be detected; Windows message scheduling
can add delay. Mixed-DPI and exclusive-game combinations have not all been tested.

### Blocking and resume

`FramePacer` pumps messages and checks pause state before allowing another frame.
While paused it cancels the frame timer and excludes both frame timer and DXGI
readiness handles from its infinite message-aware wait. Scene updates, resize
work, drawing, and `Present` are skipped together. Notifications and shutdown
messages remain responsive. An already-submitted frame can finish after a pause
event; no further frames are submitted after that event is processed.

Every reason change increments a small timing version. The pacer resets elapsed
time when that version changes, even if suspend and resume are processed in one
message batch. The first resumed particle update receives zero delta; there is no
catch-up burst. Previously consumed DXGI readiness stays latched correctly until
the next actual frame. The device, texture, buffers, and scene remain allocated;
pausing does not promise lower RAM/VRAM usage. Normal shutdown still disposes them.

Power handling uses supported `WM_POWERBROADCAST` notifications and
`RegisterPowerSettingNotification`, without changing power plans, preventing
sleep, requesting execution-state overrides, or waking the machine. Display
status is session-wide, not an independent per-monitor power reading. A monitor
switched off with a hardware button may not notify Windows. Modern Standby and
driver notification order can vary; display-off and lock reasons are retained
until their own corresponding resume events. GPU removal/driver reset during
sleep still uses the existing error-and-exit path; automatic device-loss and
Explorer recovery remain out of scope.

API references: [WTS session registration](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification),
[initial session flags](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ns-wtsapi32-wtsinfoex_level1_w),
[session display-status notifications](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids),
and [DWM window attributes](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute).

## Scene and layer structure

```text
IScene (Update, Render, Dispose)
└── RainyForestScene
    ├── StaticBackgroundLayer  — one immutable GPU texture + shader resource view
    └── RainParticleLayer      — fixed arrays of drops and colored vertices
```

`Program` constructs one renderer and one `RainyForestScene`. The renderer owns
the frame, internal render target, reusable vertex upload buffer, and presentation.
The scene supplies the draw order: background, then rain. Only the rain receives
updates. The two layers are small concrete classes; no scene manager, layer
registry, or additional layer interface is needed for this milestone.

`StaticBackgroundLayer` owns its `TextureResource`. Construction uses Windows
Imaging Component (WIC) to decode the PNG into BGRA pixels, creates a 480×270
immutable Direct3D texture once, then releases the decoder, COM objects, file
handle, and temporary pixel buffer references. No image data is retained on the
CPU by the scene. Each frame reuses the same shader resource view. The background
and final upscale use the existing point-sampled fullscreen-triangle pipeline.

## Code map

| File/class | Responsibility |
| --- | --- |
| `Program.cs` | Owns startup, one-thread message/render loop, shutdown, and error reporting. |
| `Windows/RenderWindow.cs` | Creates the HWND, processes close/resize/DPI messages, and records minimization. |
| `Windows/NativeMethods.cs` | Small set of Win32 interop declarations. |
| `Windows/DesktopNative.cs` | Explorer discovery, parenting, styles, DPI, events, and hotkey interop. |
| `Windows/DesktopHostInfo.cs` | Finds and validates the modern Progman or legacy WorkerW host. |
| `Windows/DesktopHost.cs` | Attaches/sizes the render window, handles host events, and restores window state on disposal. |
| `Runtime/PauseReason.cs` | Independent overlapping reasons for pausing; zero means active. |
| `Runtime/RuntimeStateManager.cs` | Notification HWND, WTS/power subscriptions, foreground hook and one-second geometry fallback. |
| `Windows/FullscreenDetector.cs` | Allocation-free foreground client/frame/monitor checks. |
| `Windows/RuntimeNative.cs` | Small set of WTS, power, timer, foreground and DWM declarations. |
| `Rendering/D3D11Renderer.cs` | Owns device, swap chain, buffers, shaders, render targets, and disposal. |
| `Rendering/ColoredVertex.cs` | Compact 12-byte position/color vertex. |
| `Rendering/Shaders/` | Simple color and point-sampled upscale shaders, source and embedded bytecode. |
| `Rendering/PngTextureLoader.cs` | Startup-only WIC PNG decode, validation, upload, and partial-failure cleanup. |
| `Rendering/TextureResource.cs` | Owns a texture and its shader resource view. |
| `Scenes/IScene.cs` | Minimal scene contract: update, render, and disposal. |
| `Scenes/RainyForestScene.cs` | Composes background and rain in draw order; owns its layers. |
| `Scenes/Layers/StaticBackgroundLayer.cs` | Loads once, draws the same texture, and disposes it. |
| `Scenes/Layers/RainParticleLayer.cs` | Reusable fixed arrays, delta-time motion, and batched rain geometry. |
| `Timing/FramePacer.cs` | 30 FPS cap and blocking waits that also service window messages. |

## Timing and resource use

The window and Direct3D immediate context run on the same thread. A monotonic
`Stopwatch` deadline enforces at least 1/30 second between frame starts. A
one-shot Windows waitable timer sleeps until the deadline;
`MsgWaitForMultipleObjectsEx` also wakes for window messages. No `SpinWait`,
`Sleep(0)`, periodic callbacks, or `timeBeginPeriod` is used. The timer requests
per-timer high resolution on Windows 10 1803+ so a 33.33 ms deadline does not
regularly round to about 46.9 ms (21 FPS). Older systems fall back to an ordinary
timer. It wakes for frame deadlines only and is canceled while minimized.
The ordinary-timer fallback allows 1 ms coalescing tolerance; high-resolution
timers use zero tolerance. Scheduler jitter and presentation can make actual FPS lower
than the cap; this is an upper limit, not a promise of exact 30 Hz cadence.

A two-buffer flip-discard swap chain uses a DXGI frame-latency waitable handle
and maximum frame latency of one. Both deadline and DXGI readiness are required
before drawing, including the first frame. Once readiness is consumed, the
pacer remembers it until a frame begins; it never repeatedly waits on a ready
handle. VSync handles presentation; it does not supply the 30 FPS cap.

When minimized, the timer is canceled, timer/DXGI handles are excluded from the
wait, and only window messages wake the thread. Resume resets elapsed time.
Late frames never cause catch-up bursts; movement delta is clamped to 100 ms
after other long interruptions, such as dragging the window.

Each active frame clears the small scene target, draws one background triangle
and one rain batch, then draws one upscale triangle into the window back buffer. The scene is
redrawn in full; this milestone does not implement dirty rectangles. There are
no per-frame managed allocations in the intended render path after warmup.
The two window-sized swap-chain buffers still consume GPU memory, even with a
small internal texture. The background adds one 480×270 BGRA texture (about
506 KiB of pixel storage before driver overhead). GPU, driver, and .NET runtime overhead must be measured.

Resources are created once and reused. Resize releases all references to the old
back buffer before `ResizeBuffers`; the fixed internal scene texture is retained.
`using` releases the pacer, scene texture, renderer, runtime subscriptions/window,
desktop host, then render window, including on exceptions.
Renderer construction cleans up partial initialization. Shutdown clears context
bindings, releases resources and COM wrappers, closes wait handles, flushes
deferred destruction once, and releases context/device. No GC-forced cleanup or
per-frame GPU synchronization query is used.

The app stops with an error dialog on initialization/device/presentation failure;
automatic device-loss recovery is outside this milestone.

## Dependencies and shaders

Direct packages: `Vortice.Direct3D11`, `Vortice.DXGI`, and `Vortice.Direct2D1`,
all pinned to 3.8.3. The last package supplies the `Vortice.WIC` image-decoding
types; this application does not create a Direct2D renderer. Its .NET 8 assembly
is approximately 523 KiB and reuses the existing Vortice/SharpGen dependencies.
`packages.lock.json` records the resolved dependency graph. Vortice pulls in
`Vortice.DirectX`, `Vortice.Mathematics`, `SharpGen.Runtime`, and
`SharpGen.Runtime.COM`, with the latter two currently using 2.4.2-beta as required
by Vortice. Required transitive Microsoft libraries include System.Text.Json,
System.Text.Encodings.Web, and System.IO.Pipelines. Referencing these assemblies
does not start background JSON processing or any browser/framework service.

No shader compiler is loaded by the running renderer. The four small `.cso`
files are embedded resources, generated from the adjacent HLSL files. After
editing HLSL, regenerate and rebuild:

```powershell
& .\tools\Compile-Shaders.ps1
dotnet build .\PixelWallpaper.sln -c Release
```

The development script uses the Windows `d3dcompiler_47.dll` through a temporary
PowerShell `Add-Type` wrapper, without an additional NuGet dependency or requiring
the Windows SDK shader tools. Pixel shader resources explicitly disable culture
inference because `ps` is also a language code.
