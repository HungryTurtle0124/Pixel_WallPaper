using PixelWallpaper.Renderer.Rendering;

namespace PixelWallpaper.Renderer.Scenes.Layers;

// A single horizontal strip, one immutable texture, and an independent animation clock.
internal sealed class SpriteAnimationLayer : IDisposable
{
    private readonly TextureResource _texture;
    private readonly int _x, _y, _frameWidth, _frameHeight, _frameCount;
    private SpriteAnimationClock _clock;
    public int CurrentFrame => _clock.Frame;

    public SpriteAnimationLayer(D3D11Renderer renderer, string imagePath, int frameWidth, int frameHeight,
        int frameCount, double animationFps, int x, int y)
    {
        _clock = new(frameCount, animationFps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
        _x = x; _y = y; _frameWidth = frameWidth; _frameHeight = frameHeight; _frameCount = frameCount;
        _texture = renderer.LoadSpriteTexture(imagePath, checked(frameWidth * frameCount), frameHeight);
    }

    public void Update(float deltaSeconds) => _clock.Update(deltaSeconds);
    public void Render(D3D11Renderer renderer) => renderer.DrawSprite(_texture, _x, _y, _frameWidth, _frameHeight,
        CurrentFrame / (float)_frameCount, (CurrentFrame + 1) / (float)_frameCount);
    public void Dispose() => _texture.Dispose();
}
