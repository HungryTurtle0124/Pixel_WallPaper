using System.Globalization;
using PixelWallpaper.Shared;

namespace PixelWallpaper.Renderer.Control;

internal sealed record RendererArguments(bool Windowed, WallpaperSettings Settings, int OwnerId, long OwnerStart)
{
    public static RendererArguments Parse(string[] args)
    {
        bool windowed = false, fullscreen = true;
        int fps = 30, owner = 0;
        long start = 0;
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
                case "--owner" when index + 2 < args.Length:
                    owner = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    start = long.Parse(args[++index], CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException("Options: --windowed, --stop, --scene rainy-forest|campfire, --fps 15|30|60, --no-fullscreen-pause.");
            }
        }
        if ((owner == 0) != (start == 0) || owner < 0 || start < 0)
            throw new ArgumentException("Invalid controller lifetime information.");
        var settings = new WallpaperSettings { Wallpaper = wallpaper, PerformanceMode = (PerformanceMode)fps, PauseWhileFullscreen = fullscreen };
        settings.Validate();
        return new(windowed, settings, owner, start);
    }
}
