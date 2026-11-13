# Pixel Wallpaper

A lightweight Windows wallpaper engine for procedural pixel-art scenes. It uses
C#, .NET 8, Direct3D 11 through Vortice, and Win32 desktop integration to render
behind the desktop icons without a browser engine or video playback.

**Status: Milestones 1–9 implemented.** A small WPF controller and optional tray
control an independent renderer. Rainy Forest, Campfire, and Giant Maple Tree
are available. Windows sign-in startup is optional and defaults to off.

## Current features

- A static pixel-art forest background and 96 reusable rain particles.
- Campfire: a static campsite, an 8 FPS fire sprite strip, 12 rising embers,
  and a small translucent flickering glow.
- Giant Maple Tree: a static hill/tree, four small swaying canopy accents, three
  grass patches, and at most four slowly falling leaves. Clouds remain static.
- **480×270 internal rendering**, upscaled with nearest-neighbor / point sampling.
- **Eco (15 FPS), Balanced (30 FPS, default), and Smooth (60 FPS)** caps using a
  blocking, message-aware waitable timer and DXGI readiness.
- WPF Start/Stop controls, scene selection, fullscreen-pause preference, and saved settings.
- Optional tray controls and per-user Windows sign-in startup.
- Desktop wallpaper mode behind icons, plus a normal Win32 preview window.
- Automatic pause/resume for foreground fullscreen applications, session lock or
  disconnection, display-off, and Windows suspend/resume notifications.
- Overlapping pause reasons, timing reset on resume, GPU resource reuse, and
  deterministic cleanup. No busy-spinning or per-frame fullscreen polling.

Fullscreen detection combines foreground events with a one-second geometry check.
Normal maximized windows keep rendering active. While paused, scene updates,
drawing, and presentation stop; GPU resources remain resident for quick resume.

## Requirements

- Windows x64 and a Direct3D 11 GPU supporting feature level 11.0.
- **Windows 11 25H2 was tested. The Windows 10 desktop-host branch is not yet verified.**
  The Win32 APIs used require Windows 10 version 1703 or later.
- The **.NET 8 Windows Desktop Runtime** for WPF and the optional Windows Forms
  tray. The standalone renderer needs the .NET 8 runtime. All three executables
  are framework-dependent.
- To build, the **.NET SDK selected by `global.json`**: currently 10.0.402, with
  roll-forward within newer .NET 10 feature bands. This SDK builds the
  `net8.0-windows` target; the SDK version and runtime target are separate choices.

NuGet restores `Vortice.Direct3D11`, `Vortice.DXGI`, and `Vortice.Direct2D1` 3.8.3.
The last package supplies WIC image-decoding bindings; rendering uses Direct3D 11.
Package graphs are recorded in each project's `packages.lock.json`. The controller
uses built-in WPF and JSON APIs. The tray uses the Windows Forms `NotifyIcon`.

## Build

Close the settings window, exit the tray if open, and stop the renderer before
rebuilding. From the repository root in PowerShell:

```powershell
dotnet restore .\PixelWallpaper.sln --locked-mode
dotnet build .\PixelWallpaper.sln -c Release --no-restore
```

The background PNG and compiled shader bytecode are included in the repository.
No shader regeneration or extra asset download is required for a normal build.

## Run the settings application

```powershell
& .\src\PixelWallpaper.App\bin\Release\net8.0-windows\PixelWallpaper.App.exe
```

Choose **Rainy Forest**, **Campfire**, or **Giant Maple Tree**, select a performance
mode, then click **Start Wallpaper**.
**Stop Wallpaper** requests a clean renderer shutdown. The status reflects whether
the tracked renderer process is alive, including while automatic pausing is active.
Only one controller and one renderer can run in a Windows session.

In Visual Studio, open `PixelWallpaper.sln`, set **PixelWallpaper.App** as the
startup project, choose **Release**, and press **Ctrl+F5**. A debugger can affect
performance measurements. Keep the `Renderer/` and `Tray/` build output folders
beside the controller executable.

