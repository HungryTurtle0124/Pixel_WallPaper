using PixelWallpaper.Renderer.Rendering;
using PixelWallpaper.Renderer.Scenes.Layers;

namespace PixelWallpaper.Renderer.Scenes;

internal sealed class MapleTreeScene : IScene
{
    private readonly StaticBackgroundLayer _background;
    private readonly LocalizedSwayLayer _canopy;
    private readonly LocalizedSwayLayer _grass;
    private readonly FallingLeafParticleLayer _leaves;

    public MapleTreeScene(D3D11Renderer renderer, string assetDirectory)
    {
        _background = new StaticBackgroundLayer(renderer, Path.Combine(assetDirectory, "Backgrounds", "maple-tree.png"));
        try
        {
            _canopy = new LocalizedSwayLayer(renderer, Path.Combine(assetDirectory, "Sprites", "maple-leaf-cluster.png"),
                32, 24, [new(177, 89, 0), new(214, 53, 1.6f), new(283, 65, 3.1f), new(307, 105, 4.8f)], amplitude: 2);
            try
            {
                _grass = new LocalizedSwayLayer(renderer, Path.Combine(assetDirectory, "Sprites", "maple-grass.png"),
                    32, 12, [new(169, 202, .8f), new(292, 205, 2.4f), new(352, 224, 4.2f)]);
                try
                {
                    _leaves = new FallingLeafParticleLayer(renderer, Path.Combine(assetDirectory, "Sprites", "maple-falling-leaf.png"));
                }
                catch { _grass.Dispose(); throw; }
            }
            catch { _canopy.Dispose(); throw; }
        }
        catch { _background.Dispose(); throw; }
    }

    public void Update(float deltaSeconds)
    {
        _canopy.Update(deltaSeconds);
        _grass.Update(deltaSeconds);
        _leaves.Update(deltaSeconds);
    }
    public void Render(D3D11Renderer renderer)
    {
        _background.Render(renderer);
        _canopy.Render(renderer);
        _grass.Render(renderer);
        _leaves.Render(renderer);
    }
    public void Dispose()
    {
        _leaves.Dispose();
        _grass.Dispose();
        _canopy.Dispose();
        _background.Dispose();
    }
}
