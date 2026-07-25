using System.IO;

namespace Commander;

public static class AppPaths
{
    public static string LocalRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIReceptionist");

    public static string ModelsRoot { get; } = Path.Combine(LocalRoot, "models");
    public static string DiagnosticsRoot { get; } = Path.Combine(LocalRoot, "diagnostics");
    public static string RuntimeRoot { get; } = Path.Combine(LocalRoot, "runtime");
    public static string LogPath { get; } = Path.Combine(LocalRoot, "app.log");
}
