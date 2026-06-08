using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;

namespace Commander
{
    public partial class FunctionPage : UserControl
    {
        private readonly MediaKeyInterceptor _interceptor;
        private bool _isRecording = false;
        private bool _suppressEvents = false;

        public event EventHandler<bool>? IsEnabledChanged2;

        // Key recording hook
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public int vkCode; public int scanCode; public int flags; public int time; public IntPtr dwExtraInfo; }

        private IntPtr _recordHookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _recordHookProc;

        public FunctionPage(MediaKeyInterceptor interceptor)
        {
            InitializeComponent();
            _interceptor = interceptor;

            try { RefreshLocalization(); } catch { }
            try { LoadSettings(); } catch { }
        }

        public void RefreshLocalization()
        {
            LblTitle.Text = I18n.Get("title.app");
            LblSubtitle.Text = I18n.Get("title.subtitle");
            LblToggleTitle.Text = I18n.Get("toggle.title");
            LblToggleDesc.Text = I18n.Get("toggle.description");
            LblLaunchTitle.Text = I18n.Get("launch.title");
            LblLaunchDesc.Text = I18n.Get("launch.description");
            LblMappingTitle.Text = I18n.Get("mapping.title").ToUpper();
            LblSource.Text = I18n.Get("mapping.source");
            BtnChangeKey.Content = _isRecording ? I18n.Get("mapping.cancel") : I18n.Get("mapping.change");
            LblReset.Text = "↻  " + I18n.Get("mapping.reset");
        }

        private void LoadSettings()
        {
            _suppressEvents = true;
            var s = Settings.Instance;
            _interceptor.TargetKeyCode = s.TargetKeyCode;
            LblTarget.Text = s.TargetKeyName;
            SwLaunch.IsChecked = s.LaunchAtLogin;
            SwEnabled.IsChecked = s.IsEnabled;
            _suppressEvents = false;
            // Trigger interceptor if needed
            if (s.IsEnabled) { _interceptor.Start(); IsEnabledChanged2?.Invoke(this, true); }
        }

        public void ToggleEnabled()
        {
            SwEnabled.IsChecked = !(SwEnabled.IsChecked ?? false);
        }

        private void SwEnabled_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            _interceptor.TargetKeyCode = Settings.Instance.TargetKeyCode;
            _interceptor.Start();
            Settings.Instance.IsEnabled = true;
            Settings.Instance.Save();
            IsEnabledChanged2?.Invoke(this, true);
        }

        private void SwEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            _interceptor.Stop();
            Settings.Instance.IsEnabled = false;
            Settings.Instance.Save();
            IsEnabledChanged2?.Invoke(this, false);
        }

        private void SwLaunch_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            StartupHelper.Enable();
            Settings.Instance.LaunchAtLogin = true;
            Settings.Instance.Save();
        }

        private void SwLaunch_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            StartupHelper.Disable();
            Settings.Instance.LaunchAtLogin = false;
            Settings.Instance.Save();
        }

        private void BtnChangeKey_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording) StopRecording();
            else StartRecording();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.TargetKeyCode = 0xA5;
            Settings.Instance.TargetKeyName = "Right Alt";
            Settings.Instance.Save();
            LblTarget.Text = "Right Alt";
            _interceptor.TargetKeyCode = 0xA5;
        }

        private void StartRecording()
        {
            _isRecording = true;
            LblTarget.Text = I18n.Get("mapping.recording");
            LblTarget.Foreground = new SolidColorBrush(Color.FromRgb(180, 100, 0));
            TargetTag.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xA5, 0x00));
            BtnChangeKey.Content = I18n.Get("mapping.cancel");

            _recordHookProc = RecordHookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _recordHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _recordHookProc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private void StopRecording()
        {
            _isRecording = false;
            LblTarget.Text = Settings.Instance.TargetKeyName;
            LblTarget.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA2, 0x48));
            TargetTag.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x16, 0xA2, 0x48));
            BtnChangeKey.Content = I18n.Get("mapping.change");
            if (_recordHookId != IntPtr.Zero) { UnhookWindowsHookEx(_recordHookId); _recordHookId = IntPtr.Zero; }
            _recordHookProc = null;
        }

        private IntPtr RecordHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int vk = hookStruct.vkCode;
                    if (vk is not (0xB0 or 0xB1 or 0xB2 or 0xB3))
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            var name = MediaKeyInterceptor.GetKeyName(vk);
                            Settings.Instance.TargetKeyCode = vk;
                            Settings.Instance.TargetKeyName = name;
                            Settings.Instance.Save();
                            _interceptor.TargetKeyCode = vk;
                            StopRecording();
                            if (SwEnabled.IsChecked == true) _interceptor.Start();
                        });
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_recordHookId, nCode, wParam, lParam);
        }
    }
}
