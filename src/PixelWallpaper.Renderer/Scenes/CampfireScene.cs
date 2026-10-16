using PixelWallpaper.Renderer.Rendering;
using PixelWallpaper.Renderer.Scenes.Layers;

namespace PixelWallpaper.Renderer.Scenes;

internal sealed class CampfireScene : IScene
{
    private readonly StaticBackgroundLayer _background;
    private readonly SpriteAnimationLayer _fire;
    private readonly EmberParticleLayer _embers;
    private readonly LightweightFireGlowEffect _glow;

    public CampfireScene(D3D11Renderer renderer, string assetDirectory)
    {
        _embers = new EmberParticleLayer(256, 196);
        _background = new StaticBackgroundLayer(renderer, Path.Combine(assetDirectory, "Backgrounds", "campfire.png"));
        try
        {
            _fire = new SpriteAnimationLayer(renderer, Path.Combine(assetDirectory, "Sprites", "campfire-fire.png"),
                frameWidth: 32, frameHeight: 40, frameCount: 8, animationFps: 8, x: 240, y: 174);
            try
            {
                _glow = new LightweightFireGlowEffect(renderer,
                    Path.Combine(assetDirectory, "Sprites", "campfire-glow.png"), 224, 174);
            }
            catch { _fire.Dispose(); throw; }
        }
        catch { _background.Dispose(); throw; }
    }

    public void Update(float deltaSeconds)
    {
        _fire.Update(deltaSeconds);
        _embers.Update(deltaSeconds);
        _glow.Update(deltaSeconds);
    }
    public void Render(D3D11Renderer renderer)
    {
        _background.Render(renderer);
        _glow.Render(renderer);
        _fire.Render(renderer);
        _embers.Render(renderer);
    }
    public void Dispose()
    {
        _glow.Dispose();
        _fire.Dispose();
        _background.Dispose();
    }
}