**Closing the settings window exits the controller and leaves the wallpaper running.**
Minimizing behaves normally. Closing waits for an in-flight command, disconnects,
then exits WPF completely. A controller crash also leaves the independent renderer
running. Use **Stop Wallpaper** to stop it explicitly. An open tray stays available
after the settings window closes.

Reopening the controller reconnects to the existing renderer without starting a
duplicate. Live scene/FPS/fullscreen settings are authoritative: the controller
reads them into the UI and saves them to JSON. Focus activation, process exit,
and renderer-ready and preference-change notifications refresh status without
idle polling. Stop pre-Milestone-8 renderers with `--stop` before upgrading.

## Optional tray and Windows startup

- Click **Show Tray Icon**, then right-click the notification area icon. It
  may appear under the taskbar overflow arrow. The menu shows wallpaper status
  and lets you choose a scene, change FPS, toggle fullscreen pausing, and start
  or stop the wallpaper. **Open Settings** opens or restores the WPF window.
- **Exit Tray** removes the icon and exits the tray process. The wallpaper keeps
  running. Use **Show Tray Icon** again whenever you want it back.
- Check **Start Wallpaper with Windows** to register the renderer for your next
  Windows sign-in. It starts with the saved scene, FPS cap, and fullscreen-pause
  preference. The settings window and tray stay closed. Uncheck the box to
  remove registration. This leaves the current wallpaper session running.
- Startup uses the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  value `PixelWallpaper`. The checkbox shows whether the entry is registered.
  Windows can delay the launch, and Startup Apps can disable it independently.
  If you move the app folder, uncheck and recheck the option to update the stored
  path. Registration needs no administrator rights.
  [Windows Run key documentation](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)

The sign-in command can be tested manually using the bundled renderer with
`--startup` after stopping the current renderer. It retries Explorer desktop
discovery for about 15 seconds. A failure exits and writes the diagnostic file
described below. The command does not change startup registration.

## Settings and live changes

Preferences are stored per user in **`%LOCALAPPDATA%\PixelWallpaper\settings.json`**:

```json
{
  "Wallpaper": "rainy-forest",
  "PerformanceMode": "Balanced",
  "PauseWhileFullscreen": true
}
```

Changes save automatically using a temporary file and atomic replacement. A
missing file uses defaults; an invalid/unreadable file shows a warning and uses
defaults. Running state is not saved, and opening the controller leaves a stopped
wallpaper stopped. Windows startup registration is separate from the JSON preferences.

Performance mode and fullscreen-pause changes are acknowledged on the renderer's
message thread and take effect without restarting it or rebuilding GPU resources.
Changing the wallpaper while running cleanly stops the renderer and starts the
selected scene. The settings window remains open; only one renderer runs at a time.
This deliberately keeps GPU resource ownership simple. A brief return to the saved
Windows wallpaper during the switch is expected. The selected scene is saved as
`"rainy-forest"`, `"campfire"`, or `"maple-tree"` in the existing `Wallpaper` field;
old settings remain valid.
Disabling fullscreen pausing leaves lock, display-off, disconnection and suspend
pausing enabled. The controller and tray have no recurring status timer or background scan.

While connected, live renderer settings win. With no renderer, saved preferences
are used for the next Start. The tray and WPF share a session-local command mutex
that covers scene switching and saving preferences. The renderer revision guards
live changes, and the client checks saved preferences before accepting changes
while stopped. If a change is stale, the control surface refreshes for retry.

IPC requests time out after one second, and explicit Stop waits up to five seconds.
After a manual Start, the control surface waits up to seven seconds for the
renderer endpoint. Waiting for another controller's transaction is bounded to
fifteen seconds, on a worker only during commands.
No renderer is force-killed or restarted automatically. If a connection is lost,
activate/reopen the controller to refresh, then retry the explicit action.

