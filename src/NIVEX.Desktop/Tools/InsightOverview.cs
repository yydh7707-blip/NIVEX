using System.Collections.ObjectModel;
using System.IO;
using NIVEX.Toolkit;

namespace NIVEX.Desktop;

public sealed class RecordRow : Observable
{
    private bool _open;

    public RecordRow(HistoryRecord record)
    {
        Record = record;
        ToggleCommand = new AsyncCommand(_ => { IsOpen = !IsOpen; return Task.CompletedTask; });
    }

    public HistoryRecord Record { get; }
    public ICommand ToggleCommand { get; }
    public bool IsOpen { get => _open; set => Set(ref _open, value); }
    public string TimeText => Fmt.Time(Record.Time);
    public string KindText => ToolText.Get("kind_" + Record.Kind);
    public string Title => Record.Title;
    public string Summary => Record.Summary;
    public string Glyph => Record.Success ? "✓" : "!";
    public System.Windows.Media.Brush Brush => Record.Success ? Fmt.Resource("Ink") : Fmt.Resource("Danger");
    public string Detail => Record.Detail ?? "";
    public bool HasDetail => !string.IsNullOrWhiteSpace(Record.Detail);
    public string Advice => Record.Advice ?? "";
    public bool HasAdvice => !string.IsNullOrWhiteSpace(Record.Advice);
    public string ErrorType => Record.ErrorType ?? "";
    public bool HasError => !string.IsNullOrWhiteSpace(Record.ErrorType);
    public void Refresh() => Notify(null);
}

public sealed class InsightVm : ToolVm
{
    private readonly ToolkitModel _host;
    private string _period = "all";
    private string _kind = "all";
    private string _result = "all";
    private string _keyword = "";
    private string _message = "";
    private bool _error;
    private bool _anyRecords;

    public InsightVm(ToolkitModel host)
    {
        _host = host;
        ExportCommand = new AsyncCommand(_ => ExportAsync());
        ClearCommand = new AsyncCommand(_ => ClearAsync(false), _ => _anyRecords);
        ClearOldCommand = new AsyncCommand(_ => ClearAsync(true), _ => _anyRecords);
    }

    public ObservableCollection<RecordRow> Rows { get; } = new();
    public ICommand ExportCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ClearOldCommand { get; }
    public string Period { get => _period; set { if (_period != value) { _period = value; Notify(nameof(Period)); Refresh(); } } }
    public string Kind { get => _kind; set { if (_kind != value) { _kind = value; Notify(nameof(Kind)); Refresh(); } } }
    public string Result { get => _result; set { if (_result != value) { _result = value; Notify(nameof(Result)); Refresh(); } } }
    public string Keyword { get => _keyword; set { if (_keyword != value) { _keyword = value; Notify(nameof(Keyword)); Refresh(); } } }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsError { get => _error; private set => Set(ref _error, value); }
    public bool IsEmpty => Rows.Count == 0;
    public string EmptyText => ToolText.Get(_anyRecords ? "insNoMatch" : "insEmpty");
    public string CountText => ToolText.F("insCount", Rows.Count);

    protected override void OnLanguage()
    {
        foreach (var row in Rows) row.Refresh();
        RefreshAll();
    }

    private (DateTimeOffset From, DateTimeOffset To) Range()
    {
        var now = DateTimeOffset.Now;
        return Period switch
        {
            "today" => (new DateTimeOffset(now.Date, now.Offset), now),
            "7" => (now.AddDays(-7), now),
            "30" => (now.AddDays(-30), now),
            _ => (DateTimeOffset.MinValue, now),
        };
    }

    private List<HistoryRecord> Filtered()
    {
        var (from, to) = Range();
        HistoryKind? kind = Enum.TryParse<HistoryKind>(Kind, out var parsed) ? parsed : null;
        bool? success = Result switch { "ok" => true, "bad" => false, _ => null };
        return HistoryStore.Query(_host.History.All(), Period == "all" ? null : from, Period == "all" ? null : to, kind, success,
            string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim())
            .OrderByDescending(r => r.Time).ToList();
    }

