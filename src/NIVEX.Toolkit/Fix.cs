using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace NIVEX.Toolkit;

public enum CheckStatus { Pending, Checking, Healthy, Problem, Unknown, Failed, Repairing, Repaired, RepairFailed }

public sealed record CheckResult(string Id, CheckStatus Status, string Message, string? Advice = null, string? RepairId = null);
public sealed record CommandOutput(int ExitCode, string Output, bool TimedOut);
public sealed record TempFileEntry(string Path, long Size);
public sealed record TempPlan(string Root, IReadOnlyList<TempFileEntry> Files)
{
    public long TotalBytes => Files.Sum(f => f.Size);
}
public sealed record CleanupResult(int Deleted, long BytesFreed, int Skipped, bool Cancelled);

public interface ICommandRunner
{
    Task<CommandOutput> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, Encoding? encoding, CancellationToken ct);
}

public sealed class ProcessRunner : ICommandRunner
{
    public async Task<CommandOutput> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, Encoding? encoding, CancellationToken ct)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding ?? Encoding.Default,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("process did not start");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            return new CommandOutput(process.ExitCode, await output, false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            if (ct.IsCancellationRequested) throw;
            return new CommandOutput(-1, "", true);
        }
    }
}

public sealed record FixOptions(
    string TempRoot,
    string SettingsPath,
    string LogPath,
    IReadOnlyList<string> RequiredFiles,
    string Version,
    Func<bool> IsAdministrator,
    Func<long?> SystemDriveFreeBytes,
    Func<long?> SystemDriveTotalBytes,
    string DnsTestDomain = "www.microsoft.com");

public sealed class FixService(FixOptions options, ICommandRunner runner, NetworkLab network)
{
    public static readonly string[] QuickIds = ["os", "disk", "temp", "nivex", "adapter", "dns", "internet"];
    public static readonly string[] FullIds = [.. QuickIds, "gateway", "sfc", "dism"];
    private static readonly TimeSpan TempAge = TimeSpan.FromHours(24);
    private int _scanning;

    public bool IsScanning => Volatile.Read(ref _scanning) == 1;

    public Task<IReadOnlyList<CheckResult>> ScanAsync(bool full, IProgress<CheckResult>? progress, CancellationToken ct) =>
        ScanIdsAsync(full ? FullIds : QuickIds, progress, ct);

