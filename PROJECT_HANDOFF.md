# Project: Lightweight Retro Pixel Wallpaper Engine for Windows

## Document status

This document preserves the original design goals and future roadmap. It is not
a request to restart implementation or build all planned features. **Milestones
1–10 are implemented.** See [README.md](README.md) for the
current build/run instructions, measured results, and limitations, and
[architecture notes](docs/ARCHITECTURE.md) for implementation details.

The current implementation uses a WPF controller and separate C# renderer targeting
`net8.0-windows`, with Vortice/Direct3D 11, three 480×270 scenes, Win32 desktop
hosting, performance-aware suspension, and 15/30/60 FPS modes. Closing the WPF
controller leaves the independent renderer running; reopening reconnects to live
state and explicit Stop shuts it down. The broader project
split, additional scenes, and interfaces proposed below are future options rather
than requirements for the existing prototype. The repository's SDK selection is in
`global.json`; its SDK version is independent of the .NET 8 runtime target.

I want to build a lightweight Windows wallpaper engine focused specifically on retro/pixel-art procedural wallpapers.

This is NOT a video wallpaper player. Do not build the project around MP4 playback, WebView2, Chromium, or continuously playing video.

The main design goal is:

**Render only the small parts of a wallpaper that actually need to move, while keeping CPU, GPU, and RAM usage extremely low.**

The target development/test machine is relatively limited:

- Intel Core i5, 9th generation
- NVIDIA GTX 1650-class GPU
- 8 GB RAM
- Windows 10/11

The application should remain lightweight enough that Chrome, VS Code, development tools, and other normal applications can run at the same time without noticeable slowdown.

---

# Product concept

The application is a lightweight procedural wallpaper engine for retro/pixel-art scenes.

Initial wallpaper concepts:

1. Rainy Forest
2. Campfire
3. Giant Maple Tree on a Hill

These should NOT be rendered as looping videos.

Instead, a wallpaper should be composed from reusable scene components such as:

- static backgrounds
- particle systems
- sprite animations
- simple GPU effects
- slow environmental animations

Example:

Rainy Forest:

```text
Static pixel-art forest background
+
rain particle layer
+
optional slow fog layer
```

Campfire:

```text
Static campsite background
+
small looping fire sprite
+
ember particles
+
subtle light flicker
```

Maple Tree:

```text
Static hill/tree environment
+
small leaf/grass movement
+
occasional falling leaves
+
optional slow clouds
```

---

# Primary technical goals

Use:

- C#
- .NET
- WPF for the configuration/settings application
- Win32 APIs for Windows desktop integration
- Direct3D 11 for the actual wallpaper rendering

Avoid heavy frameworks when unnecessary.

Do NOT use WebView2 as the main renderer.

Do NOT use Electron.

Do NOT use a browser engine.

Do NOT use video decoding for the primary wallpaper format.

The application should eventually support multiple renderer components through a reusable scene architecture.

---

# Rendering philosophy

The engine should follow this rule:

**Animate as little as possible, render as little as possible, and sleep whenever nothing needs updating.**

Important performance principles:

- static objects should remain static
- do not recompute things that have not changed
- avoid unnecessary per-frame allocations
- reuse GPU resources
- avoid busy loops
- avoid high-frequency polling
- use event-driven Windows behavior where practical
- cap FPS
- completely stop rendering when appropriate

The application should eventually support performance modes such as:

```text
Eco
15 FPS

Balanced
30 FPS

Smooth
60 FPS
```

The default target should be 30 FPS.

For many pixel-art scenes, 15–30 FPS should be perfectly acceptable.

---

# Low-resolution rendering

The engine should preferably render scenes internally at a deliberately low resolution, such as:

```text
320x180
```

or:

```text
480x270
```

Then upscale to the monitor resolution using nearest-neighbor / point sampling.

Example:

```text
480x270
    ↓
nearest-neighbor 4x upscale
    ↓
1920x1080
```

This is desirable for two reasons:

1. much lower rendering workload
2. preserves the intentional pixel-art appearance

