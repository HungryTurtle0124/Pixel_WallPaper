using PixelWallpaper.Renderer.Rendering;

namespace PixelWallpaper.Renderer.Scenes.Layers;

internal sealed class LightweightFireGlowEffect : IDisposable
{
    private readonly TextureResource _texture;
    private readonly int _x, _y;
    private float _time;
    private byte _opacity = 185;

    public LightweightFireGlowEffect(D3D11Renderer renderer, string imagePath, int x, int y)
    {
        _x = x; _y = y;
        _texture = renderer.LoadSpriteTexture(imagePath, 64, 48);
    }

    public void Update(float deltaSeconds)
    {
        // A shared 20*pi period keeps the accumulator bounded during long sessions.
        _time = (_time + deltaSeconds) % (20 * MathF.PI);
        _opacity = (byte)(185 + 12 * MathF.Sin(_time * 2.3f) + 6 * MathF.Sin(_time * 5.1f));
    }
    public void Render(D3D11Renderer renderer) => renderer.DrawSprite(_texture, _x, _y, 64, 48, opacity: _opacity);
    public void Dispose() => _texture.Dispose();
}
