using System.Globalization;
using PixelWallpaper.Shared;

namespace PixelWallpaper.Renderer.Control;

internal sealed record RendererArguments(bool Windowed, WallpaperSettings Settings)
{
    public static RendererArguments Parse(string[] args)
    {
        bool windowed = false, fullscreen = true;
        int fps = 30;
        string wallpaper = WallpaperSettings.RainyForest;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--windowed": windowed = true; break;
                case "--no-fullscreen-pause": fullscreen = false; break;
                case "--fps" when ++index < args.Length:
                    fps = int.Parse(args[index], CultureInfo.InvariantCulture); break;
                case "--scene" when ++index < args.Length:
                    wallpaper = args[index]; break;
                case "--background": break; // Error-reporting mode, handled by Program's outer catch.
                default: throw new ArgumentException("Options: --windowed, --stop, --scene rainy-forest|campfire|maple-tree, --fps 15|30|60, --no-fullscreen-pause.");
            }
        }
        var settings = new WallpaperSettings { Wallpaper = wallpaper, PerformanceMode = (PerformanceMode)fps, PauseWhileFullscreen = fullscreen };
        settings.Validate();
        return new(windowed, settings);
    }
}
