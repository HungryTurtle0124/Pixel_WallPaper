using PixelWallpaper.Renderer.Rendering;
using PixelWallpaper.Shared;

namespace PixelWallpaper.Renderer.Scenes;

internal static class SceneFactory
{
    public static IScene Create(D3D11Renderer renderer, string scene) => scene switch
    {
        WallpaperSettings.RainyForest => new RainyForestScene(renderer,
            Path.Combine(AppContext.BaseDirectory, "Assets", "Backgrounds", "rainy-forest.png")),
        WallpaperSettings.Campfire => new CampfireScene(renderer, Path.Combine(AppContext.BaseDirectory, "Assets")),
        _ => throw new ArgumentException("Unsupported scene.", nameof(scene))
    };
}
