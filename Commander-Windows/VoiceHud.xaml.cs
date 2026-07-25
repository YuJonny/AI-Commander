using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Commander;

public partial class VoiceHud : Wpf.Ui.Controls.FluentWindow
{
    private readonly DispatcherTimer _hideTimer;

    public VoiceHud()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _hideTimer.Tick += (_, _) => HideNow();
        SourceInitialized += (_, _) => MakeNonActivating();
    }

    public void ShowState(VoiceState state)
    {
        _hideTimer.Stop();
        switch (state.Kind)
        {
            case VoiceStateKind.Listening:
                ShowTransient(state.Text, Color.FromRgb(0xE5, 0x39, 0x35), autoHide: false);
                break;
            case VoiceStateKind.Transcribing:
                ShowTransient(state.Text, Color.FromRgb(0xC7, 0x6E, 0x00), autoHide: false);
                break;
            case VoiceStateKind.Enhancing:
                ShowTransient(state.Text, Color.FromRgb(0x00, 0x78, 0xD4), autoHide: false);
                break;
            case VoiceStateKind.Inserted:
                HideNow();
                break;
            case VoiceStateKind.NoAudio:
            case VoiceStateKind.NoTranscript:
            case VoiceStateKind.ModelMissing:
            case VoiceStateKind.Error:
                ShowTransient(ShortText(state.Text), Color.FromRgb(0x7A, 0x7A, 0x7A), autoHide: true);
                break;
        }
    }

    private void ShowTransient(string text, Color color, bool autoHide)
    {
        StateText.Text = text;
        StateDot.Fill = new SolidColorBrush(color);
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 72;

        if (!IsVisible) Show();
        else NativeMethods.ShowWithoutActivation(new WindowInteropHelper(this).Handle);

        if (autoHide) _hideTimer.Start();
    }

    private static string ShortText(string value)
    {
        const int maximum = 24;
        return value.Length > maximum ? value[..(maximum - 1)] + "…" : value;
    }

    private void HideNow()
    {
        _hideTimer.Stop();
        Hide();
    }

    private void MakeNonActivating()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow | NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new nint(style));
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer.Stop();
        base.OnClosed(e);
    }

    private static class NativeMethods
    {
        internal const int GwlExStyle = -20;
        internal const long WsExTransparent = 0x00000020L;
        internal const long WsExToolWindow = 0x00000080L;
        internal const long WsExNoActivate = 0x08000000L;
        private const int SwShowNoActivate = 4;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern nint GetWindowLongPtr(nint window, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern nint SetWindowLongPtr(nint window, int index, nint value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool ShowWindow(nint window, int command);

        internal static void ShowWithoutActivation(nint window) => ShowWindow(window, SwShowNoActivate);
    }
}