    public async Task<IReadOnlyList<CheckResult>> ScanIdsAsync(IReadOnlyList<string> ids, IProgress<CheckResult>? progress, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1) throw new InvalidOperationException("scan already running");
        try
        {
            var results = new List<CheckResult>();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new CheckResult(id, CheckStatus.Checking, ""));
                var result = await CheckOneAsync(id, ct);
                results.Add(result);
                progress?.Report(result);
            }
            return results;
        }
        finally { Volatile.Write(ref _scanning, 0); }
    }

    // Re-runs a single check, used to verify a repair against the real system state.
    public async Task<CheckResult> RecheckAsync(string id, CancellationToken ct) => await CheckOneAsync(id, ct);

    private async Task<CheckResult> CheckOneAsync(string id, CancellationToken ct)
    {
        try
        {
            return id switch
            {
                "os" => CheckOs(),
                "disk" => CheckDisk(),
                "temp" => await Task.Run(() => CheckTemp(ct), ct),
                "nivex" => CheckNivex(),
                "adapter" => CheckAdapter(),
                "gateway" => await CheckGatewayAsync(ct),
                "dns" => await CheckDnsAsync(ct),
                "internet" => await CheckInternetAsync(ct),
                "sfc" => await CheckToolAsync("sfc", "sfc", ["/verifyonly"], Encoding.Unicode, ct),
                "dism" => await CheckToolAsync("dism", "dism", ["/Online", "/Cleanup-Image", "/CheckHealth"], Encoding.Default, ct),
                _ => new CheckResult(id, CheckStatus.Unknown, "not supported"),
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new CheckResult(id, CheckStatus.Failed, exception.GetType().Name);
        }
    }

    private CheckResult CheckOs() =>
        new("os", CheckStatus.Healthy, $"{RuntimeInformation.OSDescription} · build {Environment.OSVersion.Version.Build}");

    private CheckResult CheckDisk()
    {
        var free = options.SystemDriveFreeBytes();
        var total = options.SystemDriveTotalBytes();
        if (free is null || total is null or 0) return new CheckResult("disk", CheckStatus.Unknown, "unavailable");
        var percent = free.Value * 100.0 / total.Value;
        var text = $"{Gb(free.Value)} free of {Gb(total.Value)} ({percent:0}%)";
        return percent < 10 || free.Value < 10L * 1073741824
            ? new CheckResult("disk", CheckStatus.Problem, text, "Free up space: run the temporary-file cleanup or remove files you no longer need.", "clean-temp")
            : new CheckResult("disk", CheckStatus.Healthy, text);
    }

    private CheckResult CheckTemp(CancellationToken ct)
    {
        var plan = PlanTempCleanup(ct);
        if (!Directory.Exists(options.TempRoot)) return new CheckResult("temp", CheckStatus.Unknown, "temp folder not found");
        var text = $"{plan.Files.Count} files older than 24h · {Gb(plan.TotalBytes, 2)} reclaimable";
        return plan.TotalBytes >= 500L * 1048576
            ? new CheckResult("temp", CheckStatus.Problem, text, "Review the list and clean up temporary files.", "clean-temp")
            : new CheckResult("temp", CheckStatus.Healthy, text);
    }

    private CheckResult CheckNivex()
    {
        var problems = new List<string>();
        foreach (var file in options.RequiredFiles)
            if (!File.Exists(file)) problems.Add("missing: " + Path.GetFileName(file));
        if (File.Exists(options.SettingsPath))
        {
            try { using var _ = JsonDocument.Parse(File.ReadAllText(options.SettingsPath)); }
            catch (Exception exception) when (exception is JsonException or IOException) { problems.Add("settings file unreadable"); }
        }
        try
        {
            var directory = Path.GetDirectoryName(options.LogPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var _ = new FileStream(options.LogPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { problems.Add("log not writable"); }
        return problems.Count == 0
            ? new CheckResult("nivex", CheckStatus.Healthy, $"v{options.Version} · files, settings and log are accessible")
            : new CheckResult("nivex", CheckStatus.Problem, string.Join(", ", problems), "Reinstall NIVEX if files are missing. Your sign-in data is not reset by this check.");
    }

    private CheckResult CheckAdapter()
    {
        var adapters = NetworkLab.ReadAdapters();
        var active = adapters.Where(a => a.Up && a.Addresses.Count > 0).ToList();
        return active.Count > 0
            ? new CheckResult("adapter", CheckStatus.Healthy, string.Join(", ", active.Select(a => $"{a.Name} {a.Addresses[0]}").Take(3)))
            : new CheckResult("adapter", CheckStatus.Problem, "no connected adapter with an IPv4 address", "Check the cable, Wi-Fi connection or adapter state in Windows settings.");
    }

    private async Task<CheckResult> CheckGatewayAsync(CancellationToken ct)
    {
        var gateway = NetworkLab.ReadAdapters().Where(a => a.Up).SelectMany(a => a.Gateways).FirstOrDefault();
        if (gateway is null) return new CheckResult("gateway", CheckStatus.Unknown, "no default gateway configured");
        var stats = await network.PingAsync(gateway, 3, 1500, ct);
        return stats.Received > 0
            ? new CheckResult("gateway", CheckStatus.Healthy, $"{gateway} · {stats.AvgMs:0} ms")
            : new CheckResult("gateway", CheckStatus.Unknown, $"{gateway} did not answer ping (some routers block ICMP)");
    }

    private async Task<CheckResult> CheckDnsAsync(CancellationToken ct)
    {
        var dns = await network.DnsAsync(options.DnsTestDomain, ct);
        return dns.Ok
            ? new CheckResult("dns", CheckStatus.Healthy, $"{dns.Domain} · {dns.Ms:0} ms")
            : new CheckResult("dns", CheckStatus.Problem, $"{dns.Domain} lookup failed ({dns.Error})", "Flushing the DNS cache may help. If it keeps failing, check the DNS server in your network settings.", "flush-dns");
    }

    private async Task<CheckResult> CheckInternetAsync(CancellationToken ct)
    {
        foreach (var target in NetworkLab.ExternalTargets)
        {
            var stats = await network.PingAsync(target, 2, 2000, ct);
            if (stats.Received > 0) return new CheckResult("internet", CheckStatus.Healthy, $"{target} · {stats.AvgMs:0} ms");
        }
        return new CheckResult("internet", CheckStatus.Problem, "public test servers did not answer", "Check your connection. Firewalls can also block ping, so this is not conclusive.");
    }

    private async Task<CheckResult> CheckToolAsync(string id, string tool, string[] args, Encoding encoding, CancellationToken ct)
    {
        if (!options.IsAdministrator())
            return new CheckResult(id, CheckStatus.Unknown, "administrator rights required", "Restart NIVEX as administrator to run this check.");
        var result = await runner.RunAsync(tool, args, TimeSpan.FromMinutes(30), encoding, ct);
        if (result.TimedOut) return new CheckResult(id, CheckStatus.Failed, "timed out");
        return result.ExitCode == 0
            ? new CheckResult(id, CheckStatus.Healthy, $"{tool} finished with exit code 0")
            : new CheckResult(id, CheckStatus.Problem, $"{tool} finished with exit code {result.ExitCode}", $"Run '{tool}' manually from an administrator prompt for details.");
    }

    public static readonly string[] AllIds = [.. FullIds];

    public async Task<CheckResult> AdvancedNetworkRepairAsync(string kind, CancellationToken ct)
    {
        var id = "net-" + kind;
        string[] args = kind switch
        {
            "winsock" => ["winsock", "reset"],
            "tcpip" => ["int", "ip", "reset"],
            _ => [],
        };
        if (args.Length == 0) return new CheckResult(id, CheckStatus.RepairFailed, "unsupported");
        if (!options.IsAdministrator()) return new CheckResult(id, CheckStatus.RepairFailed, "administrator rights required");
        try
        {
            var result = await runner.RunAsync("netsh", args, TimeSpan.FromSeconds(60), null, ct);
            return result.ExitCode == 0
                ? new CheckResult(id, CheckStatus.Repaired, "netsh exit code 0 · restart Windows to apply")
                : new CheckResult(id, CheckStatus.RepairFailed, $"netsh exit code {result.ExitCode}");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CheckResult(id, CheckStatus.RepairFailed, exception.GetType().Name);
        }
    }

    public async Task<CheckResult> FlushDnsAsync(CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync("ipconfig", ["/flushdns"], TimeSpan.FromSeconds(30), null, ct);
            return result.ExitCode == 0
                ? new CheckResult("flush-dns", CheckStatus.Repaired, "DNS cache flushed (exit code 0)")
                : new CheckResult("flush-dns", CheckStatus.RepairFailed, $"ipconfig exit code {result.ExitCode}");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CheckResult("flush-dns", CheckStatus.RepairFailed, exception.GetType().Name);
        }
    }

    public TempPlan PlanTempCleanup(CancellationToken ct)
    {
        var files = new List<TempFileEntry>();
        var root = options.TempRoot;
        if (!Directory.Exists(root)) return new TempPlan(root, files);
        var cutoff = DateTime.UtcNow - TempAge;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(current))
                    if (!new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(directory);
                foreach (var path in Directory.EnumerateFiles(current))
                {
                    var info = new FileInfo(path);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LastWriteTimeUtc > cutoff) continue;
                    files.Add(new TempFileEntry(path, info.Length));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // inaccessible folders are skipped
            }
        }
        return new TempPlan(root, files);
    }

    public CleanupResult ExecuteTempCleanup(TempPlan plan, IProgress<int>? progress, CancellationToken ct)
    {
        var root = Path.GetFullPath(plan.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        int deleted = 0, skipped = 0;
        long freed = 0;
        foreach (var entry in plan.Files)
        {
            if (ct.IsCancellationRequested) return new CleanupResult(deleted, freed, skipped, true);
            try
            {
                var full = Path.GetFullPath(entry.Path);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                File.Delete(full);
                deleted++;
                freed += entry.Size;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
            progress?.Report(deleted + skipped);
        }
        return new CleanupResult(deleted, freed, skipped, false);
    }

    private static string Gb(long bytes, int digits = 1) => (bytes / 1073741824d).ToString("F" + digits) + " GB";
}
