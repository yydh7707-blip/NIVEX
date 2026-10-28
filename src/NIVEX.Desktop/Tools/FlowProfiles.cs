using System.Collections.ObjectModel;
using System.IO;
using NIVEX.Toolkit;

namespace NIVEX.Desktop;

public sealed class StepRow : Observable
{
    private StepState _state = StepState.Pending;
    private string _stateMessage = "";
    private string _index = "";

    public StepRow(WorkflowStep step, Action<StepRow, int> move, Action<StepRow> remove)
    {
        Step = step;
        UpCommand = new AsyncCommand(_ => { move(this, -1); return Task.CompletedTask; });
        DownCommand = new AsyncCommand(_ => { move(this, 1); return Task.CompletedTask; });
        RemoveCommand = new AsyncCommand(_ => { remove(this); return Task.CompletedTask; });
        BrowseCommand = new AsyncCommand(_ =>
        {
            var path = Step.Type switch
            {
                StepType.OpenFolder => Dialogs.OpenFolder(),
                StepType.LaunchProgram => Dialogs.OpenFile("Programs|*.exe"),
                _ => Dialogs.OpenFile(),
            };
            if (path is not null) Input = path;
            return Task.CompletedTask;
        });
    }

    public WorkflowStep Step { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand BrowseCommand { get; }
    public string TypeLabel => ToolText.Get("step_" + Step.Type);
    public string Index { get => _index; set => Set(ref _index, value); }
    public string Name { get => Step.Name; set { Step.Name = value; Notify(nameof(Name)); } }
    public string Input { get => Step.Input; set { Step.Input = value; Notify(nameof(Input)); } }
    public bool Enabled { get => Step.Enabled; set { Step.Enabled = value; Notify(nameof(Enabled)); } }
    public bool ContinueOnError { get => Step.ContinueOnError; set { Step.ContinueOnError = value; Notify(nameof(ContinueOnError)); } }
    public bool ShowInput => Step.Type != StepType.CheckPrevious;
    public bool CanBrowse => Step.Type is StepType.OpenFolder or StepType.OpenFile or StepType.LaunchProgram;
    public string StateText => _state == StepState.Pending && _stateMessage.Length == 0 ? "" : ToolText.Get("ss_" + _state);
    public string StateMessage => _stateMessage;
    public bool HasState => StateText.Length > 0;

    public System.Windows.Media.Brush StateBrush => _state is StepState.Failed
        ? Fmt.Resource("Danger")
        : _state == StepState.Succeeded ? Fmt.Resource("Ink") : Fmt.Resource("Muted");

    public void SetState(StepState state, string message)
    {
        _state = state;
        _stateMessage = message;
        Notify(nameof(StateText));
        Notify(nameof(StateMessage));
        Notify(nameof(HasState));
        Notify(nameof(StateBrush));
    }

    public void ResetState() => SetState(StepState.Pending, "");
    public void Refresh() => Notify(null);
}

public sealed class FlowVm : ToolVm
{
    private readonly WorkflowStore _store;
    private readonly ToolkitModel _host;
    private CancellationTokenSource? _cts;
    private string _id = "";
    private string _name = "";
    private string _message = "";
    private bool _error;
    private bool _running;
    private bool _hasEditor;

    public FlowVm(WorkflowStore store, ToolkitModel host)
    {
        _store = store;
        _host = host;
        NewCommand = new AsyncCommand(_ => { Begin(new Workflow { Name = ToolText.Get("defaultFlowName") }); return Task.CompletedTask; }, _ => !Running);
        SaveCommand = new AsyncCommand(_ => SaveAsync(), _ => HasEditor && !Running);
        DeleteCommand = new AsyncCommand(_ => DeleteAsync(), _ => HasEditor && !Running);
        RunCommand = new AsyncCommand(_ => RunAsync(), _ => HasEditor && !Running);
        StopCommand = new AsyncCommand(_ => { _cts?.Cancel(); return Task.CompletedTask; }, _ => Running);
        AddStepCommand = new AsyncCommand(AddStep, _ => HasEditor && !Running);
        ExportCommand = new AsyncCommand(_ => ExportAsync(), _ => HasEditor && !Running);
        ImportCommand = new AsyncCommand(_ => ImportAsync(), _ => !Running);
        Reload();
    }

