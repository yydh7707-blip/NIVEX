using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;

namespace NIVEX.Desktop;

public abstract class ToolVm : Observable
{
    protected ToolVm() => ToolText.Instance.PropertyChanged += (_, _) => OnLanguage();

    public ToolText Tx => ToolText.Instance;

    protected virtual void OnLanguage() => RefreshAll();
}

public sealed class AsyncCommand(Func<object?, Task> run, Func<object?, bool>? can = null) : ICommand
{
    private bool _busy;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_busy && (can?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await run(parameter);
        }
        catch (Exception exception)
        {
            Trace.TraceError(exception.ToString());
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

public sealed class ConfirmModel : ToolVm
{
    private TaskCompletionSource<bool>? _pending;
    private bool _isOpen;
    private string _title = "";
    private string _body = "";
    private string _yes = "";
    private string _no = "";

    public ConfirmModel()
    {
        YesCommand = new AsyncCommand(_ => Close(true));
        NoCommand = new AsyncCommand(_ => Close(false));
    }

    public bool IsOpen { get => _isOpen; private set => Set(ref _isOpen, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Body { get => _body; private set => Set(ref _body, value); }
    public string YesText { get => _yes; private set => Set(ref _yes, value); }
    public string NoText { get => _no; private set => Set(ref _no, value); }
    public ICommand YesCommand { get; }
    public ICommand NoCommand { get; }

    public Task<bool> AskAsync(string title, string body, string? yes = null, string? no = null)
    {
        _pending?.TrySetResult(false);
        _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Title = title;
        Body = body;
        YesText = yes ?? ToolText.Get("doIt");
        NoText = no ?? ToolText.Get("cancel");
        IsOpen = true;
        return _pending.Task;
    }

    public void Dismiss() => Close(false);

    private Task Close(bool result)
    {
        IsOpen = false;
        _pending?.TrySetResult(result);
        _pending = null;
        return Task.CompletedTask;
    }
}

public sealed class Choice : Observable
{
    private bool _selected;
    private readonly Action<Choice>? _onSelected;

    public Choice(string id, string label, Action<Choice>? onSelected = null)
    {
        Id = id;
        Label = label;
        _onSelected = onSelected;
    }

    public string Id { get; }
    public string Label { get; set; }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Notify(nameof(IsSelected));
            if (value) _onSelected?.Invoke(this);
        }
    }

    public void SetSilently(bool value)
    {
        _selected = value;
        Notify(nameof(IsSelected));
    }
}

public sealed record Row(string Primary, string Secondary = "");

public sealed class NavEntry(string section, string icon, Func<string> label) : Observable
{
    private bool _selected;
    public string Section { get; } = section;
    public string Icon { get; } = icon;
    public string Label => label();
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    public void Refresh() => Notify(nameof(Label));
}

internal static class Fmt
{
    public static string Bytes(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / 1073741824d:0.0} GB" :
        bytes >= 1L << 20 ? $"{bytes / 1048576d:0.#} MB" :
        $"{bytes / 1024d:0.#} KB";

    public static string Gb(ulong? bytes) => bytes is null ? "—" : $"{bytes.Value / 1073741824d:0.0} GB";
    public static string Pct(double? value) => value is null ? "—" : $"{value.Value:0}%";
    public static string Time(DateTimeOffset time) => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public static string Ms(double? value) => value is null ? "—" : $"{value.Value:0} ms";

    public static Brush Resource(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.White;
}

internal static class Dialogs
{
    public static string? OpenFile(string filter = "All files|*.*")
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public static string? OpenFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public static string? Save(string filter, string fileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = fileName, OverwritePrompt = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
