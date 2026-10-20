using System.Windows;

namespace SneznyjBrowser;

public partial class App : Application
{
    Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Ошибка в любом обработчике раньше молча закрывала весь браузер (например, когда окно входа Google
        // закрывалось само). Теперь она пишется в crash.log, а браузер работает дальше.
        DispatcherUnhandledException += (_, ev) =>
        {
            CrashLog.Write("окно", ev.Exception);
            ev.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            CrashLog.Write("фоновая задача", ev.Exception);
            ev.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => CrashLog.Write("программа", ev.ExceptionObject as Exception);
        // Для проверок: окно рисуется без видеокарты, тогда снимок PrintWindow видно, даже если окно перекрыто.
        if (Environment.GetEnvironmentVariable("SNEZNYJ_BROWSER_SOFTWARE") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Одна копия браузера: повторный запуск передаёт ссылки уже открытому окну.
        var mutex = new Mutex(true, "SneznyjBrowser.Single." + AppPaths.InstanceKey, out bool created);
        if (!created)
        {
            mutex.Dispose();
            SingleInstance.SendToRunning(e.Args);
            Shutdown();
            return;
        }
        _mutex = mutex;

        // --no-activate: окно появляется, но не забирает фокус (для скриптов и обновления в фоне).
        bool activate = !e.Args.Contains("--no-activate");
        AppPaths.Ensure();
        // Тема нужна ещё до первого окна: все цвета интерфейса берутся из неё.
        var settings = Json.Load<SettingsData>(AppPaths.SettingsFile);
        settings.Normalize();
        Themes.Apply(settings);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        WelcomeWindow.RunIfFirstStart(activate);

        var window = new MainWindow(e.Args) { ShowActivated = activate };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        SingleInstance.Listen(args => Dispatcher.BeginInvoke(() => window.OpenFromOutside(args)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
