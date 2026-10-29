using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using NIVEX.Toolkit;

namespace NIVEX.Desktop;

public sealed class FixRow(string id) : Observable
{
    private CheckStatus _status = CheckStatus.Pending;
    private string _message = "";
    private string? _advice;
    private string? _repairId;

    public string Id { get; } = id;
    public string Title => ToolText.Get("chk_" + Id);
    public CheckStatus Status => _status;
    public string Message => _message;
    public string? RepairId => _repairId;
    public bool CanRepair => _repairId is not null && _status == CheckStatus.Problem;
    public bool HasMessage => !string.IsNullOrWhiteSpace(_message);
    public string StatusText => ToolText.Get("st_" + _status.ToString().ToLowerInvariant());

    public string Glyph => _status switch
    {
        CheckStatus.Healthy or CheckStatus.Repaired => "✓",
        CheckStatus.Problem or CheckStatus.Failed or CheckStatus.RepairFailed => "!",
        CheckStatus.Unknown => "?",
        _ => "·",
    };

    public Brush StatusBrush => _status switch
    {
        CheckStatus.Problem or CheckStatus.Failed or CheckStatus.RepairFailed => Fmt.Resource("Danger"),
        CheckStatus.Healthy or CheckStatus.Repaired => Fmt.Resource("Ink"),
        _ => Fmt.Resource("Muted"),
    };

    public string AdviceText
    {
        get
        {
            if (_advice is null) return "";
            if (Id is "disk" or "temp" or "dns" or "internet" or "adapter" or "nivex") return ToolText.Get("adv_" + Id);
            if (Id is "sfc" or "dism")
            {
                if (_status == CheckStatus.Unknown && _message.Contains("administrator rights", StringComparison.OrdinalIgnoreCase))
                    return ToolText.Get("adv_admin");
                if (_status == CheckStatus.Unknown && _message.Contains("output did not confirm", StringComparison.OrdinalIgnoreCase))
                    return ToolText.Get("adv_output");
                return ToolText.Get("adv_tool");
            }
            return _advice;
        }
    }

    public bool HasAdvice => _advice is not null;

    public void Apply(CheckResult result)
    {
        _status = result.Status;
        _message = result.Message;
        _advice = result.Advice;
        _repairId = result.RepairId;
        Notify(null);
    }

    public void Refresh() => Notify(null);
}

public sealed class FixVm : ToolVm
{
    private readonly FixService _fix;
    private readonly ToolkitModel _host;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _active;
    private string _summary = "";
    private string _message = "";
    private string _lastScan = "";
    private readonly string _version;
    private readonly CrashLab _crash = new(new ProcessRunner());
    private List<CheckResult> _last = [];
    private List<ErrorEvent> _errors = [];
    private string _compare = "";
    private string _diagNote = "";

    public FixVm(FixService fix, ToolkitModel host, bool isAdmin, string version = "", string dataDir = "")
    {
        _fix = fix;
        _host = host;
        _version = version;
        IsAdmin = isAdmin;
        Extras = new FixExtrasVm(fix, host, isAdmin, dataDir.Length == 0 ? Path.Combine(Path.GetTempPath(), "nivex-fix") : dataDir);
        RunAllCommand = new AsyncCommand(_ => RunAllAsync(), _ => !Busy && Rows.Any(r => r.CanRepair));
        ExportCommand = new AsyncCommand(_ => ExportAsync());
        ErrorsCommand = new AsyncCommand(_ => LoadErrorsAsync(), _ => !Busy);
        LargeCommand = new AsyncCommand(_ => FindLargeAsync(), _ => !Busy);
        QuickCommand = new AsyncCommand(_ => ScanAsync(false), _ => !Busy);
        FullCommand = new AsyncCommand(_ => ScanAsync(true), _ => !Busy);
        CancelCommand = new AsyncCommand(_ => { _cts?.Cancel(); return Task.CompletedTask; }, _ => Busy);
        RepairCommand = new AsyncCommand(RepairAsync, p => !Busy && p is FixRow { CanRepair: true });
        foreach (var id in FixService.QuickIds) Rows.Add(new FixRow(id));
    }

