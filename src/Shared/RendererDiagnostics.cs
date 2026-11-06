using System.Diagnostics;
using System.IO;

namespace PixelWallpaper.Shared;

// One bounded, local error file, written only on failure. No retained stream or parent pipe.
internal static class RendererDiagnostics
{
    public static string ErrorPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "PixelWallpaper", "renderer-error.txt");

    public static void Write(Exception error)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            string detail = error.ToString();
            if (detail.Length > 8000) detail = detail[..8000];
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorPath)!);
            File.WriteAllText(ErrorPath, $"{process.Id} {process.StartTime.ToUniversalTime().Ticks}\n{detail}");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }

    public static string? Read(int processId, long startTime)
    {
        try
        {
            using var file = File.OpenRead(ErrorPath);
            if (file.Length > 40000) return null;
            using var reader = new StreamReader(file);
            return reader.ReadLine() == $"{processId} {startTime}" ? reader.ReadToEnd() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
