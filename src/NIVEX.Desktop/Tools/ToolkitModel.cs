using System.IO;
using System.Security.Principal;
using NIVEX.Application;
using NIVEX.Infrastructure;
using NIVEX.Toolkit;

namespace NIVEX.Desktop;

public sealed class ToolkitModel : ToolVm, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<int> _intervalSeconds;
    private readonly Action<string> _navigate;

    public ToolkitModel(ISystemMetrics metrics, Dispatcher dispatcher, Action<string> navigate, Func<int> intervalSeconds, string version)
    {
        _dispatcher = dispatcher;
        _navigate = navigate;
        _intervalSeconds = intervalSeconds;
        var root = AppPaths.Root;
        Directory.CreateDirectory(Path.Combine(root, "insight"));
        var workflowDir = Path.Combine(root, "workflows");
        Directory.CreateDirectory(workflowDir);

        History = new HistoryStore(Path.Combine(root, "insight", "history.json"));
        Confirm = new ConfirmModel();
        Service = new MonitorService(metrics);
        var network = new NetworkLab(new SystemPinger(), new SystemDnsResolver(), NetworkLab.ReadAdapters);
        var admin = IsAdministrator();
        var required = new List<string>();
        if (Environment.ProcessPath is { } exe) required.Add(exe);
        var appAssembly = typeof(SessionService).Assembly.Location;
        if (appAssembly.Length > 0) required.Add(appAssembly);
        var fixService = new FixService(
            new FixOptions(Path.GetTempPath(), AppPaths.Settings, AppPaths.LogFile, required, version, () => admin, FreeBytes, TotalBytes),
            new ProcessRunner(), network);

        Engine = new WorkflowEngine(new SystemStepHost(ConfirmStepAsync));
        Fix = new FixVm(fixService, this, admin);
        Monitor = new MonitorVm(Service, this, dispatcher);
        Network = new NetworkVm(network, this, Path.Combine(root, "insight", "network-last.json"));
        Flow = new FlowVm(new WorkflowStore(workflowDir), this);
        Profiles = new ProfilesVm(new ProfileStore(Path.Combine(root, "profiles.json")), this);
        Insight = new InsightVm(this);
        Overview = new OverviewVm(this);
        Profiles.RefreshWorkflowChoices();
        NotifyHistoryChanged();
    }

    public HistoryStore History { get; }
    public ConfirmModel Confirm { get; }
    public MonitorService Service { get; }
    public WorkflowEngine Engine { get; }
    public FixVm Fix { get; }
    public MonitorVm Monitor { get; }
    public NetworkVm Network { get; }
    public FlowVm Flow { get; }
    public ProfilesVm Profiles { get; }
    public InsightVm Insight { get; }
    public OverviewVm Overview { get; }

    public void Navigate(string section) => _navigate(section);

    public void AddHistory(HistoryRecord record)
    {
        History.Add(record);
        NotifyHistoryChanged();
    }

    public void NotifyHistoryChanged()
    {
        Insight.Refresh();
        Overview.Refresh();
    }

    public void OnSection(string section)
    {
        if (section == "Monitor") Monitor.Start(_intervalSeconds());
        else Monitor.Stop();
        if (section is "Home") Overview.Refresh();
        if (section == "Insight") Insight.Refresh();
    }

    public void StopAll()
    {
        Monitor.Stop();
        Confirm.Dismiss();
    }

    public void Dispose() => Service.Dispose();

    private Task<bool> ConfirmStepAsync(string message, CancellationToken token)
    {
        var task = _dispatcher.InvokeAsync(() =>
        {
            token.Register(() => _dispatcher.BeginInvoke(Confirm.Dismiss));
            return Confirm.AskAsync(ToolText.Get("step_Confirm"), message, ToolText.Get("confirm"));
        });
        return task.Task.Unwrap();
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static long? FreeBytes()
    {
        try { return new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace; }
        catch (Exception) { return null; }
    }

    private static long? TotalBytes()
    {
        try { return new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).TotalSize; }
        catch (Exception) { return null; }
    }
}