Do not blur pixel graphics during scaling.

---

# Future architecture options

Keep the code modular.

Possible future structure; the current controller and renderer keep further concerns
in folders until splitting more projects is justified:

```text
PixelWallpaper/
│
├── PixelWallpaper.App/
│   ├── WPF settings/configuration UI
│   ├── MainWindow
│   ├── ViewModels
│   └── application startup
│
├── PixelWallpaper.Core/
│   ├── Scene
│   ├── SceneManager
│   ├── timing
│   ├── configuration
│   └── wallpaper state
│
├── PixelWallpaper.Rendering/
│   ├── Direct3D renderer
│   ├── StaticLayer
│   ├── SpriteLayer
│   ├── ParticleLayer
│   └── EffectLayer
│
└── PixelWallpaper.Windows/
    ├── DesktopHost
    ├── Win32 interop
    ├── fullscreen detection
    ├── monitor handling
    └── power/session state
```

Exact project names can change if a cleaner architecture makes sense.

---

# Scene architecture

A wallpaper should eventually be represented as a scene.

Conceptually:

```text
Scene
├── BackgroundLayer
├── SpriteLayer(s)
├── ParticleLayer(s)
└── EffectLayer(s)
```

Possible abstraction:

```csharp
public interface ISceneLayer
{
    void Update(float deltaTime);
    void Render(/* rendering context */);
}
```

However, do not blindly implement this exact interface if a better Direct3D-oriented design makes more sense.

The architecture should allow us to add new wallpaper scenes later without rewriting the rendering engine.

---

# Layer types

## StaticLayer

Used for:

- forest backgrounds
- hills
- sky
- campsite
- mountains
- buildings

The texture should be loaded once and reused.

No unnecessary updates.

---

## ParticleLayer

Used for:

- rain
- snow
- falling leaves
- embers
- stars
- dust

A particle could contain approximately:

```text
position
velocity
size
opacity
lifetime
```

Keep particle representation compact.

Avoid allocating/destroying hundreds of C# objects every frame.

Prefer reusable buffers/pools or struct-based data where appropriate.

---

## SpriteLayer

Used for small animated elements such as:

- fire
- waterfall
- flag
- bird
- moving leaves
- character
- glowing lantern

Sprite animations may have independent animation rates.

Example:

```text
Wallpaper update: 30 FPS
Fire animation: 8 FPS
```

Do not force everything to update at the same frequency unnecessarily.

---

## EffectLayer

Eventually used for lightweight GPU effects such as:

- fog
- tint
- light flicker
- slow water distortion
- day/night transition
- simple shader effects

Do not implement complicated shader systems initially.

---

# Performance manager

This is a major part of the project.

The wallpaper should change behavior depending on what the user is doing.

Desired states:

```text
DESKTOP_VISIBLE
    ↓
render normally

MOSTLY_COVERED
    ↓
optionally reduce FPS

FULLSCREEN_APPLICATION
    ↓
stop rendering
    ↓
pause updates

WINDOWS_LOCKED
    ↓
stop rendering

SLEEP / DISPLAY OFF
    ↓
stop rendering
```

Eventually:

```text
Desktop visible:
30 FPS

Large/maximized application:
15 FPS optional

Fullscreen game/video:
0 FPS

PC locked:
0 FPS
```

When suspended, CPU usage should approach zero.

Avoid loops such as:

```csharp
while (true)
{
    CheckSomething();
}
```

without blocking/sleeping.

---

# Memory goals

This project is specifically intended for a machine with only 8 GB RAM.

Optimize for:

- low steady-state memory usage
- no increasing memory consumption over time
- correct disposal of GPU resources
- correct disposal of COM/native resources
- no accumulating frame queues
- no runaway particle allocations
- no resource leaks

Do not prematurely micro-optimize everything, but design the architecture so that obvious waste is avoided.

---

# Windows desktop integration

The rendered scene ultimately needs to appear as the user's Windows wallpaper, behind desktop icons.

Use the appropriate Win32 desktop/window APIs.

