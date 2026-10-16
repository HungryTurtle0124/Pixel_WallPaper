# Pixel Wallpaper

A lightweight Windows wallpaper engine for procedural pixel-art scenes. It uses
C#, .NET 8, Direct3D 11 through Vortice, and Win32 desktop integration to render
behind the desktop icons without a browser engine or video playback.

**Status: Milestones 1–6 complete.** A small WPF controller manages the separate
renderer process. Rainy Forest and Campfire are available; later milestones have not started.

## Current features

- A static pixel-art forest background and 96 reusable rain particles.
- Campfire: a static campsite, an 8 FPS fire sprite strip, 12 rising embers,
  and a small translucent flickering glow.
- **480×270 internal rendering**, upscaled with nearest-neighbor / point sampling.
- **Eco (15 FPS), Balanced (30 FPS, default), and Smooth (60 FPS)** caps using a
  blocking, message-aware waitable timer and DXGI readiness.
- WPF Start/Stop controls, scene selection, fullscreen-pause preference, and saved settings.
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
- The **.NET 8 Windows Desktop Runtime (WPF)** for the controller. The standalone
  renderer needs the .NET 8 runtime. Both executables are framework-dependent.
- To build, the **.NET SDK selected by `global.json`**: currently 10.0.402, with
  roll-forward within newer .NET 10 feature bands. This SDK builds the
  `net8.0-windows` target; the SDK version and runtime target are separate choices.

NuGet restores `Vortice.Direct3D11`, `Vortice.DXGI`, and `Vortice.Direct2D1` 3.8.3.
The last package supplies WIC image-decoding bindings; rendering uses Direct3D 11.
Package graphs are recorded in each project's `packages.lock.json`. The controller
uses built-in WPF and JSON APIs; no additional UI toolkit/package was added.

## Build

Close the controller/renderer before rebuilding. From the repository root in PowerShell:

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

Choose **Rainy Forest** or **Campfire**, select a performance mode, then click **Start Wallpaper**.
**Stop Wallpaper** requests a clean renderer shutdown. The status reflects whether
the tracked renderer process is alive, including while automatic pausing is active.
Only one controller and one renderer can run in a Windows session.

In Visual Studio, open `PixelWallpaper.sln`, set **PixelWallpaper.App** as the
startup project, choose **Release**, and press **Ctrl+F5**. A debugger can affect
performance measurements. The build copies the renderer, dependencies and asset
into the app's `Renderer/` folder; keep that folder beside the controller.

**Closing the settings window stops the wallpaper, then exits the controller.**
Minimizing the controller leaves the wallpaper running. If shutdown times out,
the settings window stays open with an error so Stop can be retried. Renderers
launched by the controller watch its process handle and request a clean exit if
it disappears unexpectedly. There is no tray icon or hidden controller mode.

An already-running renderer is detected when the controller opens. A new renderer
also announces startup, so an open controller can find it without polling. Stored
preferences are applied when connecting to a compatible renderer, and closing
the controller stops that attached renderer too. An older prototype must be
stopped and restarted before live settings can be applied.

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
defaults. Running state is not saved: opening the controller does not automatically
launch a wallpaper. No database or registry settings are used.

Performance mode and fullscreen-pause changes are acknowledged on the renderer's
message thread and take effect without restarting it or rebuilding GPU resources.
Changing the wallpaper while running cleanly stops the renderer and starts the
selected scene. The settings window remains open; only one renderer runs at a time.
This deliberately keeps GPU resource ownership simple. A brief return to the saved
Windows wallpaper during the switch is expected. The selected scene is saved as
`"rainy-forest"` or `"campfire"` in the existing `Wallpaper` field; old settings remain valid.
Disabling fullscreen pausing leaves lock, display-off, disconnection and suspend
pausing enabled. The controller has no recurring status timer or background scan.

To create a framework-dependent folder for distribution:

```powershell
dotnet publish .\src\PixelWallpaper.App -c Release --self-contained false -o .\publish
```

Keep the whole published folder, including `Renderer/`.

## Standalone renderer and preview

The renderer can still run without WPF:

```powershell
& .\src\PixelWallpaper.Renderer\bin\Release\net8.0-windows\PixelWallpaper.Renderer.exe
```

Standalone defaults remain Rainy Forest, Balanced/30 FPS with fullscreen pausing enabled.
Optional arguments are `--scene rainy-forest`, `--scene campfire`, `--fps 15`,
`--fps 30`, `--fps 60`, and `--no-fullscreen-pause`.
Direct launches do not read the controller's JSON preferences.

For the original normal-window preview:

```powershell
& .\src\PixelWallpaper.Renderer\bin\Release\net8.0-windows\PixelWallpaper.Renderer.exe --windowed
```

Append `--scene campfire` for the Campfire preview. Fire animation remains 8 FPS
at all three render caps. Pausing freezes fire, embers and flicker together.

Preview starts with a 960×540 client area. Resizing favors integer scaling and
centers the scene with dark borders. Minimize pauses rendering; restore resumes
without advancing particles through the paused time. Close the preview or press
Escape to exit. To debug this mode in Visual Studio, select `PixelWallpaper.Renderer`
as startup project and set its command-line argument to `--windowed`.

## Stop the wallpaper

Use **Stop Wallpaper** or close the settings window. The standalone command remains available:

```powershell
& .\src\PixelWallpaper.Renderer\bin\Release\net8.0-windows\PixelWallpaper.Renderer.exe --stop
```

