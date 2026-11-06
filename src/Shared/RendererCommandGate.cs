using System.IO;

namespace PixelWallpaper.Shared;

// A transaction is synchronous on one worker thread: Mutex ownership must never cross await.
// No thread exists just to hold this gate; only explicit controller commands use it.
internal static class RendererCommandGate
{
    public static T Run<T>(Func<T> command)
    {
        using var gate = new Mutex(false, "Local\\PixelWallpaper.Commands.v1");
        bool acquired;
        try { acquired = gate.WaitOne(TimeSpan.FromSeconds(15)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Another wallpaper command is still running. Please try again.");
        try { return command(); }
        finally { gate.ReleaseMutex(); }
    }
}
