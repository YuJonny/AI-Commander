using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Commander;

public sealed record ForegroundTarget(nint WindowHandle, string? ProcessName, string? ProcessPath);

public static class TextInserter
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInput);

    public static ForegroundTarget CaptureForegroundTarget()
    {
        var handle = GetForegroundWindow();
        try
        {
            _ = GetWindowThreadProcessId(handle, out var processId);
            using var process = Process.GetProcessById((int)processId);
            return new ForegroundTarget(handle, process.ProcessName, process.MainModule?.FileName);
        }
        catch
        {
            return new ForegroundTarget(handle, null, null);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    public static void Insert(string text, ForegroundTarget target)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (target.WindowHandle != 0) SetForegroundWindow(target.WindowHandle);

        const int maxEventsPerBatch = 256;
        var events = new List<INPUT>(Math.Min(text.Length * 2, maxEventsPerBatch));
        foreach (var codeUnit in text)
        {
            events.Add(UnicodeInput(codeUnit, keyUp: false));
            events.Add(UnicodeInput(codeUnit, keyUp: true));
            if (events.Count >= maxEventsPerBatch) SendBatch(events);
        }
        SendBatch(events);
    }

    private static INPUT UnicodeInput(char codeUnit, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = 0,
                ScanCode = codeUnit,
                Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
            },
        },
    };

    private static void SendBatch(List<INPUT> events)
    {
        if (events.Count == 0) return;
        var array = events.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        events.Clear();
        if (sent != array.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked synthesized text input.");
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
        [FieldOffset(0)] public HardwareInput Hardware;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
