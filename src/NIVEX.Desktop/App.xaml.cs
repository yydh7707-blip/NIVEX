using NIVEX.Application;
using NIVEX.Infrastructure;

namespace NIVEX.Desktop;

public partial class App : System.Windows.Application
{
    private FileLog? _log;
    private WindowsMetrics? _metrics;

    protected override void OnStartup(StartupEventArgs e)
    {
        _log = new FileLog();
        DispatcherUnhandledException += (_, args) =>
        {
            _log.Error("dispatcher", args.Exception);
            args.Handled = true;
            MessageBox.Show("요청을 처리하지 못했습니다. 로그를 확인하세요.", "NIVEX");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) _log.Error("process", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Error("task", args.Exception);
            args.SetObserved();
        };

        var settings = new JsonSettingsStore();
        var vault = new DpapiVault();
        var api = new ApiClient(settings);
        _metrics = new WindowsMetrics();
        var startup = new StartupRegistration();
        var sessions = new SessionService(api, vault);
        var model = new AppModel(sessions, api, settings, _metrics, startup, _log, Dispatcher);
        var window = new MainWindow(model);
        MainWindow = window;
        window.Show();
        _ = model.InitializeAsync();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window) window.Model.Shutdown();
        _metrics?.Dispose();
        base.OnExit(e);
    }
}
