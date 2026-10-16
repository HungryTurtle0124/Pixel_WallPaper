# Pixel Wallpaper

A lightweight Windows wallpaper engine for procedural pixel-art scenes. It uses
C#, .NET 8, Direct3D 11 through Vortice, and Win32 desktop integration to render
behind the desktop icons without a browser engine or video playback.

**Status: Milestones 1–4 complete.** Milestone 5 (settings UI) has not started.
This is an early prototype with one Rainy Forest scene.

## Current features

- A static pixel-art forest background and 96 reusable rain particles.
- **480×270 internal rendering**, upscaled with nearest-neighbor / point sampling.
- A **30 FPS cap** using a blocking, message-aware waitable timer and DXGI readiness.
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
- The **.NET 8 runtime** to run the framework-dependent executable.
- To build, the **.NET SDK selected by `global.json`**: currently 10.0.402, with
  roll-forward within newer .NET 10 feature bands. This SDK builds the
  `net8.0-windows` target; the SDK version and runtime target are separate choices.

NuGet restores `Vortice.Direct3D11`, `Vortice.DXGI`, and `Vortice.Direct2D1` 3.8.3.
The last package supplies WIC image-decoding bindings; rendering uses Direct3D 11.
The application package graph is recorded in `packages.lock.json`.

## Build

From the repository root in PowerShell:

```powershell
dotnet restore .\PixelWallpaper.sln --locked-mode
dotnet build .\PixelWallpaper.sln -c Release --no-restore
```

The background PNG and compiled shader bytecode are included in the repository.
No shader regeneration or extra asset download is required for a normal build.

## Run as wallpaper

```powershell
& .\src\PixelWallpaper.Renderer\bin\Release\net8.0-windows\PixelWallpaper.Renderer.exe
```

The application attaches to the primary desktop behind its icons. There is no tray
icon or ordinary taskbar button, and only one instance can run at a time.

In Visual Studio, open `PixelWallpaper.sln`, select `PixelWallpaper.Renderer` as the
startup project, choose **Release**, and press **Ctrl+F5**. Leave command-line
arguments empty for desktop mode. A debugger can affect performance measurements.

## Windowed preview

```powershell
& .\src\PixelWallpaper.Renderer\bin\Release\net8.0-windows\PixelWallpaper.Renderer.exe --windowed
```

Preview starts with a 960×540 client area. Resizing favors integer scaling and
centers the scene with dark borders. Minimize pauses rendering; restore resumes
without advancing particles through the paused time. Close the preview or press
Escape to exit. In Visual Studio, set the debug command-line argument to
`--windowed` to use this mode.

## Stop the wallpaper

```powershell
& .\src\PixelWallpaper.Renderer\bin\Release\net8.0-windows\PixelWallpaper.Renderer.exe --stop
```

**Ctrl+Alt+Shift+Q** also stops desktop mode if the hotkey was available at startup.
Use `--stop` if another application owns that shortcut. Shutdown releases owned
Direct3D and Win32 resources and reveals the saved Windows wallpaper.

## Measured performance

Short Release-mode samples at 1920×1080 on a GTX 1650:

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

## Background image

The bundled forest is a temporary, generated placeholder. To replace it, stop the
app and replace `src/PixelWallpaper.Renderer/Assets/Backgrounds/rainy-forest.png`
with an **opaque, exactly 480×270 PNG**, keeping its name. Rebuild and restart.
The image is loaded once; there is no live reload. The build copies it beside the
executable under `Assets/Backgrounds/`.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/PixelWallpaper.Renderer/` | The single shipping executable. |
| `Rendering/` within the project | Direct3D device, textures, shaders, and drawing. |
| `Scenes/` within the project | `RainyForestScene`, static background, and rain particle layers. |
| `Windows/` within the project | Win32 window, desktop host, fullscreen checks, and interop. |
| `Runtime/` and `Timing/` within the project | Pause reasons, notifications, and frame pacing. |
| `tests/PixelWallpaper.Verification/` | Separate, non-shipping regression helper. |
| `tools/` | Development-only profiling, shader compilation, and placeholder generation. |

See [architecture notes](docs/ARCHITECTURE.md) for implementation details and
[PROJECT_HANDOFF.md](PROJECT_HANDOFF.md) for the original design intent and roadmap.

## Verification helper

Stop the wallpaper and other verification helpers before running:

```powershell
dotnet run --project .\tests\PixelWallpaper.Verification -c Release
```

**This intentionally opens a blank white test window**, then maximizes it, makes
it fullscreen, restores it, and closes it to check frame suspension. Keep the
fixture in the foreground until it finishes. The helper also checks notification
handling, resume timing, allocations, and native registration cleanup. Synthetic
power/session tests do not lock or suspend Windows.

For callback allocation/cleanup checks without a visible fixture:

```powershell
dotnet run --project .\tests\PixelWallpaper.Verification -c Release -- --notifications
```

The helper is outside the shipping solution and is never launched by the app.
After editing HLSL, run `tools/Compile-Shaders.ps1` and rebuild; commit the matching
`.cso` files. `tools/Generate-Placeholder.ps1` overwrites the bundled forest image
when explicitly invoked, so avoid running it over replacement artwork.

## Current limitations

- One scene and one primary-monitor wallpaper; no multi-monitor support.
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
- No WPF settings, tray icon, startup integration, additional scenes, wallpaper
  editor, online features, browser rendering, or video playback.
