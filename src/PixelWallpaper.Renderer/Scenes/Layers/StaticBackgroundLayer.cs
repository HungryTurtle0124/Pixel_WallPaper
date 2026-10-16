using PixelWallpaper.Renderer.Rendering;

namespace PixelWallpaper.Renderer.Scenes.Layers;

internal sealed class StaticBackgroundLayer : IDisposable
{
    private readonly TextureResource _texture;

    public StaticBackgroundLayer(D3D11Renderer renderer, string imagePath)
    {
        // Decode and upload exactly once. No per-frame I/O, decoding, or CPU pixel copy.
        _texture = renderer.LoadBackgroundTexture(imagePath);
    }

    public void Render(D3D11Renderer renderer) => renderer.DrawBackground(_texture);

    public void Dispose() => _texture.Dispose();
}
