namespace Commander;

public enum InputMethodKind
{
    LocalVoice,
    Custom,
    WindowsVoiceTyping,
    Typeless,
    ClaudeCode,
    Codex,
    WisprFlow,
    WeChatVoice,
}

public enum AsrModelKind
{
    Qwen,
    SenseVoice,
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8,
}

public enum ShortcutTriggerMode
{
    Hold,
    Tap,
}

public sealed record KeyStroke(
    int KeyCode,
    KeyModifiers Modifiers,
    ShortcutTriggerMode TriggerMode,
    string DisplayName);

public sealed record InputMethodPreset(
    InputMethodKind Kind,
    string DisplayName,
    string Description,
    KeyStroke? Shortcut)
{
    public bool IsLocalVoice => Kind == InputMethodKind.LocalVoice;
    public bool IsCustom => Kind == InputMethodKind.Custom;
}

public static class InputMethodCatalog
{
    public static IReadOnlyList<InputMethodPreset> All { get; } =
    [
        new(InputMethodKind.LocalVoice, "本地大模型", "全程本地识别并直接输入", null),
        new(InputMethodKind.Custom, "自定义", "映射到任意 Windows 快捷键", null),
        new(InputMethodKind.WindowsVoiceTyping, "Windows 语音输入", "Windows + H",
            new KeyStroke(VirtualKeys.H, KeyModifiers.Windows, ShortcutTriggerMode.Tap, "Win + H")),
        new(InputMethodKind.Typeless, "Typeless", "官方 Windows 默认快捷键",
            new KeyStroke(VirtualKeys.RightAlt, KeyModifiers.None, ShortcutTriggerMode.Tap, "Right Alt")),
        new(InputMethodKind.ClaudeCode, "Claude Code", "语音输入快捷键",
            new KeyStroke(VirtualKeys.CapsLock, KeyModifiers.None, ShortcutTriggerMode.Tap, "Caps Lock")),
        new(InputMethodKind.Codex, "Codex", "直接启动听写",
            new KeyStroke(VirtualKeys.D, KeyModifiers.Control | KeyModifiers.Shift, ShortcutTriggerMode.Tap,
                "Ctrl + Shift + D")),
        new(InputMethodKind.WisprFlow, "Wispr Flow", "Windows 免提听写",
            new KeyStroke(
                VirtualKeys.Space,
                KeyModifiers.Control | KeyModifiers.Windows,
                ShortcutTriggerMode.Tap,
                "Ctrl + Win + Space")),
        new(InputMethodKind.WeChatVoice, "微信语音输入", "微信 Windows 官方语音输入",
            new KeyStroke(
                VirtualKeys.LeftControl,
                KeyModifiers.Windows,
                ShortcutTriggerMode.Hold,
                "Ctrl + Win")),
    ];

    public static InputMethodPreset Get(InputMethodKind kind) =>
        All.FirstOrDefault(item => item.Kind == kind) ?? All[0];
}

public static class VirtualKeys
{
    public const int Backspace = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Alt = 0x12;
    public const int CapsLock = 0x14;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Delete = 0x2E;
    public const int D = 0x44;
    public const int H = 0x48;
    public const int LeftWindows = 0x5B;
    public const int RightWindows = 0x5C;
    public const int LeftShift = 0xA0;
    public const int RightShift = 0xA1;
    public const int LeftControl = 0xA2;
    public const int RightControl = 0xA3;
    public const int LeftAlt = 0xA4;
    public const int RightAlt = 0xA5;
    public const int MediaPlayPause = 0xB3;
}