Likely relevant concepts include:

- HWND
- Progman
- WorkerW
- EnumWindows
- SetParent
- FindWindow / FindWindowEx
- monitor enumeration
- desktop window hierarchy

Do not assume hacks copied from old tutorials are automatically correct.

Keep desktop integration isolated inside the Windows-specific layer.

The renderer should not need to know how WorkerW/Progman work.

---

# FIRST DEVELOPMENT TARGET

Do NOT immediately implement all three wallpapers.

Do NOT build a full settings UI first.

Do NOT build multi-monitor support first.

Do NOT build a plugin system first.

Start with the smallest vertical slice.

## Milestone 1 — complete

Create a Windows application that:

1. initializes the necessary Direct3D 11 resources
2. creates a rendering window
3. renders a very simple pixel-art-style scene
4. runs at a capped frame rate
5. can cleanly start and stop

At first, rendering into a normal application window is acceptable.

Use temporary generated/simple graphics if art assets are not available.

The first scene can simply be:

```text
dark background
+
simple falling rain particles
```

The point is to prove that the lightweight rendering loop works.

---

# Milestone 2 — complete

Create a static background texture and rain particle layer.

Target scene:

**Rainy Forest prototype**

If no forest art exists yet, use a placeholder texture or simple generated background.

The renderer should support:

```text
Static background
+
rain particles
```

Implement:

- fixed/capped FPS
- delta-time-based movement
- particle reuse
- clean resource lifecycle
- nearest-neighbor scaling

---

# Milestone 3 — complete

Embed the renderer behind Windows desktop icons.

The wallpaper should behave like an actual desktop wallpaper rather than a normal application window.

Keep desktop-host code separate from the renderer.

---

# Milestone 4 — complete

Add performance-aware pausing.

Detect at minimum:

- fullscreen foreground application
- session lock/unlock if practical

When fullscreen is detected:

```text
particle updates stop
rendering stops
renderer sleeps
```

When the desktop becomes visible again:

```text
rendering resumes
```

Do not spend GPU cycles drawing a wallpaper nobody can see.

---

# Milestone 5 — complete

Implemented: separate WPF Start/Stop/settings controller, one scene choice,
Eco/Balanced/Smooth modes, optional fullscreen pausing, local JSON persistence,
and clean shutdown ownership. Milestone 8 supersedes its original close-to-stop behavior.
Tray integration and Start with Windows remain deferred.

Only after the rendering core is stable, add the WPF control application.

Initial settings should remain simple:

```text
Wallpaper:
[ Rainy Forest ]

Performance:
[ Eco      ]
[ Balanced ]
[ Smooth   ]

Start with Windows:
[ ]

Pause while fullscreen:
[x]
```

Do not spend excessive time making the UI visually fancy yet.

---

# Milestone 6 — complete: Campfire and multi-scene selection

Implemented: the existing scene abstraction supports Campfire alongside Rainy
Forest. Campfire reuses the static background layer and adds an 8 FPS sprite
strip, a fixed 12-ember pool and one faint flickering glow sprite. The controller
saves scene selection and cleanly restarts only the renderer on scene changes.
Performance modes, fullscreen preference and mandatory session/power pausing are
retained. Original placeholder art and its generator are included. No new dependency.

## Campfire — implemented

Uses:

- static background
- fire sprite animation
- ember particle system
- subtle light flicker

This verifies independent sprite timing and reused resources.

## Milestone 7 — complete: Giant Maple Tree on a Hill

Implemented: a static hill and maple tree with four small canopy accents and
three grass patches swaying at 8 Hz on a slow eight-second cycle, plus a fixed
four-leaf pool with occasional spawning. The shared animation clock and sprite
renderer are reused. Clouds stay static; no dependency or renderer redesign.
Scene selection persists using the existing JSON format and renderer restart.
Tray/startup/recovery and other product-level improvements remain deferred.

Uses:

- static background
- localized leaf movement
- occasional falling leaves
- grass movement
- optional slow clouds

This verifies slow independent animation and scene composition.

