using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Commander;

public partial class MainWindow : FluentWindow
{
    private readonly MediaKeyInterceptor _interceptor = new();
    private readonly VoiceController _voiceController = new();
    private readonly VoiceHud _voiceHud = new();
    private readonly FunctionPage _functionPage;
    private readonly AboutPage _aboutPage;
    private readonly LanguagePage _languagePage;
    private bool _isQuitting;
    private bool _isDisposed;

    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: true);
        I18n.LanguageChanged += OnLanguageChanged;

        _functionPage = new FunctionPage(_interceptor, _voiceController, _voiceHud);
        _aboutPage = new AboutPage();
        _languagePage = new LanguagePage();
        _functionPage.IsEnabledChanged2 += (_, enabled) => UpdateTrayMenu(enabled);
        ContentHost.Children.Add(_functionPage);

        ApplyLocalization();
        Closing += MainWindow_Closing;
        Closed += (_, _) => DisposeServices();
    }

    private void ApplyLocalization()
    {
        Title = I18n.Get("title.app");
        TitleBar.Title = I18n.Get("title.app");
        TabFunction.Content = I18n.Get("tab.function");
        TabAbout.Content = I18n.Get("tab.about");
        TabLanguage.Content = I18n.Get("tab.language");
        TrayMenuTitle.Header = I18n.Get("menu.title");
        TrayMenuOpen.Header = I18n.Get("menu.open");
        TrayMenuQuit.Header = I18n.Get("menu.quit");
        TrayIcon.ToolTipText = I18n.Get("menu.title");
        UpdateTrayMenu(Settings.Instance.IsEnabled);
    }

    private void UpdateTrayMenu(bool enabled)
    {
        TrayMenuStatus.Header = enabled ? I18n.Get("menu.status.running") : I18n.Get("menu.status.stopped");
        TrayMenuToggle.Header = enabled ? I18n.Get("menu.disable") : I18n.Get("menu.enable");
    }

    private void OnLanguageChanged()
    {
        ApplyLocalization();
        _functionPage.RefreshLocalization();
        _aboutPage.RefreshLocalization();
        _languagePage.RefreshLocalization();
    }

    private void ShowPage(UserControl page)
    {
        if (ContentHost.Children.Count == 1 && ReferenceEquals(ContentHost.Children[0], page)) return;
        ContentHost.Children.Clear();
        ContentHost.Children.Add(page);
    }

    private void TabFunction_Checked(object sender, RoutedEventArgs e)
    {
        if (_functionPage is not null) ShowPage(_functionPage);
    }

    private void TabAbout_Checked(object sender, RoutedEventArgs e)
    {
        if (_aboutPage is not null) ShowPage(_aboutPage);
    }

    private void TabLanguage_Checked(object sender, RoutedEventArgs e)
    {
        if (_languagePage is not null) ShowPage(_languagePage);
    }

    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) => ShowMainWindow();
    private void TrayMenuOpen_Click(object sender, RoutedEventArgs e) => ShowMainWindow();
    private void TrayMenuToggle_Click(object sender, RoutedEventArgs e) => _functionPage.ToggleEnabled();

    private void TrayMenuQuit_Click(object sender, RoutedEventArgs e)
    {
        _isQuitting = true;
        Close();
        Application.Current.Shutdown();
    }

    private void ShowMainWindow()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isQuitting) return;
        e.Cancel = true;
        Hide();
    }

    private void DisposeServices()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        I18n.LanguageChanged -= OnLanguageChanged;
        SystemThemeWatcher.UnWatch(this);
        _voiceController.Dispose();
        _interceptor.Dispose();
        _voiceHud.Close();
        ModelDownloadService.Shared.Dispose();
        TrayIcon.Dispose();
    }
}
