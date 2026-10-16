using System.Numerics;
using PixelWallpaper.Renderer.Rendering;

namespace PixelWallpaper.Renderer.Scenes.Layers;

internal sealed class EmberParticleLayer
{
    private struct Ember
    {
        public float X, Y, Age, Lifetime, Rise, Drift;
    }
    private readonly Ember[] _embers;
    private readonly ColoredVertex[] _vertices;
    private readonly Random _random = new(6701);
    private readonly int _originX, _originY;

    public EmberParticleLayer(int originX, int originY, int count = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _originX = originX; _originY = originY;
        _embers = new Ember[count];
        _vertices = new ColoredVertex[checked(count * 6)];
        for (int i = 0; i < count; i++)
        {
            Recycle(ref _embers[i]);
            // Stagger the initial pool so startup has the same sparse distribution as steady state.
            _embers[i].Age = _embers[i].Lifetime * i / count;
            _embers[i].Y -= _embers[i].Age * _embers[i].Rise;
            _embers[i].X += _embers[i].Age * _embers[i].Drift;
        }
        Update(0);
    }

    private void Recycle(ref Ember ember)
    {
        ember = new Ember
        {
            X = _originX + _random.Next(-6, 7), Y = _originY + _random.Next(-3, 4),
            Lifetime = 1.8f + _random.NextSingle() * 1.5f,
            Rise = 15 + _random.NextSingle() * 12, Drift = (_random.NextSingle() - .5f) * 7
        };
    }

    public void Update(float deltaSeconds)
    {
        for (int i = 0; i < _embers.Length; i++)
        {
            ref Ember ember = ref _embers[i];
            ember.Age += deltaSeconds;
            if (ember.Age >= ember.Lifetime) Recycle(ref ember);
            ember.X += ember.Drift * deltaSeconds;
            ember.Y -= ember.Rise * deltaSeconds;
            int x = (int)MathF.Floor(ember.X + MathF.Sin(ember.Age * 3 + i) * 1.5f);
            int y = (int)MathF.Floor(ember.Y);
            byte alpha = (byte)(170 * MathF.Min(1, ember.Age * 5) * (1 - ember.Age / ember.Lifetime));
            uint color = 0x002C9DFFu | ((uint)alpha << 24);
            Vector2 tl = ToClip(x, y), br = ToClip(x + 1, y + (i % 3 == 0 ? 2 : 1));
            int v = i * 6;
            _vertices[v] = new(tl, color);
            _vertices[v + 1] = new(new(br.X, tl.Y), color);
            _vertices[v + 2] = new(new(tl.X, br.Y), color);
            _vertices[v + 3] = _vertices[v + 2];
            _vertices[v + 4] = _vertices[v + 1];
            _vertices[v + 5] = new(br, color);
        }
    }

    private static Vector2 ToClip(int x, int y) => new(
        x * (2f / D3D11Renderer.InternalWidth) - 1, 1 - y * (2f / D3D11Renderer.InternalHeight));
    public void Render(D3D11Renderer renderer) => renderer.DrawParticles(_vertices, alphaBlend: true);
}
