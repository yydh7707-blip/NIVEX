using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using NIVEX.Toolkit;

namespace NIVEX.Desktop;

public sealed class StartupRow : Observable
{
    private readonly Func<StartupRow, Task> _toggle;
    private bool _enabled;
    private readonly bool _stateKnown;

    public StartupRow(StartupEntry entry, bool enabled, bool isProtected, bool stateKnown, Func<StartupRow, Task> toggle)
    {
        Entry = entry;
        _enabled = enabled;
        IsProtected = isProtected;
        _stateKnown = stateKnown;
        _toggle = toggle;
        ToggleCommand = new AsyncCommand(_ => _toggle(this), _ => CanToggle);
    }

    public StartupEntry Entry { get; }
    public bool IsProtected { get; }
    public ICommand ToggleCommand { get; }
    public string Name => Entry.Name;
    public bool Enabled { get => _enabled; set { Set(ref _enabled, value); Notify(nameof(State)); Notify(nameof(ToggleText)); } }
    public bool CanToggle => !IsProtected && _stateKnown;
    public string State => IsProtected ? ToolText.Get("stProtected") : !_stateKnown ? ToolText.Get("stUnknown") : ToolText.Get(_enabled ? "stOn" : "stOff");
    public string ToggleText => ToolText.Get(_enabled ? "disableBtn" : "enableBtn");
    public void Refresh() => Notify(null);
    public string Detail => $"{Entry.Location} · {ToolText.Get(Entry.PathExists ? "pathOk" : "pathMissing")}";
}

public sealed class CheckToggle(string id, bool on, Action changed) : Observable
{
    private bool _on = on;
    public string Id { get; } = id;
    public string Title => ToolText.Get("chk_" + Id);
    public bool IsOn { get => _on; set { if (_on == value) return; _on = value; Notify(nameof(IsOn)); changed(); } }
}

public sealed class FixExtrasVm : ToolVm
{
    private readonly FixService _fix;
    private readonly ToolkitModel _host;
    private readonly bool _isAdmin;
    private readonly ProcessRunner _runner = new();
    private readonly StartupJournal _journal;
    private readonly MaintProfileStore _store;
    private readonly string _schedulePath;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _loading;
    private bool _scheduleOn;
    private DateTime _lastRun = DateTime.MinValue;
    private MaintProfile? _selected;
    private string _note = "";
    private string _newName = "";
    private string _interval = "24";

    public FixExtrasVm(FixService fix, ToolkitModel host, bool isAdmin, string dataDir)
    {
        _fix = fix;
        _host = host;
        _isAdmin = isAdmin;
        _journal = new StartupJournal(Path.Combine(dataDir, "startup-changes.json"));
        _store = new MaintProfileStore(Path.Combine(dataDir, "maintenance-profiles.json"));
        _schedulePath = Path.Combine(dataDir, "schedule.json");
        foreach (var p in _store.Load()) Profiles.Add(p);
        LoadSchedule();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == _scheduleProfile) ?? Profiles.FirstOrDefault();

