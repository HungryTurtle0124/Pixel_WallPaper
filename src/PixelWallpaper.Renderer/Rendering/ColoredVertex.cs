using System.Numerics;
using System.Runtime.InteropServices;

namespace PixelWallpaper.Renderer.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ColoredVertex(Vector2 position, uint color)
{
    public readonly Vector2 Position = position;
    public readonly uint Color = color;
}
