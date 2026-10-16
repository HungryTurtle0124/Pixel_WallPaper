using Vortice.Direct3D11;

namespace PixelWallpaper.Renderer.Rendering;

internal sealed class TextureResource(ID3D11Texture2D texture, ID3D11ShaderResourceView view) : IDisposable
{
    public ID3D11ShaderResourceView View { get; } = view;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        View.Dispose();
        texture.Dispose();
    }
}
