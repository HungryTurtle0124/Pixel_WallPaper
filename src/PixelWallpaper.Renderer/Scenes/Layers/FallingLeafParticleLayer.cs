using PixelWallpaper.Renderer.Rendering;

namespace PixelWallpaper.Renderer.Scenes.Layers;

internal sealed class FallingLeafParticleLayer : IDisposable
{
    private struct Leaf
    {
        public bool Active;
        public float X, Y, Age, Lifetime, FallSpeed, Drift, Phase;
    }
    private readonly TextureResource _texture;
    private readonly Leaf[] _leaves = new Leaf[4];
    private readonly Random _random = new(7107);
    private float _untilSpawn = 1.5f;

    public FallingLeafParticleLayer(D3D11Renderer renderer, string imagePath) =>
        _texture = renderer.LoadSpriteTexture(imagePath, 16, 8);

    public void Update(float deltaSeconds)
    {
        for (int i = 0; i < _leaves.Length; i++)
        {
            ref Leaf leaf = ref _leaves[i];
            if (!leaf.Active) continue;
            leaf.Age += deltaSeconds;
            if (leaf.Age >= leaf.Lifetime) { leaf.Active = false; continue; }
            leaf.X += leaf.Drift * deltaSeconds;
            leaf.Y += leaf.FallSpeed * deltaSeconds;
        }
        _untilSpawn -= deltaSeconds;
        if (_untilSpawn > 0) return;
        // One attempt per interval, with no catch-up burst if a pool slot is unavailable.
        _untilSpawn = 3.5f + _random.NextSingle() * 2;
        for (int i = 0; i < _leaves.Length; i++)
        {
            if (_leaves[i].Active) continue;
            _leaves[i] = new Leaf
            {
                Active = true, X = _random.Next(183, 318), Y = _random.Next(89, 128),
                Lifetime = 10 + _random.NextSingle() * 3,
                FallSpeed = 7 + _random.NextSingle() * 3, Drift = 1 + _random.NextSingle() * 3,
                Phase = _random.NextSingle() * 2 * MathF.PI
            };
            break;
        }
    }

    public void Render(D3D11Renderer renderer)
    {
        for (int i = 0; i < _leaves.Length; i++)
        {
            ref Leaf leaf = ref _leaves[i];
            if (!leaf.Active) continue;
            int x = (int)MathF.Floor(leaf.X + MathF.Sin(leaf.Age * 1.4f + leaf.Phase) * 3);
            int y = (int)MathF.Floor(leaf.Y);
            int frame = ((int)(leaf.Age * 2) + i) & 1; // Two silhouettes; no rotation pipeline.
            byte opacity = (byte)(230 * MathF.Min(1, leaf.Age) * MathF.Min(1, (leaf.Lifetime - leaf.Age) / 2));
            renderer.DrawSprite(_texture, x, y, 8, 8, frame * .5f, (frame + 1) * .5f, opacity);
        }
    }
    public void Dispose() => _texture.Dispose();
}
