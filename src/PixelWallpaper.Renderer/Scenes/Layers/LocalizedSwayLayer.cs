using PixelWallpaper.Renderer.Rendering;

namespace PixelWallpaper.Renderer.Scenes.Layers;

internal readonly record struct SwayPlacement(int X, int Y, float Phase);

// One immutable texture reused at a few positions. Tree clusters and grass share this layer.
internal sealed class LocalizedSwayLayer : IDisposable
{
    private readonly TextureResource _texture;
    private readonly SwayPlacement[] _placements;
    private readonly int[] _offsets;
    private readonly int _width, _height, _amplitude;
    private SpriteAnimationClock _clock = new(64, 8); // 8 ticks/sec; one gentle 8-second cycle.

    public LocalizedSwayLayer(D3D11Renderer renderer, string imagePath, int width, int height,
        ReadOnlySpan<SwayPlacement> placements, int amplitude = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (placements.IsEmpty || amplitude is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(placements));
        _width = width; _height = height; _amplitude = amplitude;
        _placements = placements.ToArray();
        _offsets = new int[placements.Length];
        UpdateOffsets();
        _texture = renderer.LoadSpriteTexture(imagePath, width, height);
    }

    public void Update(float deltaSeconds)
    {
        int previous = _clock.Frame;
        _clock.Update(deltaSeconds);
        if (_clock.Frame != previous) UpdateOffsets();
    }

    private void UpdateOffsets()
    {
        for (int i = 0; i < _placements.Length; i++)
            _offsets[i] = (int)MathF.Round(MathF.Sin(_clock.Frame * (2 * MathF.PI / 64) + _placements[i].Phase) * _amplitude);
    }

    public void Render(D3D11Renderer renderer)
    {
        for (int i = 0; i < _placements.Length; i++)
            renderer.DrawSprite(_texture, _placements[i].X + _offsets[i], _placements[i].Y, _width, _height);
    }
    public void Dispose() => _texture.Dispose();
}
