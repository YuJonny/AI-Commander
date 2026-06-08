using System;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Commander
{
    public partial class MainWindow : FluentWindow
    {
        private readonly MediaKeyInterceptor _interceptor = new();
        private FunctionPage? _funcPage;
        private AboutPage? _aboutPage;
        private LanguagePage? _langPage;

        public MainWindow()
        {
            InitializeComponent();
            I18n.LanguageChanged += OnLanguageChanged;

            // Apply initial localization
            ApplyLocalization();

            // Setup pages
            _funcPage = new FunctionPage(_interceptor);
            _aboutPage = new AboutPage();
            _langPage = new LanguagePage();

            _funcPage.IsEnabledChanged2 += (s, enabled) => UpdateTrayMenu(enabled);

            ContentHost.Children.Add(_funcPage);

            // Set up close → hide
            Closing += MainWindow_Closing;
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
            UpdateTrayMenu(Settings.Instance.IsEnabled);
            TrayIcon.ToolTipText = I18n.Get("menu.title");
        }

        private void UpdateTrayMenu(bool enabled)
        {
            TrayMenuStatus.Header = enabled ? I18n.Get("menu.status.running") : I18n.Get("menu.status.stopped");
            TrayMenuToggle.Header = enabled ? I18n.Get("menu.disable") : I18n.Get("menu.enable");
        }

        private void OnLanguageChanged()
        {
            ApplyLocalization();
            _funcPage?.RefreshLocalization();
            _aboutPage?.RefreshLocalization();
            _langPage?.RefreshLocalization();
        }

        private void ShowPage(UserControl page)
        {
            ContentHost.Children.Clear();
            ContentHost.Children.Add(page);
        }

        private void TabFunction_Checked(object sender, RoutedEventArgs e) { if (_funcPage != null) ShowPage(_funcPage); }
        private void TabAbout_Checked(object sender, RoutedEventArgs e) { if (_aboutPage != null) ShowPage(_aboutPage); }
        private void TabLanguage_Checked(object sender, RoutedEventArgs e) { if (_langPage != null) ShowPage(_langPage); }

        // Tray menu handlers
        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) => ShowMainWindow();
        private void TrayMenuOpen_Click(object sender, RoutedEventArgs e) => ShowMainWindow();
        private void TrayMenuToggle_Click(object sender, RoutedEventArgs e) => _funcPage?.ToggleEnabled();
        private void TrayMenuQuit_Click(object sender, RoutedEventArgs e)
        {
            TrayIcon.Dispose();
            _interceptor.Dispose();
            Application.Current.Shutdown();
        }

        private void ShowMainWindow()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Hide instead of closing - keep running in tray
            e.Cancel = true;
            Hide();
        }
    }
}
