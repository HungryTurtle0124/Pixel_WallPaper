using System.Numerics;
using System.Runtime.InteropServices;

namespace PixelWallpaper.Renderer.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SpriteVertex(Vector2 position, Vector2 uv, uint color)
{
    public readonly Vector2 Position = position;
    public readonly Vector2 Uv = uv;
    public readonly uint Color = color;
}