    public bool IsAdmin { get; }
    public FixExtrasVm Extras { get; }
    public ICommand RunAllCommand { get; }
    public ObservableCollection<FindingRow> Findings { get; } = new();
    public ObservableCollection<InfoRow> RecurringIssues { get; } = new();
    public ObservableCollection<InfoRow> Errors { get; } = new();
    public ObservableCollection<InfoRow> Large { get; } = new();
    public ICommand ExportCommand { get; }
    public ICommand ErrorsCommand { get; }
    public ICommand LargeCommand { get; }
    public bool HasFindings => Findings.Count > 0;
    public bool HasRecurringIssues => RecurringIssues.Count > 0;
    public string Compare { get => _compare; private set { Set(ref _compare, value); Notify(nameof(HasCompare)); } }
    public bool HasCompare => _compare.Length > 0;
    public string DiagNote { get => _diagNote; private set => Set(ref _diagNote, value); }
    public ObservableCollection<FixRow> Rows { get; } = new();
    public ICommand QuickCommand { get; }
    public ICommand FullCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RepairCommand { get; }
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Notify(nameof(Idle)); } }
    public bool Idle => !_busy;
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string LastScan => _lastScan.Length == 0 ? ToolText.Get("neverScanned") : _lastScan;
    public string AdminText => ToolText.Get(IsAdmin ? "adminYes" : "adminNo");

    protected override void OnLanguage()
    {
        foreach (var row in Rows) row.Refresh();
        RebuildFindings();
        RefreshAll();
    }

    private async Task ScanAsync(bool full)
    {
        Busy = true;
        Message = "";
        Summary = ToolText.Get("scanning");
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Rows.Clear();
        foreach (var id in full ? FixService.FullIds : FixService.QuickIds) Rows.Add(new FixRow(id));
        _active = true;
        try
        {
            var progress = new Progress<CheckResult>(r =>
            {
                if (_active) Rows.FirstOrDefault(x => x.Id == r.Id)?.Apply(r);
            });
            var results = await _fix.ScanAsync(full, progress, token);
            _active = false;
            if (token.IsCancellationRequested)
            {
                Message = ToolText.Get("scanCancelled");
                Summary = "";
                return;
            }
            foreach (var result in results) Rows.FirstOrDefault(x => x.Id == result.Id)?.Apply(result);
            var healthy = results.Count(r => r.Status == CheckStatus.Healthy);
            var problems = results.Count(r => r.Status == CheckStatus.Problem);
            var unknown = results.Count(r => r.Status == CheckStatus.Unknown);
            var failed = results.Count(r => r.Status == CheckStatus.Failed);
            Summary = ToolText.F("scanSummary", healthy, problems, unknown, failed);
            _last = results.ToList();
            Compare = "";
            RebuildFindings();
            RebuildRecurringIssues(results);
            _lastScan = Fmt.Time(DateTimeOffset.Now);
            Notify(nameof(LastScan));
            _host.AddHistory(new HistoryRecord
            {
                Kind = HistoryKind.Fix,
                Title = ToolText.Get(full ? "fullScan" : "quickScan"),
                Success = problems == 0 && failed == 0,
                Summary = Summary,
                ErrorType = problems + failed == 0 ? null : string.Join(",", results.Where(r => r.Status is CheckStatus.Problem or CheckStatus.Failed).Select(r => r.Id)),
                Detail = string.Join("\n", results.Select(r => $"{r.Id}: {r.Status} - {r.Message}")),
                Advice = string.Join("\n", results.Where(r => r.Advice is not null).Select(r => r.Advice)),
            });
        }
        catch (InvalidOperationException)
        {
            Message = ToolText.Get("flowBusy2");
        }
        finally
        {
            _active = false;
            Busy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task RepairAsync(object? parameter)
    {
        if (parameter is not FixRow row || row.RepairId is null) return;
        Busy = true;
        Message = "";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try { await RepairOneAsync(row, token); }
        finally
        {
            Busy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    // Workspace: one confirmation for the plan, then each repair in order. A failed step stops the queue.
    private async Task RunAllAsync()
    {
        var rows = Rows.Where(r => r.CanRepair).GroupBy(r => r.RepairId).Select(g => g.First()).ToList();
        if (rows.Count == 0) return;
        var plan = string.Join("\n", rows.Select((r, i) => $"{i + 1}. {ToolText.Get("rep_" + r.RepairId)}"));
        if (!await _host.Confirm.AskAsync(ToolText.Get("workspaceTitle"), ToolText.F("workspaceBody", plan))) return;
        Busy = true;
        Message = "";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            foreach (var row in rows)
            {
                var ok = await RepairOneAsync(row, token);
                if (ok is not true)
                {
                    if (ok == false) Message = ToolText.Get("workspaceStopped");
                    break;
                }
            }
        }
        finally
        {
            Busy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task<bool?> RepairOneAsync(FixRow row, CancellationToken token)
    {
        var ids = row.RepairId == "flush-dns" ? new[] { row.Id } : new[] { "disk", "temp" };
        var before = ids.Select(id => _last.FirstOrDefault(r => r.Id == id)).OfType<CheckResult>().ToList();
        bool? ok = null;
        try
        {
            if (row.RepairId == "flush-dns") ok = await FlushDnsAsync(row, token);
            else if (row.RepairId == "clean-temp") ok = await CleanTempAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Message = ToolText.Get("repairCancelled");
            await VerifyAsync(before, false, CancellationToken.None, true);
            return false;
        }
        if (ok is null) return null;
        await VerifyAsync(before, ok.Value, token.IsCancellationRequested ? CancellationToken.None : token, token.IsCancellationRequested);
        return ok;
    }

    private async Task VerifyAsync(IReadOnlyList<CheckResult> before, bool repairOk, CancellationToken token, bool cancelled = false)
    {
        var lines = new List<string>();
        var comparisons = new List<CheckComparison>();
        foreach (var old in before)
        {
            var after = await _fix.RecheckAsync(old.Id, token);
            Rows.FirstOrDefault(x => x.Id == old.Id)?.Apply(after);
            var verdict = BeforeAfter.Compare(old, after, !repairOk, cancelled);
            comparisons.Add(verdict);
            var index = _last.FindIndex(r => r.Id == old.Id);
            if (index >= 0) _last[index] = after;
            var execution = ToolText.Get("execution_" + verdict.Execution.ToString().ToLowerInvariant());
            lines.Add($"{ToolText.Get("chk_" + old.Id)}: {ToolText.Get("vd_" + verdict.Verdict.ToString().ToLowerInvariant())} · {execution} ({old.Message} → {after.Message})");
        }
        RebuildFindings();
        Compare = string.Join("\n", lines);
        _host.AddHistory(new HistoryRecord
        {
            Kind = HistoryKind.Fix,
            Title = ToolText.Get("compareTitle"),
            Success = comparisons.Count > 0 && comparisons.All(c => c.Verdict == Verdict.Resolved && c.Execution == RepairExecution.Succeeded),
            Summary = ToolText.Get("compareTitle"),
            Detail = Compare,
        });
    }

    private void RebuildFindings()
    {
        Findings.Clear();
        foreach (var finding in RootCauseAnalyzer.Analyze(_last)) Findings.Add(new FindingRow(finding));
        Notify(nameof(HasFindings));
    }

    private void RebuildRecurringIssues(IReadOnlyList<CheckResult> current)
    {
        RecurringIssues.Clear();
        var unresolved = current.Where(r => r.Status is CheckStatus.Problem or CheckStatus.Failed).ToList();
        var prior = _host.History.All()
            .Where(record => record.Kind == HistoryKind.Fix && record.ErrorType is not null)
            .ToList();
        foreach (var result in unresolved)
        {
            var matches = prior.Where(record => record.ErrorType!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(result.Id, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(record => record.Time)
                .ToList();
            if (matches.Count == 0) continue;
            RecurringIssues.Add(new InfoRow(
                ToolText.Get("chk_" + result.Id),
                ToolText.F("recurringDetail", matches.Count + 1, Fmt.Time(matches[0].Time)),
                true));
        }
        Notify(nameof(HasRecurringIssues));
    }

    private async Task ExportAsync()
    {
        if (_last.Count == 0) { Message = ToolText.Get("exportNeedScan"); return; }
        var path = Dialogs.Save("HTML|*.html", $"NIVEX-report-{DateTime.Now:yyyyMMdd-HHmm}.html");
        if (path is null) return;
        var errors = _errors.ToList();
        var labels = new FixReport.Labels(
            ToolText.Get("reportTitle"), ToolText.Get("reportGenerated"), "", ToolText.Get("analysisTitle"), ToolText.Get("adviceLabel"),
            ToolText.Get("reportOpen"), ToolText.Get("reportChecks"), ToolText.Get("errorsTitle"), ToolText.Get("reportNote"),
            r => ToolText.Get("chk_" + r.Id), s => ToolText.Get("st_" + s.ToString().ToLowerInvariant()),
            f => ToolText.Get("fnd_" + f.CheckId), c => ToolText.Get("cert_" + c.ToString().ToLowerInvariant()),
            f => ToolText.Get("fnd_" + f.CheckId + "_adv"));
        var html = FixReport.Html(labels, _last, RootCauseAnalyzer.Analyze(_last), errors, _version, DateTimeOffset.Now);
        try
        {
            await File.WriteAllTextAsync(path, html);
            Message = ToolText.F("exportDone", Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Message = ToolText.Get("exportFailed");
        }
    }

    private async Task LoadErrorsAsync()
    {
        Busy = true;
        _cts = new CancellationTokenSource();
        DiagNote = ToolText.Get("loading");
        try
        {
            _errors = (await _crash.ReadAsync(15, _cts.Token)).ToList();
            var dumps = await Task.Run(CrashLab.ReadDumps, _cts.Token);
            Errors.Clear();
            foreach (var e in _errors.Take(30))
            {
                var facts = new[] { e.Application, e.ErrorCode, e.Module }.Where(x => !string.IsNullOrWhiteSpace(x));
                var detail = $"{e.Log} · {Fmt.Time(e.Time)} · {ToolText.F("errorRepeats", e.RepeatCount)}";
                var factText = string.Join(" · ", facts);
                if (factText.Length > 0) detail += "\n" + factText;
                if (!string.IsNullOrWhiteSpace(e.Message)) detail += "\n" + e.Message;
                Errors.Add(new InfoRow($"{e.Provider} · {e.EventId}", detail, false));
            }
            foreach (var d in dumps.Take(5)) Errors.Add(new InfoRow(Path.GetFileName(d.Path), $"{Fmt.Bytes(d.Size)} · {Fmt.Time(d.Created)}", false));
            DiagNote = ToolText.F("errorsCount", _errors.Count, dumps.Count);
        }
        catch (OperationCanceledException)
        {
            DiagNote = ToolText.Get("scanCancelled");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException)
        {
            DiagNote = ToolText.Get("errorsUnavailable");
        }
        finally
        {
            Busy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task FindLargeAsync()
    {
        var folder = Dialogs.OpenFolder();
        if (folder is null) return;
        Busy = true;
        DiagNote = ToolText.Get("loading");
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(count => DiagNote = ToolText.F("largeProgress", count));
            var scan = await Task.Run(() => LargeFileFinder.Find(folder, 25, 100L * 1048576, _cts.Token, progress), _cts.Token);
            Large.Clear();
            foreach (var f in scan.Files) Large.Add(new InfoRow(Path.GetFileName(f.Path), $"{Fmt.Bytes(f.Size)} · {f.Path}", false));
            DiagNote = ToolText.F("largeCount", scan.Files.Count, scan.ScannedFiles, scan.Inaccessible);
        }
        catch (OperationCanceledException)
        {
            DiagNote = ToolText.Get("scanCancelled");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            DiagNote = ToolText.Get("largeFailed");
        }
        finally
        {
            Busy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    public async Task<bool?> FlushDnsAsync(FixRow? row, CancellationToken token)
    {
        if (!await _host.Confirm.AskAsync(ToolText.Get("flushTitle"), ToolText.Get("flushBody"))) return null;
        var result = await _fix.FlushDnsAsync(token);
        row?.Apply(new CheckResult(row.Id, result.Status, result.Message));
        Message = $"{ToolText.Get("flushTitle")}: {ToolText.Get("st_" + result.Status.ToString().ToLowerInvariant())}";
        _host.AddHistory(new HistoryRecord
        {
            Kind = HistoryKind.Repair,
            Title = ToolText.Get("flushTitle"),
            Success = result.Status == CheckStatus.Repaired,
            Summary = Message,
            ErrorType = result.Status == CheckStatus.Repaired ? null : "repair_failed",
            Detail = result.Message,
        });
        return result.Status == CheckStatus.Repaired;
    }

    private async Task<bool?> CleanTempAsync(CancellationToken token)
    {
        var plan = await Task.Run(() => _fix.PlanTempCleanup(token), token);
        if (plan.Files.Count == 0)
        {
            Message = ToolText.Get("cleanNothing");
            return null;
        }
        var examples = string.Join("\n", plan.Files.Take(6).Select(f => Path.GetFileName(f.Path)));
        var body = ToolText.F("cleanBody", plan.Files.Count, Fmt.Bytes(plan.TotalBytes), examples, plan.InaccessibleDirectories);
        if (!await _host.Confirm.AskAsync(ToolText.Get("cleanTitle"), body)) return null;
        var result = await Task.Run(() => _fix.ExecuteTempCleanup(plan, null, token), token);
        Message = ToolText.F("cleanDone", result.Deleted, Fmt.Bytes(result.LogicalBytesDeleted), result.Skipped);
        _host.AddHistory(new HistoryRecord
        {
            Kind = HistoryKind.Repair,
            Title = ToolText.Get("cleanTitle"),
            Success = result.Deleted > 0 || result.Skipped == 0,
            Summary = Message,
            ErrorType = result.Cancelled ? "cancelled" : null,
        });
        return !result.Cancelled;
    }
}

public sealed class InfoRow(string title, string detail, bool warn)
{
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public Brush Brush { get; } = warn ? Fmt.Resource("Danger") : Fmt.Resource("Ink");
}

public sealed class FindingRow(Finding finding)
{
    public string Title => ToolText.Get("fnd_" + finding.CheckId);
    public string Certainty => ToolText.Get("cert_" + finding.Certainty.ToString().ToLowerInvariant());
    public string Evidence => finding.Evidence;
    public string Advice => finding.SuggestedNextStep ?? ToolText.Get("fnd_" + finding.CheckId + "_adv");
    public string Risk => ToolText.Get("risk_" + finding.Risk.ToString().ToLowerInvariant());
    public string Related => finding.RelatedIds.Length == 0 ? "" : string.Join(", ", finding.RelatedIds.Select(id => ToolText.Get("chk_" + id)));
    public bool HasRelated => finding.RelatedIds.Length > 0;
}

public sealed class MonitorVm : ToolVm
{
    private readonly MonitorService _service;
    private readonly ToolkitModel _host;
    private readonly Dispatcher _dispatcher;
    private MonitorSample? _latest;
    private string _error = "";
    private string _message = "";
    private double[] _cpu = [];
    private double[] _mem = [];
    private double[] _disk = [];
    private double[] _gpu = [];

    public MonitorVm(MonitorService service, ToolkitModel host, Dispatcher dispatcher)
    {
        _service = service;
        _host = host;
        _dispatcher = dispatcher;
        _service.Sampled += sample => _dispatcher.BeginInvoke(() => Apply(sample));
        _service.Failed += error => _dispatcher.BeginInvoke(() => { Error = error; Notify(nameof(StateText)); });
        SaveCommand = new AsyncCommand(_ => SaveAsync());
    }

    public ICommand SaveCommand { get; }
    public double[] CpuSeries { get => _cpu; private set => Set(ref _cpu, value); }
    public double[] MemSeries { get => _mem; private set => Set(ref _mem, value); }
    public double[] DiskSeries { get => _disk; private set => Set(ref _disk, value); }
    public double[] GpuSeries { get => _gpu; private set => Set(ref _gpu, value); }
    public string Error { get => _error; private set { Set(ref _error, value); Notify(nameof(HasError)); } }
    public bool HasError => _error.Length > 0;
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string StateText => ToolText.Get(_service.IsRunning ? "monRunning" : "monStopped");
    public string UpdatedText => _service.LastSampleAt is { } at ? Fmt.Time(at) + at.ToLocalTime().ToString(":ss") : "—";

    public string CpuText => _latest is null ? Measuring : _latest.Raw.CpuPending ? Measuring : Percent(_latest.Cpu);
    public string MemText => _latest?.MemoryPercent is null ? Unavailable : $"{Fmt.Pct(_latest.MemoryPercent)} · {Fmt.Gb(_latest.Raw.MemoryUsedBytes)} / {Fmt.Gb(_latest.Raw.MemoryTotalBytes)}";
    public string DiskText => _latest?.DiskPercent is null ? Unavailable : $"{Fmt.Pct(_latest.DiskPercent)} · {Fmt.Gb(_latest.Raw.DiskUsedBytes)} / {Fmt.Gb(_latest.Raw.DiskTotalBytes)}";

    public string GpuText
    {
        get
        {
            if (_latest is null) return Measuring;
            if (_latest.Gpu is null) return _latest.Raw.GpuAvailable ? Measuring : Unavailable;
            return string.IsNullOrWhiteSpace(_latest.Raw.GpuName) ? Fmt.Pct(_latest.Gpu) : $"{Fmt.Pct(_latest.Gpu)} · {_latest.Raw.GpuName}";
        }
    }

    public string NetText => _latest?.Raw.NetworkConnected switch
    {
        true => ToolText.Get("connected"),
        false => ToolText.Get("disconnected"),
        _ => Unavailable,
    };

    private static string Unavailable => ToolText.Get("unavailable");
    private static string Measuring => ToolText.Get("measuring");
    private string Percent(double? value) => value is null ? Unavailable : Fmt.Pct(value);

    public void Start(int seconds)
    {
        _service.Start(TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 30)));
        Notify(nameof(StateText));
    }

    public void Stop()
    {
        _service.Stop();
        Notify(nameof(StateText));
    }

    private void Apply(MonitorSample sample)
    {
        _latest = sample;
        var history = _service.History();
        CpuSeries = history.Cpu;
        MemSeries = history.Memory;
        DiskSeries = history.Disk;
        GpuSeries = history.Gpu;
        Error = "";
        RefreshAll();
    }

    private Task SaveAsync()
    {
        var average = _service.Averages();
        if (average.Samples == 0 || (average.Cpu is null && average.Memory is null && average.Disk is null))
        {
            Message = ToolText.Get("monNoData");
            return Task.CompletedTask;
        }
        var summary = ToolText.F("monSummary", Fmt.Pct(average.Cpu), Fmt.Pct(average.Memory), Fmt.Pct(average.Disk), average.Samples);
        _host.AddHistory(new HistoryRecord
        {
            Kind = HistoryKind.Monitor,
            Title = ToolText.Get("monSummaryTitle"),
            Success = true,
            Summary = summary,
        });
        Message = ToolText.Get("monSaved");
        return Task.CompletedTask;
    }

    protected override void OnLanguage() => RefreshAll();
}

public sealed class NetworkVm : ToolVm
{
    private readonly NetworkLab _lab;
    private readonly ToolkitModel _host;
    private readonly string _lastPath;
    private NetworkReport? _previous;
    private string _hostName = "8.8.8.8";
    private string _count = "4";
    private string _timeout = "2000";
    private string _domain = "www.microsoft.com";
    private string _message = "";
    private bool _error;
    private bool _busy;

    public NetworkVm(NetworkLab lab, ToolkitModel host, string lastPath)
    {
        _lab = lab;
        _host = host;
        _lastPath = lastPath;
        _previous = LoadPrevious();
        DiagnoseCommand = new AsyncCommand(_ => RunAsync(() => DiagnoseAsync(CancellationToken.None, false)), _ => !Busy);
        PingCommand = new AsyncCommand(_ => RunAsync(() => PingAsync(CancellationToken.None)), _ => !Busy);
        DnsCommand = new AsyncCommand(_ => RunAsync(() => DnsAsync(CancellationToken.None)), _ => !Busy);
        FlushCommand = new AsyncCommand(_ => RunAsync(FlushAndRetestAsync), _ => !Busy);
        OpenFixCommand = new AsyncCommand(_ => { _host.Navigate("Fix"); return Task.CompletedTask; });
    }

    public ObservableCollection<Row> Results { get; } = new();
    public ObservableCollection<Row> Issues { get; } = new();
    public ObservableCollection<Row> Compare { get; } = new();
    public ICommand DiagnoseCommand { get; }
    public ICommand PingCommand { get; }
    public ICommand DnsCommand { get; }
    public ICommand FlushCommand { get; }
    public ICommand OpenFixCommand { get; }
    public string HostName { get => _hostName; set => Set(ref _hostName, value); }
    public string Count { get => _count; set => Set(ref _count, value); }
    public string Timeout { get => _timeout; set => Set(ref _timeout, value); }
    public string Domain { get => _domain; set => Set(ref _domain, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsError { get => _error; private set => Set(ref _error, value); }
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Notify(nameof(Idle)); } }
    public bool Idle => !_busy;
    public bool HasIssues => Issues.Count > 0;
    public bool HasCompare => Compare.Count > 0;
    public bool HasResults => Results.Count > 0;

    private NetworkReport? LoadPrevious()
    {
        try
        {
            return File.Exists(_lastPath) ? JsonSerializer.Deserialize<NetworkReport>(File.ReadAllText(_lastPath)) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveLast(NetworkReport report)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_lastPath)!);
            File.WriteAllText(_lastPath, JsonSerializer.Serialize(report));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        Busy = true;
        IsError = false;
        Message = ToolText.Get("netRunning");
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            IsError = true;
            Message = exception.GetType().Name;
        }
        finally
        {
            Busy = false;
        }
    }

    private void ResetOutput()
    {
        Results.Clear();
        Issues.Clear();
        Compare.Clear();
        Notify(nameof(HasIssues));
        Notify(nameof(HasCompare));
        Notify(nameof(HasResults));
    }

    private static string PingText(PingStats s)
    {
        if (s.Received == 0) return $"{s.Host}: {ToolText.Get("netNoReply")} · 100%";
        return ToolText.F("netPingResult", s.Host, Fmt.Ms(s.MinMs), Fmt.Ms(s.AvgMs), Fmt.Ms(s.MaxMs), s.LossPercent, s.Received, s.Sent);
    }

    private static string PingNote(PingStats s) =>
        s.Received == 0 ? ToolText.Get("netLimit") : s.LossPercent > 0 ? ToolText.Get("netPartial") : "";

    private static string DnsText(DnsResult d) => d.Ok
        ? ToolText.F("netDnsOk", d.Domain, string.Join(", ", d.Addresses.Take(3)), d.Ms)
        : ToolText.F("netDnsFail", d.Domain, d.Error ?? "");

    private async Task DiagnoseAsync(CancellationToken token, bool afterFlush)
    {
        if (!NetworkLab.TryValidateHost(Domain, out var domain, out _))
        {
            IsError = true;
            Message = ToolText.Get("netBadHost");
            return;
        }
        ResetOutput();
        var report = await _lab.DiagnoseAsync(domain, token);
        foreach (var a in report.Adapters.Where(a => a.Up))
            Results.Add(new Row($"{ToolText.Get("netAdapters")}: {a.Name} ({a.Kind})",
                string.Join(" · ", a.Addresses.Concat(a.Gateways).Concat(a.DnsServers))));
        Results.Add(report.Gateway is null
            ? new Row($"{ToolText.Get("netGateway")}: {ToolText.Get("netNoGateway")}")
            : new Row($"{ToolText.Get("netGateway")} {PingText(report.Gateway)}", PingNote(report.Gateway)));
        foreach (var e in report.External) Results.Add(new Row(PingText(e), PingNote(e)));
        Results.Add(new Row(DnsText(report.Dns)));
        foreach (var issue in report.Issues) Issues.Add(new Row(ToolText.Get("netIssue_" + issue.Code)));
        foreach (var line in NetworkLab.Compare(_previous, report)) Compare.Add(new Row(line.Metric, $"{line.Previous}  →  {line.Current}"));
        _previous = report;
        SaveLast(report);
        Notify(nameof(HasIssues));
        Notify(nameof(HasCompare));
        Notify(nameof(HasResults));
        Message = ToolText.Get("netDone");
        _host.AddHistory(new HistoryRecord
        {
            Kind = HistoryKind.Network,
            Title = ToolText.Get(afterFlush ? "netFlushRetest" : "netDiagnose"),
            Success = report.Issues.Count == 0,
            Summary = report.Issues.Count == 0 ? ToolText.Get("netNoIssues") : string.Join(" / ", report.Issues.Select(i => ToolText.Get("netIssue_" + i.Code))),
            ErrorType = report.Issues.Count == 0 ? null : string.Join(",", report.Issues.Select(i => i.Code)),
            Detail = string.Join("\n", Results.Select(r => r.Primary)),
        });
    }

    private async Task PingAsync(CancellationToken token)
    {
        if (!NetworkLab.TryValidateHost(HostName, out var host, out _))
        {
            IsError = true;
            Message = ToolText.Get("netBadHost");
            return;
        }
        if (!int.TryParse(Count, out var count) || count is < 1 or > 10 || !int.TryParse(Timeout, out var timeout) || timeout is < 200 or > 5000)
        {
            IsError = true;
            Message = ToolText.Get("netBadNumber");
            return;
        }
        ResetOutput();
        var stats = await _lab.PingAsync(host, count, timeout, token);
        Results.Add(new Row(PingText(stats), PingNote(stats)));
        Notify(nameof(HasResults));
        Message = ToolText.Get("netDone");
        _host.AddHistory(new HistoryRecord
        {
            Kind = HistoryKind.Network,
            Title = ToolText.Get("netPing"),
            Success = stats.Received > 0 && stats.LossPercent == 0,
            Summary = PingText(stats),
            ErrorType = stats.Received == 0 ? "no_reply" : stats.LossPercent > 0 ? "packet_loss" : null,
        });
    }

    private async Task DnsAsync(CancellationToken token)
    {
        if (!NetworkLab.TryValidateHost(Domain, out var domain, out _))
        {
            IsError = true;
            Message = ToolText.Get("netBadHost");
            return;
        }
        ResetOutput();
        var result = await _lab.DnsAsync(domain, token);
        Results.Add(new Row(DnsText(result)));
        Notify(nameof(HasResults));
        Message = ToolText.Get("netDone");
        _host.AddHistory(new HistoryRecord
        {
            Kind = HistoryKind.Network,
            Title = ToolText.Get("netDns"),
            Success = result.Ok,
            Summary = DnsText(result),
            ErrorType = result.Ok ? null : "dns_failed",
        });
    }

    private async Task FlushAndRetestAsync()
    {
        var ok = await _host.Fix.FlushDnsAsync(null, CancellationToken.None);
        if (ok is null)
        {
            Message = "";
            return;
        }
        if (ok == false)
        {
            Message = ToolText.Get("st_repairfailed");
            IsError = true;
            return;
        }
        await DiagnoseAsync(CancellationToken.None, true);
    }

    protected override void OnLanguage() => RefreshAll();
}
