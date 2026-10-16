namespace PixelWallpaper.Shared;

internal enum PerformanceMode { Eco = 15, Balanced = 30, Smooth = 60 }

internal sealed record WallpaperSettings
{
    public const string RainyForest = "rainy-forest";
    public const string Campfire = "campfire";
    public string Wallpaper { get; init; } = RainyForest;
    public PerformanceMode PerformanceMode { get; init; } = PerformanceMode.Balanced;
    public bool PauseWhileFullscreen { get; init; } = true;

    public void Validate()
    {
        if (Wallpaper is not (RainyForest or Campfire) || !Enum.IsDefined(PerformanceMode))
            throw new ArgumentException("Unsupported wallpaper or performance mode.");
    }
}