    public void Refresh()
    {
        _anyRecords = _host.History.All().Count > 0;
        Rows.Clear();
        foreach (var record in Filtered().Take(300)) Rows.Add(new RecordRow(record));
        if (_host.History.RecoveredFromCorruption && Message.Length == 0)
        {
            IsError = true;
            Message = ToolText.Get("insRecovered");
        }
        Notify(nameof(IsEmpty));
        Notify(nameof(EmptyText));
        Notify(nameof(CountText));
    }

    private Task ExportAsync()
    {
        var records = Filtered();
        IsError = true;
        if (records.Count == 0)
        {
            Message = ToolText.Get("insNoReport");
            return Task.CompletedTask;
        }
        var path = Dialogs.Save("HTML|*.html", $"NIVEX-report-{DateTime.Now:yyyyMMdd-HHmm}.html");
        if (path is null) return Task.CompletedTask;
        var (from, to) = Range();
        if (Period == "all") from = records.Min(r => r.Time);
        var html = ReportBuilder.BuildHtml(records, ToolText.Get("insReportTitle"), from, to, DateTimeOffset.Now, ToolText.Instance.Korean);
        var (ok, error) = ReportWriter.Save(path, html, true);
        IsError = !ok;
        Message = ok ? ToolText.Get("insExported") : ToolText.F("insExportFail", error ?? "");
        return Task.CompletedTask;
    }

    private async Task ClearAsync(bool olderOnly)
    {
        var title = ToolText.Get("insClearTitle");
        var body = ToolText.Get(olderOnly ? "insClearOldBody" : "insClearBody");
        if (!await _host.Confirm.AskAsync(title, body, ToolText.Get("delete"))) return;
        var before = _host.History.All().Count;
        if (olderOnly) _host.History.RemoveOlderThan(DateTimeOffset.Now.AddDays(-30));
        else _host.History.Clear();
        var removed = before - _host.History.All().Count;
        IsError = false;
        Message = ToolText.F("insCleared", removed);
        _host.NotifyHistoryChanged();
    }
}

public sealed class OverviewRow(string section, string titleKey) : Observable
{
    private string _value = "";
    private string _detail = "";
    private bool _ok = true;

    public string Section { get; } = section;
    public string Title => ToolText.Get(titleKey);
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public System.Windows.Media.Brush ValueBrush => _ok ? Fmt.Resource("Ink") : Fmt.Resource("Danger");

    public void Update(string value, string detail, bool ok)
    {
        _ok = ok;
        Value = value;
        Detail = detail;
        Notify(nameof(ValueBrush));
    }

    public void Refresh() => Notify(nameof(Title));
}

public sealed class OverviewVm : ToolVm
{
    private readonly ToolkitModel _host;

    public OverviewVm(ToolkitModel host)
    {
        _host = host;
        OpenCommand = new AsyncCommand(p => { if (p is string section) _host.Navigate(section); return Task.CompletedTask; });
        Rows = [new("Fix", "ovFix"), new("Network", "ovNetwork"), new("Monitor", "ovMonitor"), new("Flow", "ovFlow"), new("Insight", "ovInsight")];
    }

    public IReadOnlyList<OverviewRow> Rows { get; }
    public ICommand OpenCommand { get; }

    protected override void OnLanguage()
    {
        foreach (var row in Rows) row.Refresh();
        Refresh();
        RefreshAll();
    }

    public void Refresh()
    {
        var all = _host.History.All();
        Fill(Rows[0], all.Where(r => r.Kind == HistoryKind.Fix).MaxBy(r => r.Time));
        Fill(Rows[1], all.Where(r => r.Kind == HistoryKind.Network).MaxBy(r => r.Time));
        Fill(Rows[2], all.Where(r => r.Kind == HistoryKind.Monitor).MaxBy(r => r.Time));
        Fill(Rows[3], all.Where(r => r.Kind is HistoryKind.Flow or HistoryKind.Profile).MaxBy(r => r.Time));
        Fill(Rows[4], all.MaxBy(r => r.Time));
    }

    private static void Fill(OverviewRow row, HistoryRecord? record)
    {
        if (record is null) row.Update(ToolText.Get("ovEmpty"), "", true);
        else row.Update(record.Title + " · " + Fmt.Time(record.Time), record.Summary, record.Success);
    }
}