Controller launches are independent Windows shell launches without redirected
stdio pipes. Background-mode fatal errors are kept in one bounded local file,
`%LOCALAPPDATA%\PixelWallpaper\renderer-error.txt`; it is written only on failure.
Startup error readback checks PID and process creation time to avoid showing an
old failure. Standalone launches keep their error dialog.

To create a framework-dependent folder for distribution:

```powershell
dotnet publish .\src\PixelWallpaper.App -c Release --self-contained false -o .\publish
```

Keep the whole published folder, including `Renderer/` and `Tray/`.

## Standalone renderer and preview

The renderer can still run without WPF:

```powershell
& .\src\PixelWallpaper.Renderer\bin\Release\net8.0-windows\PixelWallpaper.Renderer.exe
```

Standalone defaults remain Rainy Forest, Balanced/30 FPS with fullscreen pausing enabled.
Optional arguments are `--scene rainy-forest`, `--scene campfire`, `--scene maple-tree`,
`--fps 15`, `--fps 30`, `--fps 60`, and `--no-fullscreen-pause`.
Use `--startup` to load the saved preferences and wait for Explorer readiness.

For the original normal-window preview:

```powershell
& .\src\PixelWallpaper.Renderer\bin\Release\net8.0-windows\PixelWallpaper.Renderer.exe --windowed
```

Append `--scene campfire` for the Campfire preview. Fire animation remains 8 FPS
at all three render caps. Pausing freezes fire, embers and flicker together.
Append `--scene maple-tree` for Giant Maple Tree. Canopy and grass offsets update
at 8 Hz on an eight-second cycle, with 2-pixel and 1-pixel maximum movement.
Only the small overlays move; the trunk, hill, and most of the crown stay static.
One falling leaf is attempted every 3.5–5.5 seconds, using a four-slot pool.

Preview starts with a 960×540 client area. Resizing favors integer scaling and
centers the scene with dark borders. Minimize pauses rendering; restore resumes
without advancing particles through the paused time. Close the preview or press
Escape to exit. To debug this mode in Visual Studio, select `PixelWallpaper.Renderer`
as startup project and set its command-line argument to `--windowed`.

## Stop the wallpaper

Use **Stop Wallpaper** in settings or the tray. The standalone command remains available:

```powershell
& .\src\PixelWallpaper.Renderer\bin\Release\net8.0-windows\PixelWallpaper.Renderer.exe --stop
```

**Ctrl+Alt+Shift+Q** also stops desktop mode if the hotkey was available at startup.
Use `--stop` if another application owns that shortcut. Shutdown releases owned
Direct3D and Win32 resources and reveals the saved Windows wallpaper.

## Measured performance

Milestone 9 used the same Giant Maple Tree renderer at Balanced on Windows 11
25H2 and a GTX 1650. Each Milestone 9 row uses a 20-second sample. CPU is
normalized to the whole machine. GPU values are per-process 3D-engine averages
and exclude DWM. Working sets include shared pages.

| Configuration | FPS | Renderer CPU / GPU | Renderer working set / private | Control CPU / GPU | Control working set / private |
| --- | ---: | ---: | ---: | ---: | ---: |
| Milestone 8, renderer alone | 29.67 | 0.337% / 0.05% | 55.18 / 38.03 MiB | — | — |
| Milestone 9, renderer alone | 29.57 | 0.283% / 0.40% | 55.40 / 38.52 MiB | — | — |
| Milestone 9, with idle tray | 29.62 | 0.332% / 0.65% | 49.50 / 38.66 MiB | 0.000% / no counter exposed | 46.79 / 10.44 MiB |
| Milestone 9, with idle WPF | 29.59 | 0.273% / 0.30% | 46.82 / 34.63 MiB | 0.000% / 0.00% | 110.07 / 55.27 MiB |

The renderer-only sample had a 48-byte render-thread allocation delta. The later
tray and WPF samples and all nine warmed scene/mode checks each recorded zero
render-thread allocated bytes. The short GPU readings varied between runs.