    public List<Workflow> Workflows { get; private set; } = new();
    public ObservableCollection<Choice> Items { get; } = new();
    public ObservableCollection<StepRow> Steps { get; } = new();
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand AddStepCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsError { get => _error; private set => Set(ref _error, value); }
    public bool Running { get => _running; private set { Set(ref _running, value); Notify(nameof(Idle)); } }
    public bool Idle => !_running;
    public bool HasEditor { get => _hasEditor; private set { Set(ref _hasEditor, value); Notify(nameof(NoEditor)); } }
    public bool NoEditor => !_hasEditor;
    public bool NoSteps => Steps.Count == 0;
    public bool NoWorkflows => Items.Count == 0;

    protected override void OnLanguage()
    {
        foreach (var step in Steps) step.Refresh();
        RefreshAll();
    }

    public void Reload(string? selectId = null)
    {
        var report = _store.LoadAll();
        Workflows = report.Items.OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Items.Clear();
        foreach (var workflow in Workflows)
            Items.Add(new Choice(workflow.Id, workflow.Name, c => { if (!Running) Begin(Workflows.First(w => w.Id == c.Id)); }));
        if (report.Errors.Count > 0)
        {
            IsError = true;
            Message = ToolText.F("flowLoadErrors", report.Errors.Count);
        }
        if (selectId is not null) Items.FirstOrDefault(c => c.Id == selectId)?.SetSilently(true);
        Notify(nameof(NoWorkflows));
        _host.Profiles?.RefreshWorkflowChoices();
    }

    private void Begin(Workflow workflow)
    {
        _id = workflow.Id;
        Name = workflow.Name;
        Steps.Clear();
        foreach (var step in workflow.Steps) AddRow(Clone(step));
        HasEditor = true;
        Message = "";
        IsError = false;
        foreach (var item in Items) item.SetSilently(item.Id == workflow.Id);
        Reindex();
    }

    private static WorkflowStep Clone(WorkflowStep s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Type = s.Type,
        Input = s.Input,
        Enabled = s.Enabled,
        ContinueOnError = s.ContinueOnError,
    };

    private void AddRow(WorkflowStep step) => Steps.Add(new StepRow(step, Move, RemoveRow));

    private void Move(StepRow row, int delta)
    {
        if (Running) return;
        var index = Steps.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Steps.Count) return;
        Steps.Move(index, target);
        Reindex();
    }

    private void RemoveRow(StepRow row)
    {
        if (Running) return;
        Steps.Remove(row);
        Reindex();
    }

    private void Reindex()
    {
        for (var i = 0; i < Steps.Count; i++) Steps[i].Index = (i + 1).ToString();
        Notify(nameof(NoSteps));
    }

    private Task AddStep(object? parameter)
    {
        if (parameter is not string text || !Enum.TryParse<StepType>(text, out var type)) return Task.CompletedTask;
        if (Steps.Count >= WorkflowStore.MaxSteps) return Task.CompletedTask;
        var step = new WorkflowStep { Type = type, Name = ToolText.Get("step_" + type), Input = type == StepType.Wait ? "5" : type == StepType.Confirm ? ToolText.Get("confirmPrompt") : "" };
        AddRow(step);
        Reindex();
        return Task.CompletedTask;
    }

    private Workflow Build() => new()
    {
        Id = _id,
        Name = Name.Trim(),
        Steps = Steps.Select(s => Clone(s.Step)).ToList(),
    };

    private async Task<bool> SaveAsync()
    {
        IsError = true;
        if (string.IsNullOrWhiteSpace(Name))
        {
            Message = ToolText.Get("flowNeedName");
            return false;
        }
        var workflow = Build();
        var invalid = WorkflowStore.Validate(workflow);
        if (invalid is not null)
        {
            Message = ToolText.F("flowSaveFail", invalid);
            return false;
        }
        try
        {
            await Task.Run(() => _store.Save(workflow));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Message = ToolText.F("flowSaveFail", exception.Message);
            return false;
        }
        IsError = false;
        Message = ToolText.Get("flowSaved");
        Reload(workflow.Id);
        return true;
    }

    private async Task DeleteAsync()
    {
        if (!await _host.Confirm.AskAsync(ToolText.Get("flowDeleteTitle"), ToolText.F("flowDeleteBody", Name))) return;
        var id = _id;
        await Task.Run(() => _store.Delete(id));
        _host.Profiles?.Unlink(id);
        HasEditor = false;
        Steps.Clear();
        Name = "";
        Message = "";
        Reload();
        Notify(nameof(NoSteps));
    }

