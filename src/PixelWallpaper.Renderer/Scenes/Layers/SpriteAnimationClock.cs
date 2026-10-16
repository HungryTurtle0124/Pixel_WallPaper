namespace PixelWallpaper.Renderer.Scenes.Layers;

// Scene time only: paused scenes receive no updates, so no wall-clock catch-up on resume.
internal struct SpriteAnimationClock
{
    private readonly int _frameCount;
    private readonly double _framesPerSecond;
    private double _fractionalFrames;
    public int Frame { get; private set; }

    public SpriteAnimationClock(int frameCount, double framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        _frameCount = frameCount; _framesPerSecond = framesPerSecond;
    }

    public void Update(float deltaSeconds)
    {
        _fractionalFrames += deltaSeconds * _framesPerSecond;
        int advance = (int)Math.Floor(_fractionalFrames);
        if (advance == 0) return;
        _fractionalFrames -= advance;
        Frame = (Frame + advance) % _frameCount;
    }
}
