using System.IO;

namespace Commander;

public static class AppLog
{
    private static readonly object Sync = new();

    public static void Info(string source, string message) => Write("INFO", source, message, null);
    public static void Error(string source, Exception? exception, string? message = null) =>
        Write("ERROR", source, message ?? exception?.Message ?? "Unknown error", exception);

    private static void Write(string level, string source, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppPaths.LocalRoot);
                using var writer = new StreamWriter(AppPaths.LogPath, append: true);
                writer.WriteLine($"[{DateTimeOffset.Now:O}] {level} {source}: {message}");
                if (exception is not null) writer.WriteLine(exception);
            }
        }
        catch
        {
            // Logging must never take down the app.
        }
    }
}