    private async Task RunAsync()
    {
        IsError = true;
        var steps = Steps.Select(s => s.Step).ToList();
        for (var i = 0; i < steps.Count; i++)
        {
            if (!steps[i].Enabled) continue;
            var invalid = StepValidator.Validate(steps[i]);
            if (invalid is not null)
            {
                Message = $"{i + 1}. {steps[i].Name}: {invalid}";
                return;
            }
        }
        foreach (var row in Steps) row.ResetState();
        Running = true;
        Message = ToolText.Get("netRunning");
        IsError = false;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<StepProgress>(p =>
            {
                if (p.Index >= 0 && p.Index < Steps.Count) Steps[p.Index].SetState(p.State, p.Message);
            });
            var result = await _host.Engine.RunAsync(steps, progress, _cts.Token);
            if (!result.Started)
            {
                IsError = true;
                Message = ToolText.Get("flowBusy");
                return;
            }
            for (var i = 0; i < result.Steps.Count && i < Steps.Count; i++) Steps[i].SetState(result.Steps[i].State, result.Steps[i].Message);
            IsError = !result.Success;
            Message = ToolText.Get(result.Cancelled ? "flowStopped" : result.Success ? "flowDone" : "flowFailed");
            _host.AddHistory(new HistoryRecord
            {
                Kind = HistoryKind.Flow,
                Title = Name,
                Success = result.Success,
                Summary = Message,
                ErrorType = result.Success ? null : result.Cancelled ? "cancelled" : "step_failed",
                Detail = string.Join("\n", result.Steps.Select((s, i) => $"{i + 1}. {(i < Steps.Count ? Steps[i].Step.Name : "")}: {s.State} {s.Message}")),
            });
        }
        finally
        {
            Running = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private Task ExportAsync()
    {
        var path = Dialogs.Save("JSON|*.json", (string.IsNullOrWhiteSpace(Name) ? "workflow" : Name.Trim()) + ".json");
        if (path is null) return Task.CompletedTask;
        var (ok, error) = _store.Export(Build(), path);
        IsError = !ok;
        Message = ok ? ToolText.Get("flowExported") : ToolText.F("flowSaveFail", error ?? "");
        return Task.CompletedTask;
    }

    private Task ImportAsync()
    {
        var path = Dialogs.OpenFile("JSON|*.json");
        if (path is null) return Task.CompletedTask;
        var (workflow, error) = _store.Import(path);
        if (workflow is null)
        {
            IsError = true;
            Message = ToolText.F("flowSaveFail", error ?? "");
            return Task.CompletedTask;
        }
        Begin(workflow);
        Message = ToolText.Get("flowImported");
        return Task.CompletedTask;
    }
}

public sealed class ItemRow : Observable
{
    public ItemRow(ProfileItem item, Action<ItemRow, int> move, Action<ItemRow> remove)
    {
        Item = item;
        UpCommand = new AsyncCommand(_ => { move(this, -1); return Task.CompletedTask; });
        DownCommand = new AsyncCommand(_ => { move(this, 1); return Task.CompletedTask; });
        RemoveCommand = new AsyncCommand(_ => { remove(this); return Task.CompletedTask; });
        BrowseCommand = new AsyncCommand(_ =>
        {
            var path = Item.Kind == ProfileItemKind.Folder ? Dialogs.OpenFolder() : Dialogs.OpenFile("Programs|*.exe");
            if (path is not null) Value = path;
            return Task.CompletedTask;
        });
    }

    public ProfileItem Item { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand BrowseCommand { get; }
    public string KindLabel => ToolText.Get(Item.Kind switch { ProfileItemKind.Program => "profAddProgram", ProfileItemKind.Folder => "profAddFolder", _ => "profAddUrl" });
    public bool CanBrowse => Item.Kind != ProfileItemKind.Url;
    public string Value { get => Item.Value; set { Item.Value = value; Notify(nameof(Value)); } }
    public bool Enabled { get => Item.Enabled; set { Item.Enabled = value; Notify(nameof(Enabled)); } }
    public void Refresh() => Notify(null);
}

public sealed class ProfilesVm : ToolVm
{
    private readonly ProfileStore _store;
    private readonly ToolkitModel _host;
    private List<Profile> _profiles;
    private CancellationTokenSource? _cts;
    private string _id = "";
    private string _name = "";
    private string _description = "";
    private string? _workflowId;
    private string _message = "";
    private bool _error;
    private bool _running;
    private bool _hasEditor;