Verified on Windows 11 25H2: startup registration and removal, direct `--startup`
launch with saved preferences, a diagnostic and clean exit on a missing asset,
real lock/unlock, and fullscreen pause/resume. Actual Windows sign-in startup
still needs an end-to-end test.

Milestone 8: Giant Maple Tree, Balanced mode, started by WPF and measured after
the controller process had exited completely. No extra resident service/helper:

| Metric | Milestone 7 Maple | Milestone 8, controller closed |
| --- | ---: | ---: |
| FPS | 29.63 | 29.67 |
| Machine-normalized CPU | 0.399% | 0.337% |
| Process 3D GPU average / peak | 0.8% / 3% | 0.05% / 1% |
| RAM working set | 53.08 MiB | 55.18 MiB |
| Private memory | 37.93 MiB | 38.03 MiB |
| Dedicated GPU memory | 21.36 MiB | 21.35 MiB |
| Warm renderer-thread allocations | 0 bytes | 0 bytes |

The M8 FPS/allocation sample lasted 20 seconds; CPU/GPU/memory sampling lasted
28.94 seconds with twenty coarse WMI GPU samples. Working set and handle count
were unchanged during the sample, and the controller process count stayed zero.
This removes the WPF process's separate resident cost (the M5 idle measurement
was 113 MiB working set / 45 MiB private memory). Process working sets include
shared pages, so that is not a claim of exactly 113 MiB of system-wide RAM saved.
GPU samples exclude DWM and vary with drivers/background activity; lower counters
in a short run do not establish a guaranteed speedup. Renderer cost remains in
the same general range as M7. Long-session/device-loss recovery is not established.

The lifecycle helper passed concurrent starts, normal controller exit, forced
test-controller crash, abandoned command-mutex recovery, stale-revision rejection,
renderer crash/explicit restart, and explicit Stop. Sixty-four reconnect/dispose
cycles stayed within three handles of the helper's baseline. The three-scene mode/pause suite
passed with zero warmed renderer-thread allocations. Its first scene allocation
sample allows shell-startup notifications/interop to settle before measuring;
startup itself is not allocation-free. The real lock/unlock check passed with
WPF absent (`Locked → None`, user-confirmed smooth resume). WPF Start/close/reopen,
same-PID reconnection despite deliberately stale JSON, scene switching after
reconnect, forced WPF exit/survival, and the Stop button were also verified.

Milestone 7 renderer-only desktop comparison at 1920×1080 on a GTX 1650,
Balanced mode, controller closed:

| Metric | Rainy Forest | Campfire | Giant Maple Tree |
| --- | ---: | ---: | ---: |
| FPS (20-second sample) | 29.61 | 29.68 | 29.63 |
| Machine-normalized CPU | 0.309% | 0.362% | 0.399% |
| Process 3D GPU average / peak | 0.35% / 2% | 0.55% / 2% | 0.8% / 3% |
| RAM working set | 52.95 MiB | 52.98 MiB | 53.08 MiB |
| Private memory | 37.86 MiB | 37.83 MiB | 37.93 MiB |
| Dedicated GPU memory | 21.35 MiB | 21.42 MiB | 21.36 MiB |
| Steady renderer-thread allocations | 0 bytes | 0 bytes | 0 bytes |

CPU/memory/GPU sampling lasted 29.10 / 26.46 / 26.43 seconds, respectively, after
five-second startup waits. GPU uses twenty coarse WMI samples and excludes DWM
composition. These short sequential runs show the same general resource range;
they do not establish long-session stability. Maple's working set decreased
0.26 MiB during the sample and handle count decreased by one. The allocation
sample spans leaf spawning, expiration and slot reuse.

