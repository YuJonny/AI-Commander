using System.IO;
using System.Text.Json;

namespace Commander;

public sealed class Settings
{
    private const int CurrentSchemaVersion = 2;
    private static readonly object Sync = new();
    private static Settings? _instance;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Commander", "settings.json");

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool IsEnabled { get; set; }
    public InputMethodKind SelectedInputMethod { get; set; } = InputMethodKind.LocalVoice;
    public AsrModelKind SelectedAsrModel { get; set; } = AsrModelKind.Qwen;
    public bool CleanFillers { get; set; } = true;
    public bool UseLlm { get; set; }
    public bool LaunchAtLogin { get; set; }
    public string Language { get; set; } = "auto";
    public bool FirstRunCompleted { get; set; }
    public bool PipelineDiagnosticsEnabled { get; set; }

    // 本机润色性能探测结果（LlmPerformanceProbe，0=未测）。
    // Windows 用 CPU 推理，各机器差一个数量级，超时预算必须按实测给。
    public int LlmPerformanceTier { get; set; }
    public double LlmPromptTokensPerSecond { get; set; }
    public double LlmGenerationTokensPerSecond { get; set; }
    public double LlmEstimatedPolishSeconds { get; set; }

    // Kept for backward compatibility with the v1 settings file.
    public int TargetKeyCode { get; set; } = VirtualKeys.RightAlt;
    public string TargetKeyName { get; set; } = "Right Alt";
    public KeyModifiers TargetModifiers { get; set; } = KeyModifiers.None;
    public ShortcutTriggerMode TargetTriggerMode { get; set; } = ShortcutTriggerMode.Hold;

    public static Settings Instance
    {
        get
        {
            lock (Sync)
            {
                return _instance ??= Load();
            }
        }
    }

    public KeyStroke CustomShortcut
    {
        get => new(TargetKeyCode, TargetModifiers, TargetTriggerMode, TargetKeyName);
        set
        {
            TargetKeyCode = value.KeyCode;
            TargetModifiers = value.Modifiers;
            TargetTriggerMode = value.TriggerMode;
            TargetKeyName = value.DisplayName;
        }
    }

    private static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Settings();

            var json = File.ReadAllText(FilePath);
            using var document = JsonDocument.Parse(json);
            var value = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();

            // v1 only knew a custom single-key mapping. Preserve that behavior on upgrade.
            if (!document.RootElement.TryGetProperty(nameof(SchemaVersion), out _))
            {
                value.SelectedInputMethod = InputMethodKind.Custom;
                value.TargetModifiers = KeyModifiers.None;
                value.TargetTriggerMode = ShortcutTriggerMode.Hold;
            }

            value.SchemaVersion = CurrentSchemaVersion;
            return value;
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        lock (Sync)
        {
            try
            {
                SchemaVersion = CurrentSchemaVersion;
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
                var temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, FilePath, overwrite: true);
            }
            catch
            {
                // Settings are non-critical. Runtime state remains valid if persistence fails.
            }
        }
    }
}