        LoadStartupCommand = new AsyncCommand(_ => LoadStartupAsync(), _ => !Busy);
        UndoCommand = new AsyncCommand(_ => UndoAsync(), _ => !Busy);
        DevicesCommand = new AsyncCommand(_ => LoadDevicesAsync(), _ => !Busy);
        DeviceManagerCommand = new AsyncCommand(_ => { Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }); return Task.CompletedTask; });
        NetRepairCommand = new AsyncCommand(p => NetRepairAsync(p as string ?? ""), _ => !Busy);
        AppCheckCommand = new AsyncCommand(_ => AppCheckAsync(), _ => !Busy);
        NewProfileCommand = new AsyncCommand(_ => NewProfile(), _ => !Busy);
        DuplicateCommand = new AsyncCommand(_ => Duplicate(), _ => !Busy && SelectedProfile is not null);
        DeleteProfileCommand = new AsyncCommand(_ => DeleteProfile(), _ => !Busy && Profiles.Count > 1 && SelectedProfile is not null);
        RunProfileCommand = new AsyncCommand(_ => RunProfileAsync(false), _ => !Busy && SelectedProfile is not null);
        SelectProfileCommand = new AsyncCommand(p => { if (p is MaintProfile m) SelectedProfile = m; return Task.CompletedTask; });
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    private string _scheduleProfile = "";

    public ObservableCollection<StartupRow> Startup { get; } = new();
    public ObservableCollection<InfoRow> Devices { get; } = new();
    public ObservableCollection<InfoRow> AppRows { get; } = new();
    public ObservableCollection<InfoRow> ProfileResults { get; } = new();
    public ObservableCollection<MaintProfile> Profiles { get; } = new();
    public ObservableCollection<CheckToggle> ProfileChecks { get; } = new();
    public ICommand LoadStartupCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand DevicesCommand { get; }
    public ICommand DeviceManagerCommand { get; }
    public ICommand NetRepairCommand { get; }
    public ICommand AppCheckCommand { get; }
    public ICommand NewProfileCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand RunProfileCommand { get; }
    public ICommand SelectProfileCommand { get; }
    public string SelectedName => SelectedProfile?.Name ?? "—";
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public string Note { get => _note; private set => Set(ref _note, value); }
    public string NewName { get => _newName; set => Set(ref _newName, value); }

    public string Interval
    {
        get => _interval;
        set
        {
            Set(ref _interval, value);
            SaveSelected();
        }
    }

    public MaintProfile? SelectedProfile
    {
        get => _selected;
        set
        {
            if (_selected == value || (value is null && _loading)) return;
            _selected = value;
            Notify(nameof(SelectedProfile));
            Notify(nameof(SelectedName));
            LoadSelected();
        }
    }

    public bool ScheduleOn
    {
        get => _scheduleOn;
        set
        {
            if (_scheduleOn == value) return;
            _scheduleOn = value;
            if (value) _lastRun = DateTime.Now;
            Notify(nameof(ScheduleOn));
            Notify(nameof(ScheduleNote));
            SaveSchedule();
        }
    }

    public string ScheduleNote => _scheduleOn && SelectedProfile is { } p
        ? ToolText.F("scheduleOnNote", _lastRun.AddHours(p.IntervalHours).ToString("yyyy-MM-dd HH:mm"))
        : ToolText.Get("scheduleOffNote");

    protected override void OnLanguage()
    {
        foreach (var row in Startup) row.Refresh();
        RefreshAll();
    }

    private void LoadSelected()
    {
        _loading = true;
        ProfileChecks.Clear();
        if (_selected is { } p)
        {
            foreach (var id in FixService.AllIds) ProfileChecks.Add(new CheckToggle(id, p.CheckIds.Contains(id), SaveSelected));
            _interval = p.IntervalHours.ToString();
            Notify(nameof(Interval));
        }
        _loading = false;
        Notify(nameof(ScheduleNote));
    }

    private void SaveSelected()
    {
        if (_loading || _selected is null) return;
        var ids = ProfileChecks.Where(c => c.IsOn).Select(c => c.Id).ToArray();
        if (ids.Length == 0 || !int.TryParse(_interval, out var hours)) return;
        var updated = _selected with { CheckIds = ids, IntervalHours = Math.Clamp(hours, 1, 168) };
        var index = Profiles.IndexOf(_selected);
        if (index < 0 || updated == _selected) return;
        _loading = true;
        Profiles[index] = updated;
        _selected = updated;
        _loading = false;
        Notify(nameof(SelectedProfile));
        _store.Save(Profiles);
        Notify(nameof(ScheduleNote));
    }

    private Task NewProfile()
    {
        var name = NewName.Trim();
        if (name.Length == 0 || Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Note = ToolText.Get("profNameBad"); return Task.CompletedTask; }
        var profile = new MaintProfile(name[..Math.Min(40, name.Length)], [.. FixService.QuickIds], 24);
        Profiles.Add(profile);
        _store.Save(Profiles);
        SelectedProfile = profile;
        NewName = "";
        Note = ToolText.Get("profSaved");
        return Task.CompletedTask;
    }

    private Task Duplicate()
    {
        if (SelectedProfile is null) return Task.CompletedTask;
        var copy = MaintProfileStore.Duplicate(SelectedProfile, Profiles);
        Profiles.Add(copy);
        _store.Save(Profiles);
        SelectedProfile = copy;
        Note = ToolText.Get("profSaved");
        return Task.CompletedTask;
    }

    private async Task DeleteProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1) return;
        if (!await _host.Confirm.AskAsync(ToolText.Get("profDeleteTitle"), SelectedProfile.Name)) return;
        var gone = SelectedProfile;
        SelectedProfile = Profiles.First(p => p != gone);
        Profiles.Remove(gone);
        _store.Save(Profiles);
        Note = ToolText.Get("profSaved");
    }

    private async Task RunProfileAsync(bool automatic)
    {
        if (SelectedProfile is not { } profile) return;
        Busy = true;
        _cts = new CancellationTokenSource();
        ProfileResults.Clear();
        try
        {
            var results = await _fix.ScanIdsAsync(profile.CheckIds, null, _cts.Token);
            foreach (var r in results)
                ProfileResults.Add(new InfoRow($"{ToolText.Get("chk_" + r.Id)} · {ToolText.Get("st_" + r.Status.ToString().ToLowerInvariant())}", r.Message,
                    r.Status is CheckStatus.Problem or CheckStatus.Failed));
            _lastRun = DateTime.Now;
            SaveSchedule();
            Notify(nameof(ScheduleNote));
            var problems = results.Count(r => r.Status is CheckStatus.Problem or CheckStatus.Failed);
            Note = ToolText.F("mpRunDone", profile.Name, problems);
            _host.AddHistory(new HistoryRecord
            {
                Kind = HistoryKind.Fix,
                Title = ToolText.Get(automatic ? "scheduledScan" : "profileScan") + " · " + profile.Name,
                Success = problems == 0,
                Summary = Note,
                ErrorType = problems == 0 ? null : string.Join(",", results.Where(r => r.Status is CheckStatus.Problem or CheckStatus.Failed).Select(r => r.Id)),
                Detail = string.Join("\n", results.Select(r => $"{r.Id}: {r.Status} - {r.Message}")),
            });
        }
        catch (OperationCanceledException) { Note = ToolText.Get("scanCancelled"); }
        catch (InvalidOperationException) { Note = ToolText.Get("flowBusy2"); }
        finally
        {
            Busy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task TickAsync()
    {
        if (!_scheduleOn || Busy || _fix.IsScanning || SelectedProfile is not { } p) return;
        if (DateTime.Now < _lastRun.AddHours(p.IntervalHours)) return;
        await RunProfileAsync(true);
    }

    private void LoadSchedule()
    {
        try
        {
            if (!File.Exists(_schedulePath)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_schedulePath));
            var root = doc.RootElement;
            _scheduleOn = root.TryGetProperty("on", out var on) && on.ValueKind == System.Text.Json.JsonValueKind.True;
            _scheduleProfile = root.TryGetProperty("profile", out var pr) ? pr.GetString() ?? "" : "";
            if (root.TryGetProperty("last", out var last) && DateTime.TryParse(last.GetString(), out var parsed)) _lastRun = parsed;
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException) { }
    }

    private void SaveSchedule()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_schedulePath)!);
            File.WriteAllText(_schedulePath, System.Text.Json.JsonSerializer.Serialize(new { on = _scheduleOn, profile = SelectedProfile?.Name ?? "", last = _lastRun.ToString("o") }));
        }
        catch (IOException) { }
    }

    private async Task LoadStartupAsync()
    {
        var entries = await Task.Run(StartupInspector.Read);
        Startup.Clear();
        foreach (var e in entries)
        {
            var known = StartupManager.TryGetEnabled(e.Location, e.Name, out var enabled);
            Startup.Add(new StartupRow(e, enabled, StartupManager.IsProtected(e.Name, e.Command), known, ToggleAsync));
        }
        Note = ToolText.F("startupCount", entries.Count);
    }

    private async Task ToggleAsync(StartupRow row)
    {
        if (row.IsProtected) return;
        if (row.Entry.Location == "HKLM" && !_isAdmin) { Note = ToolText.Get("adv_admin"); return; }
        var target = !row.Enabled;
        var body = ToolText.F(target ? "startupEnableBody" : "startupDisableBody", row.Name);
        if (!await _host.Confirm.AskAsync(ToolText.Get("startupChangeTitle"), body)) return;
        var before = row.Enabled;
        var ok = StartupManager.SetEnabled(row.Entry.Location, row.Name, before, target);
        if (ok)
        {
            row.Enabled = target;
            _journal.Add(new StartupChange(row.Name, row.Entry.Location, before, DateTimeOffset.Now));
        }
        Note = ToolText.Get(ok ? "startupChanged" : "startupFailed");
        _host.AddHistory(new HistoryRecord
        {
            Kind = HistoryKind.Repair,
            Title = ToolText.Get("startupChangeTitle"),
            Success = ok,
            Summary = $"{row.Name}: {before} → {target}",
            ErrorType = ok ? null : "repair_failed",
        });
    }

    private async Task UndoAsync()
    {
        var last = _journal.Load().LastOrDefault();
        if (last is null) { Note = ToolText.Get("undoNone"); return; }
        if (!StartupManager.TryGetEnabled(last.Location, last.Name, out var current))
        {
            Note = ToolText.Get("undoFailed");
            return;
        }
        var target = !last.WasEnabled;
        if (current != target)
        {
            Note = ToolText.Get("undoChanged");
            return;
        }
        if (!await _host.Confirm.AskAsync(ToolText.Get("undoTitle"), ToolText.F("undoBody", last.Name, ToolText.Get(last.WasEnabled ? "stOn" : "stOff")))) return;
        var ok = StartupManager.SetEnabled(last.Location, last.Name, target, last.WasEnabled);
        if (ok)
        {
            _journal.Remove(last);
            var row = Startup.FirstOrDefault(r => r.Name == last.Name && r.Entry.Location == last.Location);
            if (row is not null) row.Enabled = last.WasEnabled;
        }
        Note = ToolText.Get(ok ? "undoDone" : "undoFailed");
        _host.AddHistory(new HistoryRecord { Kind = HistoryKind.Repair, Title = ToolText.Get("undoTitle"), Success = ok, Summary = Note, ErrorType = ok ? null : "repair_failed" });
    }

    private async Task LoadDevicesAsync()
    {
        Busy = true;
        Note = ToolText.Get("loading");
        try
        {
            var list = await DeviceInspector.ReadAsync(_runner, CancellationToken.None);
            Devices.Clear();
            if (list is null) { Note = ToolText.Get("devUnavailable"); return; }
            foreach (var d in list) Devices.Add(new InfoRow(d.Name.Length == 0 ? "?" : d.Name, ToolText.F("devCode", d.Code), true));
            Note = ToolText.F("devCount", list.Count);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Note = ToolText.Get("devUnavailable");
        }
        finally { Busy = false; }
    }

    private async Task NetRepairAsync(string kind)
    {
        if (kind is not ("winsock" or "tcpip")) return;
        var before = string.Join("; ", NetworkLab.ReadAdapters().Where(a => a.Up).Select(a => $"{a.Name} {string.Join(',', a.Addresses)}"));
        var body = ToolText.F("netAdvBody", ToolText.Get("netAdv_" + kind), before.Length == 0 ? "—" : before);
        if (!_isAdmin) { Note = ToolText.Get("adv_admin"); return; }
        if (!await _host.Confirm.AskAsync(ToolText.Get("netAdvTitle"), body)) return;
        Busy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _fix.AdvancedNetworkRepairAsync(kind, _cts.Token);
            var ok = result.Status == CheckStatus.Repaired;
            Note = ToolText.Get(ok ? "netAdvDone" : "netAdvFailed");
            _host.AddHistory(new HistoryRecord
            {
                Kind = HistoryKind.Repair,
                Title = ToolText.Get("netAdvTitle") + " · " + kind,
                Success = ok,
                Summary = Note,
                ErrorType = ok ? null : "repair_failed",
                Detail = $"before: {before}\n{result.Message}",
            });
        }
        finally
        {
            Busy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task AppCheckAsync()
    {
        var path = Dialogs.OpenFile("Programs|*.exe");
        if (path is null) return;
        Busy = true;
        try
        {
            var report = await AppTroubleshooter.InspectAsync(path, _runner, CancellationToken.None);
            AppRows.Clear();
            AppRows.Add(new InfoRow(ToolText.Get("appFile"), $"{Path.GetFileName(report.Path)} · {Fmt.Bytes(report.Size)}", !report.Exists));
            AppRows.Add(new InfoRow(ToolText.Get("appFormat"), ToolText.Get(report.LooksLikeExecutable ? "appFormatOk" : "appFormatBad"), !report.LooksLikeExecutable));
            AppRows.Add(new InfoRow(ToolText.Get("appDisk"), report.DriveFreeBytes is { } free ? Fmt.Bytes(free) : "—", report.DriveFreeBytes is < 1073741824));
            AppRows.Add(new InfoRow(ToolText.Get("appVc"), report.VcRuntimePresent is null ? "—" : ToolText.Get(report.VcRuntimePresent.Value ? "appVcYes" : "appVcNo"), report.VcRuntimePresent == false));
            foreach (var c in report.Crashes)
                AppRows.Add(new InfoRow($"{ToolText.Get("appCrash")} · {c.EventId}", Fmt.Time(c.Time), true));
            Note = ToolText.F("appDone", report.Crashes.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Note = ToolText.Get("appFailed");
        }
        finally { Busy = false; }
    }

    public void Stop()
    {
        _timer.Stop();
        _cts?.Cancel();
    }
}
