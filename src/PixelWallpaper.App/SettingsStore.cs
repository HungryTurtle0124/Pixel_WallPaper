using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelWallpaper.Shared;

namespace PixelWallpaper.App;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, Converters = { new JsonStringEnumConverter<PerformanceMode>() } };
    private readonly string _path;
    public SettingsStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelWallpaper", "settings.json");

    public WallpaperSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(_path)) return new();
            using var input = File.OpenRead(_path);
            if (input.Length > 16 * 1024) throw new InvalidDataException("The settings file is too large.");
            var settings = JsonSerializer.Deserialize<WallpaperSettings>(input, JsonOptions)
                ?? throw new InvalidDataException("The settings file is empty.");
            settings.Validate();
            return settings;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            warning = "Settings could not be loaded; defaults are shown. " + error.Message;
            return new();
        }
    }

    public void Save(WallpaperSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        string temporary = _path + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, settings, JsonOptions);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