    public ProfilesVm(ProfileStore store, ToolkitModel host)
    {
        _store = store;
        _host = host;
        _profiles = store.Load();
        if (store.RecoveredFromCorruption)
        {
            IsError = true;
            _message = ToolText.Get("profCorrupt");
        }
        NewCommand = new AsyncCommand(_ => { Begin(new Profile { Name = ToolText.Get("profNew") }); return Task.CompletedTask; }, _ => !Running);
        SaveCommand = new AsyncCommand(_ => Task.FromResult(Save()), _ => HasEditor && !Running);
        DeleteCommand = new AsyncCommand(_ => DeleteAsync(), _ => HasEditor && !Running);
        DuplicateCommand = new AsyncCommand(_ => DuplicateAsync(), _ => HasEditor && !Running);
        RunCommand = new AsyncCommand(_ => RunAsync(), _ => HasEditor && !Running);
        StopCommand = new AsyncCommand(_ => { _cts?.Cancel(); return Task.CompletedTask; }, _ => Running);
        AddItemCommand = new AsyncCommand(AddItem, _ => HasEditor && !Running);
        RebuildList();
    }

    public ObservableCollection<Choice> Items { get; } = new();
    public ObservableCollection<ItemRow> Rows { get; } = new();
    public ObservableCollection<Choice> WorkflowChoices { get; } = new();
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand AddItemCommand { get; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsError { get => _error; private set => Set(ref _error, value); }
    public bool Running { get => _running; private set { Set(ref _running, value); Notify(nameof(Idle)); } }
    public bool Idle => !_running;
    public bool HasEditor { get => _hasEditor; private set { Set(ref _hasEditor, value); Notify(nameof(NoEditor)); } }
    public bool NoEditor => !_hasEditor;
    public bool NoProfiles => Items.Count == 0;
    public bool NoItems => Rows.Count == 0;

    protected override void OnLanguage()
    {
        foreach (var row in Rows) row.Refresh();
        RefreshWorkflowChoices();
        RefreshAll();
    }

    private void RebuildList(string? selectId = null)
    {
        Items.Clear();
        foreach (var profile in _profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Items.Add(new Choice(profile.Id, profile.Name, c => { if (!Running) Begin(_profiles.First(p => p.Id == c.Id)); }));
        if (selectId is not null) Items.FirstOrDefault(c => c.Id == selectId)?.SetSilently(true);
        Notify(nameof(NoProfiles));
    }

    public void RefreshWorkflowChoices()
    {
        WorkflowChoices.Clear();
        var none = new Choice("", ToolText.Get("none"), c => _workflowId = null);
        WorkflowChoices.Add(none);
        foreach (var workflow in _host.Flow.Workflows)
            WorkflowChoices.Add(new Choice(workflow.Id, workflow.Name, c => _workflowId = c.Id));
        SyncWorkflowSelection();
    }

    private void SyncWorkflowSelection()
    {
        foreach (var choice in WorkflowChoices)
            choice.SetSilently(choice.Id == (_workflowId ?? ""));
    }

    public void Unlink(string workflowId)
    {
        var changed = false;
        foreach (var profile in _profiles.Where(p => p.WorkflowId == workflowId))
        {
            profile.WorkflowId = null;
            changed = true;
        }
        if (_workflowId == workflowId) _workflowId = null;
        if (changed) Persist();
        SyncWorkflowSelection();
    }

    private void Begin(Profile profile)
    {
        _id = profile.Id;
        Name = profile.Name;
        Description = profile.Description;
        _workflowId = profile.WorkflowId;
        Rows.Clear();
        foreach (var item in profile.Items)
            Rows.Add(new ItemRow(new ProfileItem { Kind = item.Kind, Value = item.Value, Enabled = item.Enabled }, Move, Remove));
        HasEditor = true;
        Message = "";
        IsError = false;
        foreach (var choice in Items) choice.SetSilently(choice.Id == profile.Id);
        RefreshWorkflowChoices();
        Notify(nameof(NoItems));
    }

    private void Move(ItemRow row, int delta)
    {
        if (Running) return;
        var index = Rows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Rows.Count) return;
        Rows.Move(index, target);
    }

    private void Remove(ItemRow row)
    {
        if (Running) return;
        Rows.Remove(row);
        Notify(nameof(NoItems));
    }

    private Task AddItem(object? parameter)
    {
        if (parameter is not string text || !Enum.TryParse<ProfileItemKind>(text, out var kind)) return Task.CompletedTask;
        if (Rows.Count >= ProfilePlanner.MaxItems) return Task.CompletedTask;
        Rows.Add(new ItemRow(new ProfileItem { Kind = kind, Value = "" }, Move, Remove));
        Notify(nameof(NoItems));
        return Task.CompletedTask;
    }

    private Profile Build() => new()
    {
        Id = _id,
        Name = Name.Trim(),
        Description = Description.Trim(),
        WorkflowId = _workflowId,
        Items = Rows.Select(r => new ProfileItem { Kind = r.Item.Kind, Value = r.Item.Value.Trim(), Enabled = r.Item.Enabled }).ToList(),
    };

    private bool Persist()
    {
        try
        {
            _store.Save(_profiles);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            IsError = true;
            Message = ToolText.F("flowSaveFail", exception.Message);
            return false;
        }
    }

    private bool Save()
    {
        IsError = true;
        if (string.IsNullOrWhiteSpace(Name))
        {
            Message = ToolText.Get("profNeedName");
            return false;
        }
        var profile = Build();
        var invalid = ProfileStore.Validate(profile);
        if (invalid is not null)
        {
            Message = ToolText.F("flowSaveFail", invalid);
            return false;
        }
        var existing = _profiles.FindIndex(p => p.Id == profile.Id);
        if (existing >= 0) _profiles[existing] = profile;
        else _profiles.Add(profile);
        if (!Persist()) return false;
        IsError = false;
        Message = ToolText.Get("profSaved");
        RebuildList(profile.Id);
        return true;
    }

    private async Task DeleteAsync()
    {
        if (!await _host.Confirm.AskAsync(ToolText.Get("profDeleteTitle"), ToolText.F("profDeleteBody", Name))) return;
        _profiles.RemoveAll(p => p.Id == _id);
        Persist();
        HasEditor = false;
        Rows.Clear();
        Name = "";
        Message = "";
        RebuildList();
        Notify(nameof(NoItems));
    }

    private Task DuplicateAsync()
    {
        var copy = ProfileStore.Duplicate(Build(), ToolText.Get("profDuplicateSuffix"));
        _profiles.Add(copy);
        if (Persist())
        {
            RebuildList(copy.Id);
            Begin(copy);
        }
        return Task.CompletedTask;
    }

    private async Task RunAsync()
    {
        var profile = Build();
        var plan = ProfilePlanner.Build(profile, _host.Flow.Workflows);
        if (plan.Steps.Count == 0)
        {
            IsError = true;
            Message = ToolText.Get("profPreviewEmpty");
            return;
        }
        var lines = plan.Steps.Select((s, i) => ToolText.F("profStepLine", i + 1, ToolText.Get("step_" + s.Type), s.Input.Length == 0 ? s.Name : s.Input));
        var body = string.Join("\n", lines);
        if (plan.Issues.Count > 0) body += ToolText.F("profPreviewIssues", string.Join("\n", plan.Issues));
        if (!await _host.Confirm.AskAsync(ToolText.F("profPreviewTitle", profile.Name), body, ToolText.Get("run"))) return;
        Running = true;
        IsError = false;
        Message = ToolText.Get("netRunning");
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _host.Engine.RunAsync(plan.Steps, null, _cts.Token);
            if (!result.Started)
            {
                IsError = true;
                Message = ToolText.Get("flowBusy");
                return;
            }
            IsError = !result.Success;
            Message = ToolText.Get(result.Cancelled ? "flowStopped" : result.Success ? "profRunDone" : "profRunPartial");
            _host.AddHistory(new HistoryRecord
            {
                Kind = HistoryKind.Profile,
                Title = profile.Name,
                Success = result.Success,
                Summary = Message,
                ErrorType = result.Success ? null : result.Cancelled ? "cancelled" : "step_failed",
                Detail = string.Join("\n", result.Steps.Select((s, i) => $"{i + 1}. {plan.Steps[Math.Min(i, plan.Steps.Count - 1)].Name}: {s.State} {s.Message}")),
            });
        }
        finally
        {
            Running = false;
            _cts.Dispose();
            _cts = null;
        }
    }
}
