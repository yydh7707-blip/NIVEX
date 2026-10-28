using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NIVEX.Toolkit;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepType { OpenFolder, OpenFile, OpenUrl, LaunchProgram, Wait, Confirm, CheckPrevious }

public enum StepState { Pending, Running, Succeeded, Failed, Skipped, Cancelled }

public sealed class WorkflowStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public StepType Type { get; set; }
    public string Input { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool ContinueOnError { get; set; }
}

public sealed class Workflow
{
    public const int CurrentSchema = 1;
    public int Schema { get; set; } = CurrentSchema;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<WorkflowStep> Steps { get; set; } = new();
}

public sealed record StepProgress(int Index, StepState State, string Message);
public sealed record StepOutcome(StepState State, string Message);
public sealed record RunResult(bool Started, bool Success, bool Cancelled, IReadOnlyList<StepOutcome> Steps);

public interface IStepHost
{
    void Open(string target);
    void Launch(string executable);
    Task<bool> ConfirmAsync(string message, CancellationToken ct);
}

public sealed class SystemStepHost(Func<string, CancellationToken, Task<bool>> confirm) : IStepHost
{
    public void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    public void Launch(string executable) => Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable) ?? "" });
    public Task<bool> ConfirmAsync(string message, CancellationToken ct) => confirm(message, ct);
}

public static class StepValidator
{
    private static readonly HashSet<string> Executable = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".msi", ".com", ".scr", ".reg", ".lnk", ".hta", ".cpl", ".msc" };

    public static string? Validate(WorkflowStep step)
    {
        var input = (step.Input ?? "").Trim();
        switch (step.Type)
        {
            case StepType.OpenFolder:
                if (!Path.IsPathRooted(input)) return "A full folder path is required.";
                return Directory.Exists(input) ? null : "Folder not found: " + input;
            case StepType.OpenFile:
                if (!Path.IsPathRooted(input)) return "A full file path is required.";
                if (!File.Exists(input)) return "File not found: " + input;
                return Executable.Contains(Path.GetExtension(input)) ? "Programs and scripts must use the Launch program step." : null;
            case StepType.LaunchProgram:
                if (!Path.IsPathRooted(input)) return "A full program path is required.";
                if (!string.Equals(Path.GetExtension(input), ".exe", StringComparison.OrdinalIgnoreCase)) return "Only .exe programs can be launched.";
                return File.Exists(input) ? null : "Program not found: " + input;
            case StepType.OpenUrl:
                return Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host)
                    ? null : "Only http and https addresses are allowed.";
            case StepType.Wait:
                return int.TryParse(input, out var seconds) && seconds is >= 1 and <= 300 ? null : "Wait must be 1–300 seconds.";
            case StepType.Confirm:
                return input.Length is > 0 and <= 200 ? null : "Enter a message of up to 200 characters.";
            case StepType.CheckPrevious:
                return null;
            default:
                return "Unknown step type.";
        }
    }
}

