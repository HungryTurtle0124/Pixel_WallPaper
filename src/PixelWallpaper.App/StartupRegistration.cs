using System.IO;
using Microsoft.Win32;

namespace PixelWallpaper.App;

internal sealed class StartupRegistration(string executable, string keyPath = StartupRegistration.RunKey)
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "PixelWallpaper";

    public bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new FileNotFoundException("Build the renderer before enabling startup.", executable);
        string command = $"\"{executable}\" --startup";
        if (command.Length > 260)
            throw new IOException("Move the application to a shorter folder path before enabling startup.");
        using var writable = Registry.CurrentUser.CreateSubKey(keyPath);
        writable.SetValue(ValueName, command, RegistryValueKind.String);
    }
}