Live Eco/Balanced/Smooth checks measured **14.96 / 29.62 / 57.82** FPS for Rainy
Forest, **14.93 / 29.65 / 58.16** for Campfire, and **14.93 / 29.30 / 58.24** for
Giant Maple Tree. Each mode used the same scene process, with three-second samples
after warmup and zero renderer-thread allocation bytes. Six switches across all
three scenes verified clean exit before replacement, preserved preferences and
stable attachment. All three passed fullscreen pause/toggle and simulated
lock/suspend tests. Display-off/suspend hardware transitions are not forced.
The real Maple lock/unlock check recorded `Locked → None` notifications; the user
confirmed that sway and falling leaves resumed without a jump or error. The WPF
selector saved `maple-tree`, restored it after controller restart, and started
the selected scene successfully. M8 supersedes M7's original close-to-stop policy.

Milestone 5 live-mode checks at 1920×1080 on a GTX 1650, using the same renderer process:

| Mode | Measured FPS | Steady renderer-thread allocations |
| --- | ---: | ---: |
| Eco (15 target) | 14.93 | 0 bytes |
| Balanced (30 target) | 29.62 | 0 bytes |
| Smooth (60 target) | 58.38 | 0 bytes |

Each FPS sample lasted three seconds after warmup. The frame cap is an upper
limit; scheduling, VSync and monitor refresh rate can lower actual FPS.

The open WPF controller recorded **0.000% machine-normalized CPU** over a ten-second
idle sample, **113.07 MiB working set**, and **44.88 MiB private memory**, with stable
memory/handles. WPF adds a separate memory cost; the controller does not load
Vortice or the scene renderer. WPF uses its own UI rendering/composition system.
Working set and private bytes are different measurements, not values to add together.

Renderer-only Balanced baseline from Milestone 4 (short Release-mode samples):

| Metric | Active | Paused by fullscreen |
| --- | ---: | ---: |
| FPS | About 29.65 | 0 |
| Machine-normalized CPU | 0.324% | 0.000% measured |
| Process 3D GPU average / peak | 0.25% / 1% | 0% / 0% |
| RAM working set | 51.83 MiB | 51.91 MiB |
| Private memory | 37.16 MiB | 37.20 MiB |
| Dedicated GPU memory | 21.34 MiB | 21.35 MiB |

CPU/GPU samples lasted about 11–12 seconds; FPS came from a separate verification
run. GPU counters exclude Desktop Window Manager composition work. These are
observations, not guarantees: coarse counters can round small costs to zero, and
hardware, drivers, and display size affect results. Retaining resources while
paused intentionally keeps RAM/VRAM usage similar.

After warmup, scene rendering, repeated notification callbacks, and repeated
pause/resume handling showed no recurring managed allocations. Repeated native
notification registration/disposal showed stable handle counts. Real lock/unlock
was checked; display-off and suspend/resume handlers were tested with simulated
notifications, without forcing hardware sleep.

To measure a running Release instance:

```powershell
& .\tools\Measure-Renderer.ps1 -Seconds 15 -IncludeGpu
```

Task Manager's Details view can also show CPU, memory, GPU engine, and GPU memory.
Driver/WMI GPU counters may be unavailable on some systems.

## Replacing placeholder art

The bundled forest is a temporary, generated placeholder. To replace it, stop the
app and replace `src/PixelWallpaper.Renderer/Assets/Backgrounds/rainy-forest.png`
with an **opaque, exactly 480×270 PNG**, keeping its name. Rebuild and restart.
The image is loaded once; there is no live reload. The build copies it beside the
executable under `Assets/Backgrounds/`.

Campfire and Giant Maple Tree also use original, generated placeholder art. Files under
`src/PixelWallpaper.Renderer/Assets/` are:

| Asset | Required size/layout | Replacement requirements |
| --- | --- | --- |
| `Backgrounds/rainy-forest.png` | 480×270 | Opaque static PNG. |
| `Backgrounds/campfire.png` | 480×270 | Opaque static PNG. Put the campfire base near (256, 212); keep flame/embers out of the static artwork. |
| `Sprites/campfire-fire.png` | 256×40 | Eight 32×40 frames in one horizontal row, left to right; transparent straight-alpha PNG. |
| `Sprites/campfire-glow.png` | 64×48 | Transparent straight-alpha PNG with a faint warm glow; maximum bundled alpha is 52/255. |
| `Backgrounds/maple-tree.png` | 480×270 | Opaque static hill and tree, including trunk and most of the canopy. |
| `Sprites/maple-leaf-cluster.png` | 32×24 | One transparent straight-alpha canopy accent, reused four times. |
| `Sprites/maple-grass.png` | 32×12 | One transparent straight-alpha grass patch, reused three times. |
| `Sprites/maple-falling-leaf.png` | 16×8 | Two transparent 8×8 leaf silhouettes in one horizontal row, left to right. |

