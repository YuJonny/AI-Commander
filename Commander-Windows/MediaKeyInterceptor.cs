using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Commander;

public enum MediaKeyActionMode
{
    Shortcut,
    LocalVoice,
}

public sealed class MediaKeyInterceptor : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventExtendedKey = 0x0001;

    private delegate nint LowLevelKeyboardProc(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hookHandle, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInput);

    private nint _hookHandle;
    private LowLevelKeyboardProc? _hookCallback;
    private bool _mediaKeyIsDown;
    private bool _shortcutIsDown;
    private KeyStroke _shortcut = new(
        VirtualKeys.RightAlt,
        KeyModifiers.None,
        ShortcutTriggerMode.Hold,
        "Right Alt");

    public MediaKeyActionMode Mode { get; set; } = MediaKeyActionMode.Shortcut;

    public KeyStroke Shortcut
    {
        get => _shortcut;
        set => _shortcut = value;
    }

    // Compatibility for the v1 settings page while the v2 page is being initialized.
    public int TargetKeyCode
    {
        get => _shortcut.KeyCode;
        set => _shortcut = new KeyStroke(value, KeyModifiers.None, ShortcutTriggerMode.Hold, GetKeyName(value));
    }

    public event Action? MediaKeyPressed;
    public event Action? MediaKeyDetected;
    public event Action<string>? OnStatusChange;

    public void Start()
    {
        Stop();
        _hookCallback = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hookHandle = SetWindowsHookEx(
            WhKeyboardLowLevel,
            _hookCallback,
            GetModuleHandle(module?.ModuleName),
            0);
        if (_hookHandle == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the media-key hook.");
        OnStatusChange?.Invoke(I18n.Get("status.started"));
    }

    public void Stop()
    {
        if (_shortcutIsDown)
        {
            try { SendShortcut(isDown: false); }
            catch { }
        }
        _mediaKeyIsDown = false;
        _shortcutIsDown = false;
        if (_hookHandle != 0)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }
        _hookCallback = null;
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var value = Marshal.PtrToStructure<KeyboardHookData>(data);
            if (value.VirtualKey == VirtualKeys.MediaPlayPause)
            {
                var keyDown = message == WmKeyDown || message == WmSysKeyDown;
                var keyUp = message == WmKeyUp || message == WmSysKeyUp;
                if (keyDown && !_mediaKeyIsDown)
                {
                    _mediaKeyIsDown = true;
                    OnStatusChange?.Invoke(I18n.Get("status.key.down"));
                    MediaKeyDetected?.Invoke();
                    if (Mode == MediaKeyActionMode.LocalVoice)
                    {
                        _ = Task.Run(() => MediaKeyPressed?.Invoke());
                    }
                    else if (_shortcut.TriggerMode == ShortcutTriggerMode.Tap)
                    {
                        SendShortcut(isDown: true);
                        SendShortcut(isDown: false);
                    }
                    else
                    {
                        SendShortcut(isDown: true);
                        _shortcutIsDown = true;
                    }
                }
                else if (keyUp)
                {
                    _mediaKeyIsDown = false;
                    OnStatusChange?.Invoke(I18n.Get("status.key.up"));
                    if (Mode == MediaKeyActionMode.Shortcut && _shortcutIsDown)
                    {
                        SendShortcut(isDown: false);
                        _shortcutIsDown = false;
                    }
                }
                return 1;
            }
        }
        return CallNextHookEx(_hookHandle, code, message, data);
    }

    private void SendShortcut(bool isDown)
    {
        var keyCodes = ModifierKeyCodes(_shortcut.Modifiers);
        IEnumerable<int> ordered;
        if (isDown)
            ordered = keyCodes.Append(_shortcut.KeyCode);
        else
            ordered = new[] { _shortcut.KeyCode }.Concat(keyCodes.Reverse());

        var inputs = ordered.Select(keyCode => KeyboardEvent(keyCode, isDown)).ToArray();
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked the mapped shortcut.");
    }

    private static IReadOnlyList<int> ModifierKeyCodes(KeyModifiers modifiers)
    {
        var keys = new List<int>(4);
        if (modifiers.HasFlag(KeyModifiers.Control)) keys.Add(VirtualKeys.LeftControl);
        if (modifiers.HasFlag(KeyModifiers.Shift)) keys.Add(VirtualKeys.LeftShift);
        if (modifiers.HasFlag(KeyModifiers.Alt)) keys.Add(VirtualKeys.LeftAlt);
        if (modifiers.HasFlag(KeyModifiers.Windows)) keys.Add(VirtualKeys.LeftWindows);
        return keys;
    }

    private static INPUT KeyboardEvent(int keyCode, bool isDown) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = (ushort)keyCode,
                Flags = (isDown ? 0u : KeyEventKeyUp) | (IsExtendedKey(keyCode) ? KeyEventExtendedKey : 0),
            },
        },
    };

    private static bool IsExtendedKey(int keyCode) => keyCode is
        VirtualKeys.RightControl or VirtualKeys.RightAlt or
        VirtualKeys.Left or VirtualKeys.Up or VirtualKeys.Right or VirtualKeys.Down or
        VirtualKeys.Delete or VirtualKeys.LeftWindows or VirtualKeys.RightWindows;

    public static string GetKeyName(int keyCode) => keyCode switch
    {
        VirtualKeys.LeftControl => "Left Ctrl",
        VirtualKeys.RightControl => "Right Ctrl",
        VirtualKeys.LeftShift => "Left Shift",
        VirtualKeys.RightShift => "Right Shift",
        VirtualKeys.LeftAlt => "Left Alt",
        VirtualKeys.RightAlt => "Right Alt",
        VirtualKeys.LeftWindows => "Left Win",
        VirtualKeys.RightWindows => "Right Win",
        VirtualKeys.CapsLock => "Caps Lock",
        VirtualKeys.Tab => "Tab",
        VirtualKeys.Escape => "Escape",
        VirtualKeys.Enter => "Enter",
        VirtualKeys.Space => "Space",
        VirtualKeys.Backspace => "Backspace",
        VirtualKeys.Delete => "Delete",
        VirtualKeys.Left => "Left Arrow",
        VirtualKeys.Up => "Up Arrow",
        VirtualKeys.Right => "Right Arrow",
        VirtualKeys.Down => "Down Arrow",
        >= 0x30 and <= 0x39 => ((char)keyCode).ToString(),
        >= 0x41 and <= 0x5A => ((char)keyCode).ToString(),
        >= 0x70 and <= 0x7B => $"F{keyCode - 0x6F}",
        _ => $"Key {keyCode:X2}",
    };

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public int VirtualKey;
        public int ScanCode;
        public int Flags;
        public int Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
