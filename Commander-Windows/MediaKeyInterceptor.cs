using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Commander
{
    public class MediaKeyInterceptor : IDisposable
    {
        // Windows API constants
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const int VK_MEDIA_NEXT_TRACK = 0xB0;
        private const int VK_MEDIA_PREV_TRACK = 0xB1;
        private const int VK_MEDIA_STOP = 0xB2;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        // P/Invoke
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        // Union must include MOUSEINPUT so its size matches Windows' INPUT union (32 bytes on x64).
        // Without it, Marshal.SizeOf<INPUT>() is too small and SendInput silently fails.
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _hookProc;

        public int TargetKeyCode { get; set; } = 0xA3; // VK_RCONTROL
        public event Action<string>? OnStatusChange;

        public void Start()
        {
            Stop();
            _hookProc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(curModule.ModuleName), 0);

            if (_hookId == IntPtr.Zero)
            {
                OnStatusChange?.Invoke(I18n.Get("status.hook.failed"));
            }
            else
            {
                OnStatusChange?.Invoke(I18n.Get("status.started"));
            }
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _hookProc = null;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                if (hookStruct.vkCode == VK_MEDIA_PLAY_PAUSE)
                {
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        OnStatusChange?.Invoke(I18n.Get("status.key.down"));
                        SimulateKey(true);
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        OnStatusChange?.Invoke(I18n.Get("status.key.up"));
                        SimulateKey(false);
                    }
                    // Block the media key by not calling CallNextHookEx
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void SimulateKey(bool down)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)TargetKeyCode,
                        wScan = 0,
                        dwFlags = (down ? 0u : KEYEVENTF_KEYUP) | (IsExtendedKey(TargetKeyCode) ? KEYEVENTF_EXTENDEDKEY : 0u),
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static bool IsExtendedKey(int vk)
        {
            // Extended keys: right ctrl, right alt, arrow keys, insert, delete, home, end, page up/down, numpad enter, etc.
            return vk is 0xA3 or 0xA5 or 0x25 or 0x26 or 0x27 or 0x28
                or 0x2D or 0x2E or 0x24 or 0x23 or 0x21 or 0x22
                or 0x5B or 0x5C; // Win keys
        }

        public void Dispose()
        {
            Stop();
        }

        // Map virtual key code to display name
        public static string GetKeyName(int vk)
        {
            return vk switch
            {
                0xA2 => "Left Ctrl",
                0xA3 => "Right Ctrl",
                0xA0 => "Left Shift",
                0xA1 => "Right Shift",
                0xA4 => "Left Alt",
                0xA5 => "Right Alt",
                0x5B => "Left Win ⊞",
                0x5C => "Right Win ⊞",
                0x14 => "Caps Lock",
                0x09 => "Tab",
                0x1B => "Escape",
                0x0D => "Enter ↩",
                0x20 => "Space",
                0x08 => "Backspace ⌫",
                0x2E => "Delete",
                0x2D => "Insert",
                0x24 => "Home",
                0x23 => "End",
                0x21 => "Page Up",
                0x22 => "Page Down",
                0x25 => "←",
                0x26 => "↑",
                0x27 => "→",
                0x28 => "↓",
                0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
                0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
                0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
                >= 0x30 and <= 0x39 => ((char)vk).ToString(),
                >= 0x41 and <= 0x5A => ((char)vk).ToString(),
                >= 0x60 and <= 0x69 => $"Numpad {vk - 0x60}",
                0x6A => "Numpad *",
                0x6B => "Numpad +",
                0x6D => "Numpad -",
                0x6E => "Numpad .",
                0x6F => "Numpad /",
                _ => $"Key {vk:X2}"
            };
        }
    }
}
