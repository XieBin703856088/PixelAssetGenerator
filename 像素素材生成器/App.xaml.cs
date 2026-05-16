using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;

namespace PixelAssetGenerator
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            RenderOptions.ProcessRenderMode = RenderMode.Default;

            // 设置控制台输出编码为 UTF-8，修复日志中文乱码
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            // Global exception handlers
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                try
                {
                    var ex = ev.ExceptionObject as Exception;
                    var msg = ex != null
                        ? $"[全局异常] UnhandledException: {ex.Message}"
                        : $"[Global Exception] Non-Exception: {ev.ExceptionObject}";
                    System.Diagnostics.Trace.TraceError(msg);
                    TryConsoleLogError(msg, "AppDomain.UnhandledException", ex);
                }
                catch { }
            };

            DispatcherUnhandledException += (s, ev) =>
            {
                var msg = $"[全局异常] DispatcherUnhandledException: {ev.Exception?.Message}";
                System.Diagnostics.Trace.TraceError(msg);
                TryConsoleLogError(msg, "DispatcherUnhandledException", ev.Exception);
            };

            TaskScheduler.UnobservedTaskException += (s, ev) =>
            {
                var msg = $"[全局异常] UnobservedTaskException: {ev.Exception?.Message}";
                System.Diagnostics.Trace.TraceError(msg);
                TryConsoleLogError(msg, "UnobservedTaskException", ev.Exception);
            };

            // Initialize DI container (before any service is resolved)
            Services.ServiceLocator.EnsureInitialized();

            // Early init: theme and language (instant, no splash needed)
            SettingsService.ApplyTheme(SettingsService.Current.Theme);
            var lang = SettingsService.Current.Language;
            if (!string.IsNullOrEmpty(lang))
            {
                try { Services.Localization.LocalizationService.Instance.SetCulture(lang); }
                catch { }
            }

            base.OnStartup(e);

            // Show splash, then create and initialize main window asynchronously
            var splash = new SplashWindow();
            splash.Show();

            Dispatcher.BeginInvoke(new Func<Task>(async () =>
            {
                try
                {
                    splash.ReportProgress(0.05, "正在加载配置...", "");
                    await Task.Delay(60);

                    splash.ReportProgress(0.10, "正在加载语言包...", "");
                    await Task.Delay(60);

                    splash.ReportProgress(0.15, "正在创建主窗口...", "");
                    var mainWindow = new MainWindow();

                    // MainWindow constructor only does the minimum. Heavy init happens
                    // in InitializeAsync, which yields to the UI thread between steps so
                    // the splash window can render progress updates in real time.
                    await mainWindow.InitializeAsync(splash);

                    splash.ReportProgress(0.98, "正在完成启动...", "");
                    await Task.Delay(80);

                    splash.ReportProgress(1.0, "启动完成", "");
                    await Task.Delay(150);

                    splash.Close();
                    mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    mainWindow.Show();
                    mainWindow.Activate();
                    mainWindow.Topmost = true;
                    mainWindow.Topmost = false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError($"[启动异常] {ex}");
                    splash.Close();
                    // Show main window even if initialization failed
                    var fallbackWin = new MainWindow();
                    fallbackWin.Show();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Dispatcher.InvokeShutdown();
            Environment.Exit(0);
        }

        /// <summary>
        /// Attempts to log an error to the console service if it is already registered.
        /// Safe to call before DI is initialized (silently no-ops).
        /// </summary>
        private static void TryConsoleLogError(string message, string source, Exception? ex)
        {
            try
            {
                var svc = Services.ServiceLocator.TryGetService<Services.IConsoleService>();
                svc?.LogError(message, source, ex);
            }
            catch
            {
                // Console service not available yet — Trace output is sufficient
            }
        }
    }
}
