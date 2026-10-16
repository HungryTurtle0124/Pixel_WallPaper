using PixelWallpaper.Renderer.Rendering;
using PixelWallpaper.Renderer.Scenes.Layers;

namespace PixelWallpaper.Renderer.Scenes;

internal sealed class RainyForestScene : IScene
{
    private readonly StaticBackgroundLayer _background;
    private readonly RainParticleLayer _rain;

    public RainyForestScene(D3D11Renderer renderer, string backgroundPath)
    {
        // Allocate managed particle data first. The only native layer is constructed last,
        // and its loader cleans up partial initialization if image loading fails.
        _rain = new RainParticleLayer(D3D11Renderer.InternalWidth, D3D11Renderer.InternalHeight);
        _background = new StaticBackgroundLayer(renderer, backgroundPath);
    }

    public void Update(float deltaSeconds) => _rain.Update(deltaSeconds);

    public void Render(D3D11Renderer renderer)
    {
        _background.Render(renderer);
        _rain.Render(renderer);
    }

    public void Dispose() => _background.Dispose();
}
