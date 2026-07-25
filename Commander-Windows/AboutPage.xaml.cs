using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Commander;

public partial class AboutPage : UserControl
{
    private bool _suppressEvents;

    public AboutPage()
    {
        InitializeComponent();
        _suppressEvents = true;
        SwDiagnostics.IsChecked = Settings.Instance.PipelineDiagnosticsEnabled;
        _suppressEvents = false;
        RefreshLocalization();
    }

    public void RefreshLocalization()
    {
        LblAppName.Text = I18n.Get("title.app");
        LblVersion.Text = I18n.IsChinese ? "AI Commander · 2.0" : "AI接线员 · 2.0";
        LblHeroDescription.Text = I18n.Get("about.description");
        LblFree.Text = T("为爱发电 永久免费", "Made with love · Free forever");
        LblProductTitle.Text = T("关于", "About");
        LblPrivacyTitle.Text = T("全程本地", "Entirely on device");
        LblDesc.Text = T(
            "录音、识别与可选润色均在本机完成。",
            "Recording, recognition, and optional polishing all run on this PC.");
        LblRecommendedTitle.Text = I18n.Get("about.recommended");
        BtnTypeless.Content = I18n.Get("about.app.typeless");
        LblTypelessDetail.Text = T("语音输入", "Voice input");
        LblHardwareTitle.Text = I18n.Get("about.recommendedHardware");
        BtnNativeUnion.Content = I18n.Get("about.hw.nativeunion");
        LblNativeUnionDetail.Text = I18n.Get("about.hw.nativeunion.desc");
        LblDiagnosticsTitle.Text = T("诊断", "Diagnostics");
        LblDiagnosticsToggle.Text = T("语音链路诊断", "Voice pipeline diagnostics");
        LblDiagnosticsDescription.Text = T(
            "仅排查问题时开启；会在本地保存录音与各阶段结果。",
            "Enable only for troubleshooting. Recordings and stage results are stored locally.");
        LblOpenDiagnostics.Text = T("打开诊断文件夹", "Open diagnostics folder");
        LblDeveloperTitle.Text = T("开发者", "Developer");
    }

    private void SwDiagnostics_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        Settings.Instance.PipelineDiagnosticsEnabled = SwDiagnostics.IsChecked == true;
        Settings.Instance.Save();
    }

    private void BtnOpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.DiagnosticsRoot);
        Process.Start(new ProcessStartInfo(AppPaths.DiagnosticsRoot) { UseShellExecute = true });
    }

    private static string T(string chinese, string english) => I18n.IsChinese ? chinese : english;
}