The fire is drawn at (240, 174), each frame at native 32×40 size, looping at 8 FPS.
Keep transparent padding inside each frame; do not use a checkerboard background
or pre-multiply the PNG's colors. Glow is drawn at (224, 174) behind the fire;
12 embers originate near (256, 196). All coordinates use the 480×270 canvas.
The texture loader checks exact dimensions before uploading. To change placement,
frame count/size or animation rate, edit `Scenes/CampfireScene.cs` as well as the art.

Stop the wallpaper, replace the named PNGs, rebuild, and start again. Assets are
loaded once per renderer lifetime; there is no per-frame decoding or live reload.
`tools/Generate-Campfire.ps1` regenerates only the three Campfire placeholders;
run it only when intentionally overwriting those files. No third-party art is bundled.

Maple canopy overlays sit at (177,89), (214,53), (283,65), and (307,105), plus up to
2 pixels of horizontal sway. Grass patches sit at (169,202), (292,205), and
(352,224), plus 1 pixel of horizontal sway. Leaves originate across the lower crown
(x=183–317, y=89–127). Keep the background complete underneath these overlays:
they add small accents rather than covering holes in the static artwork.
To change placement or dimensions, edit `Scenes/MapleTreeScene.cs`; leaf spawn
bounds are in `Scenes/Layers/FallingLeafParticleLayer.cs`.