public sealed class WorkflowEngine(IStepHost host)
{
    private int _running;
    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public async Task<RunResult> RunAsync(IReadOnlyList<WorkflowStep> steps, IProgress<StepProgress>? progress, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return new RunResult(false, false, false, []);
        var outcomes = steps.Select(_ => new StepOutcome(StepState.Pending, "")).ToList();
        void Set(int index, StepState state, string message)
        {
            outcomes[index] = new StepOutcome(state, message);
            progress?.Report(new StepProgress(index, state, message));
        }
        try
        {
            var previousOk = true;
            var stop = false;
            var cancelled = false;
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (stop) { Set(i, cancelled ? StepState.Cancelled : StepState.Skipped, ""); continue; }
                if (!step.Enabled) { Set(i, StepState.Skipped, ""); continue; }
                if (ct.IsCancellationRequested) { cancelled = true; stop = true; Set(i, StepState.Cancelled, ""); continue; }
                Set(i, StepState.Running, "");
                var ok = false;
                var message = "";
                try
                {
                    var error = StepValidator.Validate(step);
                    if (error is not null) message = error;
                    else (ok, message) = await ExecuteAsync(step, previousOk, ct);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true; stop = true;
                    Set(i, StepState.Cancelled, "");
                    continue;
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    message = exception.Message;
                }
                if (message == "declined") { cancelled = true; stop = true; Set(i, StepState.Cancelled, "User declined"); continue; }
                Set(i, ok ? StepState.Succeeded : StepState.Failed, message);
                previousOk = ok;
                if (!ok && !step.ContinueOnError) stop = true;
            }
            var success = !cancelled && outcomes.Where((_, index) => steps[index].Enabled).All(o => o.State == StepState.Succeeded);
            return new RunResult(true, success, cancelled, outcomes);
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    private async Task<(bool Ok, string Message)> ExecuteAsync(WorkflowStep step, bool previousOk, CancellationToken ct)
    {
        var input = step.Input.Trim();
        switch (step.Type)
        {
            case StepType.OpenFolder:
            case StepType.OpenFile:
            case StepType.OpenUrl:
                host.Open(input);
                return (true, "Open request sent");
            case StepType.LaunchProgram:
                host.Launch(input);
                return (true, "Program start requested");
            case StepType.Wait:
                await Task.Delay(TimeSpan.FromSeconds(int.Parse(input)), ct);
                return (true, "Waited " + input + "s");
            case StepType.Confirm:
                return await host.ConfirmAsync(input, ct) ? (true, "Confirmed") : (false, "declined");
            case StepType.CheckPrevious:
                return previousOk ? (true, "Previous step succeeded") : (false, "Previous step failed");
            default:
                return (false, "Unknown step type");
        }
    }
}

public sealed record LoadReport<T>(IReadOnlyList<T> Items, IReadOnlyList<string> Errors);

public sealed class WorkflowStore(string directory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public const int MaxFileBytes = 256 * 1024;
    public const int MaxSteps = 100;

    public static string? Validate(Workflow workflow)
    {
        if (workflow.Schema != Workflow.CurrentSchema) return "unsupported schema";
        if (string.IsNullOrWhiteSpace(workflow.Name) || workflow.Name.Length > 80) return "invalid name";
        if (workflow.Id.Length != 32 || !workflow.Id.All(Uri.IsHexDigit)) return "invalid id";
        if (workflow.Steps is null || workflow.Steps.Count > MaxSteps) return "too many steps";
        foreach (var step in workflow.Steps)
        {
            if (!Enum.IsDefined(step.Type)) return "unknown step type";
            if ((step.Input ?? "").Length > 500 || (step.Name ?? "").Length > 80) return "step too long";
        }
        return null;
    }

    public void Save(Workflow workflow)
    {
        var error = Validate(workflow);
        if (error is not null) throw new InvalidDataException(error);
        Directory.CreateDirectory(directory);
        var path = PathFor(workflow.Id);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(workflow, Json));
        File.Move(path + ".tmp", path, true);
    }

    public bool Delete(string id)
    {
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) return false;
        var path = PathFor(id);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public LoadReport<Workflow> LoadAll()
    {
        var items = new List<Workflow>();
        var errors = new List<string>();
        if (!Directory.Exists(directory)) return new LoadReport<Workflow>(items, errors);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var (workflow, error) = ReadFile(file);
            if (workflow is not null) items.Add(workflow);
            else errors.Add(Path.GetFileName(file) + ": " + error);
        }
        return new LoadReport<Workflow>(items.OrderBy(w => w.Name).ToList(), errors);
    }

    public (bool Ok, string? Error) Export(Workflow workflow, string path)
    {
        try
        {
            if (!Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)) return (false, "invalid path");
            File.WriteAllText(path, JsonSerializer.Serialize(workflow, Json));
            return (true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return (false, exception.Message); }
    }

    public (Workflow? Workflow, string? Error) Import(string path)
    {
        var (workflow, error) = ReadFile(path);
        if (workflow is null) return (null, error);
        workflow.Id = Guid.NewGuid().ToString("N");
        return (workflow, null);
    }

    private static (Workflow?, string?) ReadFile(string path)
    {
        try
        {
            if (!Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)) return (null, "invalid path");
            if (new FileInfo(path).Length > MaxFileBytes) return (null, "file too large");
            var workflow = JsonSerializer.Deserialize<Workflow>(File.ReadAllText(path), Json);
            if (workflow is null) return (null, "empty file");
            var error = Validate(workflow);
            return error is null ? (workflow, null) : (null, error);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (null, exception is JsonException ? "invalid format" : exception.Message);
        }
    }

    private string PathFor(string id) => Path.Combine(directory, id + ".json");
}
