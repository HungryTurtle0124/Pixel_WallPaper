using System.Numerics;
using PixelWallpaper.Renderer.Rendering;

namespace PixelWallpaper.Renderer.Scenes.Layers;

internal sealed class RainParticleLayer
{
    private readonly int _width;
    private readonly int _height;

    private struct Drop
    {
        public int X;
        public float Y;
        public float Speed;
        public int Length;
        public uint Color;
    }

    private readonly Drop[] _drops;
    private readonly ColoredVertex[] _vertices;
    private readonly Random _random;

    public RainParticleLayer(int width, int height, int dropCount = 96, int seed = 12345)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dropCount);
        _width = width;
        _height = height;
        _drops = new Drop[dropCount];
        _vertices = new ColoredVertex[checked(dropCount * 6)];
        _random = new Random(seed);
        for (int i = 0; i < _drops.Length; i++)
        {
            byte shade = (byte)_random.Next(60, 135);
            _drops[i] = new Drop
            {
                X = _random.Next(_width),
                Y = _random.Next(-8, _height),
                Speed = _random.Next(65, 145),
                Length = _random.Next(3, 8),
                Color = PackColor((byte)(shade / 2), (byte)(shade * 3 / 4), shade)
            };
        }
        Update(0);
    }

    public void Update(float deltaSeconds)
    {
        for (int i = 0; i < _drops.Length; i++)
        {
            ref Drop drop = ref _drops[i];
            drop.Y += drop.Speed * deltaSeconds;
            if (drop.Y >= _height)
            {
                drop.Y = -drop.Length;
                drop.X = _random.Next(_width);
            }

            // Integer pixel boundaries preserve crisp one-pixel rain at the internal resolution.
            int y = (int)MathF.Floor(drop.Y);
            var topLeft = ToClipSpace(drop.X, y);
            var topRight = ToClipSpace(drop.X + 1, y);
            var bottomLeft = ToClipSpace(drop.X, y + drop.Length);
            var bottomRight = ToClipSpace(drop.X + 1, y + drop.Length);
            int vertex = i * 6;
            _vertices[vertex] = new(topLeft, drop.Color);
            _vertices[vertex + 1] = new(topRight, drop.Color);
            _vertices[vertex + 2] = new(bottomLeft, drop.Color);
            _vertices[vertex + 3] = new(bottomLeft, drop.Color);
            _vertices[vertex + 4] = new(topRight, drop.Color);
            _vertices[vertex + 5] = new(bottomRight, drop.Color);
        }
    }

    public void Render(D3D11Renderer renderer) => renderer.DrawParticles(_vertices);

    private Vector2 ToClipSpace(int x, int y) => new(x * (2.0f / _width) - 1, 1 - y * (2.0f / _height));

    private static uint PackColor(byte red, byte green, byte blue) =>
        (uint)(red | (green << 8) | (blue << 16)) | 0xFF000000;
}