`tools/Generate-MapleTree.ps1` recreates only the four original Maple placeholders.
It overwrites those files only when explicitly invoked. Cloud silhouettes are
baked into the background: moving clouds are intentionally skipped to keep this
scene focused on localized motion, without another texture or draw layer.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/PixelWallpaper.Renderer/` | Separate Direct3D renderer executable. |
| `src/PixelWallpaper.App/` | WPF settings window and per-user startup registration. |
| `src/PixelWallpaper.Tray/` | Optional notification area icon and menu. |
| `src/Shared/` | Shared renderer client, settings store, command gate, and protocol. |
| `Rendering/` within the project | Direct3D device, textures, shaders, and drawing. |
| `Scenes/` within the project | `SceneFactory`, three scenes, and reusable background/animation/sway/particle layers. |
| `Windows/` within the project | Win32 window, desktop host, fullscreen checks, and interop. |
| `Runtime/` and `Timing/` within the project | Pause reasons, notifications, and frame pacing. |
| `Control/` within the renderer | Lightweight message endpoint and command-line options; no parent lifetime watcher. |
| `tests/PixelWallpaper.Verification/` | Separate, non-shipping regression helper. |
| `tools/` | Development-only profiling, shader compilation, and placeholder generation. |

See [architecture notes](docs/ARCHITECTURE.md) for implementation details and
[PROJECT_HANDOFF.md](PROJECT_HANDOFF.md) for the original design intent and roadmap.

## Verification helper

Stop the controller, wallpaper and other verification helpers before running.
Use `--startup-check "$renderer"` with the executable path below to check the
startup registration logic in an isolated test key, bounded Explorer retries,
and stale settings protection between two disconnected clients.

For Milestone 8 lifecycle checks (no visible fullscreen fixture):

```powershell
$renderer = (Resolve-Path .\src\PixelWallpaper.App\bin\Release\net8.0-windows\Renderer\PixelWallpaper.Renderer.exe).Path
dotnet run --project .\tests\PixelWallpaper.Verification -c Release -- --lifecycle "$renderer"
```

This launches concurrent test controllers, checks live-state authority and stale
revision rejection, repeats reconnect/dispose, deliberately kills a test controller
while it owns the command mutex, and deliberately crashes a test renderer. It
verifies survival/reconnection and explicit recovery/Stop, then cleans up. Run it
only with the normal wallpaper stopped. These crash tests are never run by the app.

For all three scenes, live modes, allocations, pause behavior and repeated scene switching:

```powershell
$renderer = (Resolve-Path .\src\PixelWallpaper.App\bin\Release\net8.0-windows\Renderer\PixelWallpaper.Renderer.exe).Path
dotnet run --project .\tests\PixelWallpaper.Verification -c Release -- --scenes "$renderer"
```

This checks saved settings/error fallback, live FPS and allocation counts, duplicate
prevention, fullscreen-toggle behavior, mandatory lock/suspend pausing, controller
exit survival/reconnection, and six switches across all three scenes. **One labelled fullscreen test
window appears briefly for each scene and closes automatically.** `--controls`
instead of `--scenes` retains the focused Rainy Forest controller checks.
Synthetic session/power messages do not lock or suspend Windows. GUI Start/Stop,
controller restart/persistence, external-renderer discovery, duplicate controller
prevention, and close-window shutdown were also checked on the WPF application.

The original Milestone 4 regression sequence remains available:

```powershell
dotnet run --project .\tests\PixelWallpaper.Verification -c Release
```

**This intentionally opens a labelled test window**, then maximizes it, makes
it fullscreen, restores it, and closes it to check frame suspension. Keep the
fixture in the foreground until it finishes. The helper also checks notification
handling, resume timing, allocations, and native registration cleanup. Synthetic
power/session tests do not lock or suspend Windows.

For callback allocation/cleanup checks without a visible fixture:

```powershell
dotnet run --project .\tests\PixelWallpaper.Verification -c Release -- --notifications
```

Use `--settings` instead of `--notifications` for non-visual JSON persistence,
atomic replacement, and invalid/null/oversized settings-file checks.
Use `--animation` for the non-visual sprite/sway clock checks at 15/30/60 FPS.
With one renderer running, `--sample 10` reports FPS and render-thread allocated
bytes over ten seconds; these are explicit development checks, not app polling.

The helper is outside the shipping solution and is never launched by the app.
After editing HLSL, run `tools/Compile-Shaders.ps1` and rebuild; commit the matching
`.cso` files. `tools/Generate-Placeholder.ps1` overwrites the bundled forest image
when explicitly invoked, so avoid running it over replacement artwork.

## Current limitations

- Three scenes and one primary-monitor wallpaper; no multi-monitor support.
- IPC is for the current Windows session and compatible renderer version. Process
  handle/creation time, executable identity, window PID and protocol signature are
  checked; the local Win32 message channel is not an authentication/security boundary.
  Run controller and renderer at the same elevation. Stop old-version renderers
  with `--stop` before replacing application binaries.
- A controller crash during the stop/start gap of a scene switch may leave the
  wallpaper stopped. A slow startup can continue after the controller's timeout;
  reopen it to reconnect or use `--stop`. There is no background restart service
  or forced recovery of an unresponsive GPU/process.
- Explorer desktop hosting uses an undocumented shell arrangement that can change
  with Windows updates. Unrecognized hosts fail with an error. Explorer restart
  stops the renderer; relaunch it manually once Explorer is ready.
- Fullscreen is a geometry heuristic. Borderless windows covering the whole monitor
  count as fullscreen; custom borderless maximized windows can be indistinguishable.
  Overlays and unusual exclusive games can affect detection. In-place transitions
  can take about one second to detect, plus Windows scheduling delay.
- Display-power notifications depend on Windows and the driver; a physical monitor
  power button may not generate an event. Actual sleep/hibernate and all mixed-DPI
  or game configurations have not been verified. Device loss stops the app; there
  is no automatic device recovery.
- No moving clouds, wallpaper
  editor, online features, browser rendering, or video playback.