**Ctrl+Alt+Shift+Q** also stops desktop mode if the hotkey was available at startup.
Use `--stop` if another application owns that shortcut. Shutdown releases owned
Direct3D and Win32 resources and reveals the saved Windows wallpaper.

## Measured performance

Milestone 6 renderer-only desktop comparison at 1920×1080 on a GTX 1650,
Balanced mode, controller closed:

| Metric | Rainy Forest | Campfire |
| --- | ---: | ---: |
| FPS (10-second sample) | 29.67 | 29.66 |
| Machine-normalized CPU | 0.267% | 0.178% |
| Process 3D GPU average / peak | 0.4% / 3% | 0.2% / 1% |
| RAM working set | 55.05 MiB | 57.46 MiB |
| Private memory | 37.94 MiB | 38.54 MiB |
| Dedicated GPU memory | 21.34 MiB | 21.42 MiB |
| Steady renderer-thread allocations | 0 bytes | 0 bytes |

CPU/memory/GPU sampling lasted 16.09 seconds for Rainy Forest and 13.17 seconds
for Campfire, after a three-second startup wait. GPU uses ten coarse WMI samples
and excludes DWM composition. These short sequential runs show comparable cost;
the lower Campfire CPU/GPU readings are not evidence of a guaranteed speedup.
Campfire adds about 0.08 MiB of measured dedicated GPU memory. Its working set
grew 1.90 MiB during startup/warmup while handles stayed unchanged; the sample
does not establish long-session stability.

Both scenes passed live mode checks: Rainy Forest measured **14.65 / 29.52 / 57.91**
FPS and Campfire **14.67 / 29.63 / 58.28** FPS in Eco/Balanced/Smooth. Each mode used
the same scene process, with three-second samples after warmup and zero measured
renderer-thread allocation bytes. Six alternating scene switches verified clean
exit before replacement, preserved options and no overlapping renderer processes.
Both scenes passed fullscreen pause/toggle and simulated lock/suspend tests.
The real Campfire lock/unlock check recorded Windows `Locked → None` notifications;
the user confirmed that fire and embers resumed normally, without a jump or error.
The WPF selector, running scene changes, saved selection after controller restart,
and close-window renderer shutdown were also checked.

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

Campfire also uses original, generated placeholder art. Files under
`src/PixelWallpaper.Renderer/Assets/` are:

| Asset | Required size/layout | Replacement requirements |
| --- | --- | --- |
| `Backgrounds/rainy-forest.png` | 480×270 | Opaque static PNG. |
| `Backgrounds/campfire.png` | 480×270 | Opaque static PNG. Put the campfire base near (256, 212); keep flame/embers out of the static artwork. |
| `Sprites/campfire-fire.png` | 256×40 | Eight 32×40 frames in one horizontal row, left to right; transparent straight-alpha PNG. |
| `Sprites/campfire-glow.png` | 64×48 | Transparent straight-alpha PNG with a faint warm glow; maximum bundled alpha is 52/255. |

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

## Project layout

| Path | Purpose |
| --- | --- |
| `src/PixelWallpaper.Renderer/` | Separate Direct3D renderer executable. |
| `src/PixelWallpaper.App/` | WPF controller, settings storage and process/control client. |
| `src/Shared/` | Small options/protocol types linked into both executables. |
| `Rendering/` within the project | Direct3D device, textures, shaders, and drawing. |
| `Scenes/` within the project | `SceneFactory`, Rainy Forest/Campfire scenes and reusable layers. |
| `Windows/` within the project | Win32 window, desktop host, fullscreen checks, and interop. |
| `Runtime/` and `Timing/` within the project | Pause reasons, notifications, and frame pacing. |
| `Control/` within the renderer | Message endpoint, command-line options and controller lifetime monitor. |
| `tests/PixelWallpaper.Verification/` | Separate, non-shipping regression helper. |
| `tools/` | Development-only profiling, shader compilation, and placeholder generation. |

See [architecture notes](docs/ARCHITECTURE.md) for implementation details and
[PROJECT_HANDOFF.md](PROJECT_HANDOFF.md) for the original design intent and roadmap.

## Verification helper

Stop the controller, wallpaper and other verification helpers before running.
For both scenes, live modes, allocations, pause behavior and repeated scene switching:

```powershell
$renderer = (Resolve-Path .\src\PixelWallpaper.App\bin\Release\net8.0-windows\Renderer\PixelWallpaper.Renderer.exe).Path
dotnet run --project .\tests\PixelWallpaper.Verification -c Release -- --scenes "$renderer"
```

This checks saved settings/error fallback, live FPS and allocation counts, duplicate
prevention, fullscreen-toggle behavior, mandatory lock/suspend pausing, and owner
exit cleanup and six alternating scene switches. **One labelled fullscreen test
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
Use `--animation` for the non-visual 8 FPS animation-clock checks at 15/30/60 FPS.
With one renderer running, `--sample 10` reports FPS and render-thread allocated
bytes over ten seconds; these are explicit development checks, not app polling.

The helper is outside the shipping solution and is never launched by the app.
After editing HLSL, run `tools/Compile-Shaders.ps1` and rebuild; commit the matching
`.cso` files. `tools/Generate-Placeholder.ps1` overwrites the bundled forest image
when explicitly invoked, so avoid running it over replacement artwork.

## Current limitations

- Two scenes and one primary-monitor wallpaper; no multi-monitor support.
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
- No Giant Maple Tree, tray icon, startup integration, wallpaper
  editor, online features, browser rendering, or video playback.