## Milestone 8 — complete: Independent Renderer and Controller Lifecycle

Closing/crashing the controller leaves the renderer running, without a parent
process lease or redirected controller pipes. Reopening reads authoritative live
scene/performance/fullscreen preferences and saves them to the existing JSON file.
Named mutexes enforce single instances and serialize explicit commands. Atomic
status/revision checks reject stale mutations; Stop/hotkey/--stop retain clean
shutdown. No extra service, always-running WPF process, or automatic restart.

## Milestone 9 — System Tray and Windows Startup

An optional notification area application shares the WPF controller's renderer
connection and serialized settings writes. Exiting either control leaves the
renderer running. The WPF startup checkbox registers a per-user direct renderer
launch. Sign-in startup restores the saved preferences, waits for Explorer with a
deadline, and uses the same single-instance and diagnostic paths.

## Milestone 10 — Portable Windows Release

The publish script creates a self-contained Windows x64 folder and ZIP with the
controller, renderer, and optional tray. The release runs with its bundled .NET
runtime. The three executables and scene assets use application-relative paths.
Per-user settings and the opt-in Windows startup mechanism retain their existing
locations and behavior.

---

# Important engineering principle

Do not hard-code the rendering engine specifically for Rainy Forest.

Rainy Forest is the first test scene.

The architecture should eventually allow something like:

```csharp
sceneManager.Load(new RainyForestScene());
```

and later:

```csharp
sceneManager.Load(new CampfireScene());
```

without rewriting the renderer.

---

# Scope we are explicitly NOT doing yet

Do not implement these unless I specifically ask:

- MP4 wallpaper playback
- GIF wallpaper playback
- WebView2
- HTML wallpapers
- JavaScript wallpapers
- audio playback
- music visualizers
- wallpaper marketplace
- online accounts
- cloud sync
- Steam integration
- multi-monitor support
- extensive settings
- plugin architecture
- wallpaper editor
- complicated shaders

Keep the first version extremely focused.

---

# Code quality expectations

I am building this partly as a portfolio project, so structure and readability matter.

Prefer:

- clear ownership of resources
- small focused classes
- meaningful names
- minimal coupling
- comments explaining unusual Win32/D3D behavior
- proper error handling
- clean disposal patterns

Avoid:

- enormous classes
- unnecessary abstractions
- giant framework-style architecture
- premature design patterns
- excessive dependencies

Use third-party libraries only when they provide meaningful value.

If a lightweight Direct3D 11 C# binding is required, explain the available choices before adding one.

Do not silently introduce a large dependency.

---

# Development style

Work incrementally.

For each milestone:

1. explain what we are implementing
2. explain important unfamiliar Windows/Direct3D concepts
3. implement the smallest working version
4. build/run it
5. fix errors
6. verify behavior
7. only then move forward

Do not generate the entire application in one enormous pass.

I want to understand the code, not just receive a completed repository.

When making architectural decisions, prioritize in this order:

1. low resource usage
2. correctness
3. maintainability
4. simplicity
5. visual features

---

# Initial performance target

These are engineering goals, not hard guarantees:

```text
1080p display
internal render resolution: 480x270
balanced mode: 30 FPS

While rendering:
low CPU usage
low/modest GPU usage
stable RAM consumption

While paused:
CPU approximately 0%
render loop inactive
```

Measure performance periodically rather than assuming it is lightweight.

---

# Original first task — historical

The following instructions describe the original starting task, which has been
completed. They are retained as context, not as the current implementation task.

Start by inspecting the repository if one already exists.

If it is empty, propose the minimum project/solution structure required for Milestone 1.

Before writing a large amount of code, explain:

1. how you recommend accessing Direct3D 11 from C#
2. what dependency, if any, you recommend
3. how the basic render loop will work
4. how we will cap FPS without busy-spinning
5. how resource cleanup will work

Then implement only the first basic Direct3D rendering milestone.

Do not proceed into desktop embedding, WPF settings, or advanced effects until the basic renderer works reliably.
