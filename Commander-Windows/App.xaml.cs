using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Commander
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Commander",
            "error.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            // Global exception handlers — catch silent crashes
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Log("AppDomain", args.ExceptionObject as Exception);
                ShowError("非托管异常", args.ExceptionObject as Exception);
            };
            DispatcherUnhandledException += (s, args) =>
            {
                Log("Dispatcher", args.Exception);
                ShowError("UI 线程异常", args.Exception);
                args.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Log("Task", args.Exception);
                args.SetObserved();
            };

            try
            {
                base.OnStartup(e);
                Log("Startup", null, "Starting up...");

                // Single instance check
                _mutex = new Mutex(true, "Commander_AI接线员_Mutex", out bool createdNew);
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

                Log("Startup", null, "Initializing I18n");
                I18n.Init();

                Log("Startup", null, "Creating MainWindow");
                var window = new MainWindow();

                Log("Startup", null, "Showing MainWindow");
                window.Show();

                Log("Startup", null, "Started successfully");
            }
            catch (Exception ex)
            {
                Log("OnStartup", ex);
                ShowError("启动失败", ex);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private static void Log(string source, Exception? ex, string? message = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                using var w = new StreamWriter(LogPath, append: true);
                w.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {message ?? ex?.GetType().Name + ": " + ex?.Message}");
                if (ex != null)
                {
                    w.WriteLine(ex.StackTrace);
                    if (ex.InnerException != null)
                    {
                        w.WriteLine($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        w.WriteLine(ex.InnerException.StackTrace);
                    }
                }
                w.WriteLine();
            }
            catch { }
        }

        private static void ShowError(string title, Exception? ex)
        {
            try
            {
                string msg = ex?.Message ?? "Unknown error";
                if (ex?.InnerException != null) msg += $"\n\n内部异常：{ex.InnerException.Message}";
                msg += $"\n\n日志位置：{LogPath}";
                MessageBox.Show(msg, $"AI接线员 - {title}", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }
    }
}
