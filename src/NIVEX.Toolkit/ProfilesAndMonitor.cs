using System.Text.Json;
using System.Text.Json.Serialization;
using NIVEX.Application;

namespace NIVEX.Toolkit;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProfileItemKind { Program, Folder, Url }

public sealed class ProfileItem
{
    public ProfileItemKind Kind { get; set; }
    public string Value { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ProfileItem> Items { get; set; } = new();
    public string? WorkflowId { get; set; }
}

public sealed record ProfilePlan(IReadOnlyList<WorkflowStep> Steps, IReadOnlyList<string> Issues);

public static class ProfilePlanner
{
    public const int MaxItems = 50;

    public static ProfilePlan Build(Profile profile, IReadOnlyList<Workflow> workflows)
    {
        var steps = new List<WorkflowStep>();
        var issues = new List<string>();
        foreach (var item in profile.Items.Where(i => i.Enabled).Take(MaxItems))
        {
            var step = new WorkflowStep
            {
                Name = item.Value,
                Input = item.Value,
                ContinueOnError = true,
                Type = item.Kind switch
                {
                    ProfileItemKind.Program => StepType.LaunchProgram,
                    ProfileItemKind.Folder => StepType.OpenFolder,
                    _ => StepType.OpenUrl,
                },
            };
            var error = StepValidator.Validate(step);
            if (error is not null) issues.Add(error);
            steps.Add(step);
        }
        if (!string.IsNullOrEmpty(profile.WorkflowId))
        {
            var linked = workflows.FirstOrDefault(w => w.Id == profile.WorkflowId);
            if (linked is null) issues.Add("The linked workflow no longer exists.");
            else
            {
                foreach (var step in linked.Steps.Where(s => s.Enabled))
                {
                    var error = StepValidator.Validate(step);
                    if (error is not null) issues.Add(error);
                    steps.Add(step);
                }
            }
        }
        return new ProfilePlan(steps, issues);
    }
}

public sealed class ProfileStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public bool RecoveredFromCorruption { get; private set; }

    public static string? Validate(Profile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 60) return "invalid name";
        if (profile.Description.Length > 300) return "description too long";
        if (profile.Items.Count > ProfilePlanner.MaxItems) return "too many items";
        if (profile.Items.Any(i => !Enum.IsDefined(i.Kind) || i.Value.Length > 500)) return "invalid item";
        return null;
    }

    public List<Profile> Load()
    {
        RecoveredFromCorruption = false;
        try
        {
            if (!File.Exists(path)) return new List<Profile>();
            var list = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(path), Json) ?? throw new JsonException("empty");
            if (list.Any(p => string.IsNullOrWhiteSpace(p.Id) || Validate(p) is not null)) throw new JsonException("invalid profile");
            return list;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            RecoveredFromCorruption = true;
            try { if (File.Exists(path)) File.Move(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); }
            catch (Exception) { }
            return new List<Profile>();
        }
    }

    public void Save(IReadOnlyList<Profile> profiles)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(profiles, Json));
        File.Move(path + ".tmp", path, true);
    }

    public static Profile Duplicate(Profile source, string suffix) => new()
    {
        Name = (source.Name + " " + suffix).Trim(),
        Description = source.Description,
        WorkflowId = source.WorkflowId,
        Items = source.Items.Select(i => new ProfileItem { Kind = i.Kind, Value = i.Value, Enabled = i.Enabled }).ToList(),
    };
}

public sealed record MonitorSample(DateTimeOffset At, double? Cpu, double? MemoryPercent, double? DiskPercent, double? Gpu, SystemSnapshot Raw);

public sealed class MonitorService(ISystemMetrics metrics) : IDisposable
{
    public const int Capacity = 120;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _read = new(1, 1);
    private readonly Queue<double> _cpu = new(), _memory = new(), _disk = new(), _gpu = new();
    private CancellationTokenSource? _cts;
    private int _generation;

    public event Action<MonitorSample>? Sampled;
    public event Action<string>? Failed;
    public bool IsRunning { get { lock (_gate) return _cts is not null; } }
    public DateTimeOffset? LastSampleAt { get; private set; }
    public string? LastError { get; private set; }
    public MonitorSample? Latest { get; private set; }

    public void Start(TimeSpan interval)
    {
        lock (_gate)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var generation = ++_generation;
            _ = Task.Run(() => LoopAsync(interval, generation, token));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _generation++;
        }
    }

    public async Task<MonitorSample?> SampleOnceAsync(CancellationToken ct = default)
    {
        await _read.WaitAsync(ct);
        try
        {
            var raw = await Task.Run(metrics.Read, ct);
            var sample = new MonitorSample(DateTimeOffset.Now, raw.CpuPercent,
                raw.MemoryTotalBytes is > 0 && raw.MemoryUsedBytes is not null ? raw.MemoryUsedBytes * 100.0 / raw.MemoryTotalBytes : null,
                raw.DiskTotalBytes is > 0 && raw.DiskUsedBytes is not null ? raw.DiskUsedBytes * 100.0 / raw.DiskTotalBytes : null,
                raw.GpuPercent, raw);
            lock (_gate)
            {
                Push(_cpu, sample.Cpu);
                Push(_memory, sample.MemoryPercent);
                Push(_disk, sample.DiskPercent);
                Push(_gpu, sample.Gpu);
                Latest = sample;
                LastSampleAt = sample.At;
                LastError = null;
            }
            return sample;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastError = exception.GetType().Name;
            Failed?.Invoke(LastError);
            return null;
        }
        finally { _read.Release(); }
    }

    public (double[] Cpu, double[] Memory, double[] Disk, double[] Gpu) History()
    {
        lock (_gate) return (_cpu.ToArray(), _memory.ToArray(), _disk.ToArray(), _gpu.ToArray());
    }

    public (double? Cpu, double? Memory, double? Disk, int Samples) Averages()
    {
        lock (_gate)
            return (_cpu.Count > 0 ? _cpu.Average() : null, _memory.Count > 0 ? _memory.Average() : null,
                _disk.Count > 0 ? _disk.Average() : null, Math.Max(_cpu.Count, _memory.Count));
    }

    private async Task LoopAsync(TimeSpan interval, int generation, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            do
            {
                var sample = await SampleOnceAsync(ct);
                if (sample is not null && !ct.IsCancellationRequested) Sampled?.Invoke(sample);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    private static void Push(Queue<double> queue, double? value)
    {
        if (value is null) return;
        queue.Enqueue(value.Value);
        while (queue.Count > Capacity) queue.Dequeue();
    }

    public void Dispose()
    {
        Stop();
        _read.Dispose();
    }
}
