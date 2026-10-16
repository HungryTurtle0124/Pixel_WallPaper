using PixelWallpaper.Renderer.Rendering;

namespace PixelWallpaper.Renderer.Scenes;

internal interface IScene : IDisposable
{
    void Update(float deltaSeconds);
    void Render(D3D11Renderer renderer);
}
