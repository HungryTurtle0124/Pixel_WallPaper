namespace PixelWallpaper.Renderer.Runtime;

// Reasons overlap: clearing one must never resume while another still applies.
[Flags]
internal enum PauseReason
{
    None = 0,
    Fullscreen = 1,
    Locked = 2,
    DisplayOff = 4,
    Suspended = 8,
    Disconnected = 16
}
