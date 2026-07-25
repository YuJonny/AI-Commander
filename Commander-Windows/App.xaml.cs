using System.Threading;
using System.Windows;

namespace Commander;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            AppLog.Error(nameof(AppDomain), exception, "Unhandled exception");
            ShowError("非托管异常", exception);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Dispatcher", args.Exception, "UI thread exception");
            ShowError("UI 线程异常", args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error(nameof(TaskScheduler), args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        try
        {
            base.OnStartup(e);
            AppLog.Info(nameof(App), "Starting v2.0");
            _mutex = new Mutex(true, "Commander_AI接线员_Mutex", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "AI接线员已经在运行中",
                    "AI接线员",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            I18n.Init();
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            AppLog.Info(nameof(App), "Started successfully");
        }
        catch (Exception exception)
        {
            AppLog.Error(nameof(App), exception, "Startup failed");
            ShowError("启动失败", exception);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void ShowError(string title, Exception? exception)
    {
        try
        {
            var message = exception?.Message ?? "Unknown error";
            if (exception?.InnerException is not null)
                message += $"\n\n内部异常：{exception.InnerException.Message}";
            message += $"\n\n日志位置：{AppPaths.LogPath}";
            MessageBox.Show(message, $"AI接线员 - {title}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }
}
