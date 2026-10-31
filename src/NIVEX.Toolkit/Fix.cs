using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace NIVEX.Toolkit;

public enum CheckStatus { Pending, Checking, Healthy, Problem, Unknown, Failed, Repairing, Repaired, RepairFailed }

public sealed record CheckResult(string Id, CheckStatus Status, string Message, string? Advice = null, string? RepairId = null, long? Metric = null);
public sealed record CommandOutput(int ExitCode, string Output, bool TimedOut, string ErrorOutput = "");
public sealed record TempFileEntry(string Path, long Size, DateTime LastWriteTimeUtc = default);
public sealed record TempPlan(string Root, IReadOnlyList<TempFileEntry> Files, int InaccessibleDirectories = 0)
{
    public long TotalBytes => Files.Sum(f => f.Size);
}
public sealed record CleanupResult(int Deleted, long LogicalBytesDeleted, int Skipped, bool Cancelled);

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
            StandardErrorEncoding = encoding ?? Encoding.Default,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("process did not start");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync();
        var errorOutput = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(linked.Token);
            return new CommandOutput(process.ExitCode, await output, false, await errorOutput);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
            await process.WaitForExitAsync(CancellationToken.None);
            var stdout = await output;
            var stderr = await errorOutput;
            if (ct.IsCancellationRequested) throw;
            return new CommandOutput(-1, stdout, true, stderr);
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
    string DnsTestDomain = "www.microsoft.com",
    Func<CancellationToken, IProgress<FileInventoryProgress>?, FileInventoryScan>? ScanFileMetadata = null);

public sealed class FixService(FixOptions options, ICommandRunner runner, NetworkLab network)
{
    public static readonly string[] QuickIds = ["os", "disk", "temp", "nivex", "adapter", "dns", "internet"];
    public static readonly string[] FullIds = [.. QuickIds, "gateway", "drive-space", "startup", "device-errors", "recent-errors", "sfc", "dism", "file-inventory"];
    private readonly CrashLab _crash = new(runner);
    private static readonly TimeSpan TempAge = TimeSpan.FromHours(24);
    private int _scanning;

    public bool IsScanning => Volatile.Read(ref _scanning) == 1;

    public Task<IReadOnlyList<CheckResult>> ScanAsync(
        bool full,
        IProgress<CheckResult>? progress,
        CancellationToken ct,
        IProgress<FileInventoryProgress>? inventoryProgress = null) =>
        ScanIdsAsync(full ? FullIds : QuickIds, progress, ct, inventoryProgress);

    public async Task<IReadOnlyList<CheckResult>> ScanIdsAsync(
        IReadOnlyList<string> ids,
        IProgress<CheckResult>? progress,
        CancellationToken ct,
        IProgress<FileInventoryProgress>? inventoryProgress = null)
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1) throw new InvalidOperationException("scan already running");
        try
        {
            var results = new List<CheckResult>();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new CheckResult(id, CheckStatus.Checking, ""));
                var result = await CheckOneAsync(id, ct, inventoryProgress);
                results.Add(result);
                progress?.Report(result);
            }
            return results;
        }
        finally { Volatile.Write(ref _scanning, 0); }
    }

    // Re-runs a single check, used to verify a repair against the real system state.
    public async Task<CheckResult> RecheckAsync(string id, CancellationToken ct) => await CheckOneAsync(id, ct);

    private async Task<CheckResult> CheckOneAsync(string id, CancellationToken ct, IProgress<FileInventoryProgress>? inventoryProgress = null)
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
                "drive-space" => await Task.Run(CheckDriveSpace, ct),
                "startup" => await Task.Run(CheckStartup, ct),
                "device-errors" => await CheckDeviceErrorsAsync(ct),
                "recent-errors" => await CheckRecentErrorsAsync(ct),
                "sfc" => await CheckToolAsync("sfc", "sfc", ["/verifyonly"], Encoding.Unicode, ct),
                "dism" => await CheckToolAsync("dism", "dism", ["/Online", "/Cleanup-Image", "/CheckHealth"], Encoding.Default, ct),
                "file-inventory" => await CheckFileInventoryAsync(ct, inventoryProgress),
                _ => new CheckResult(id, CheckStatus.Unknown, "not supported"),
            };
        }

        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new CheckResult(id, CheckStatus.Failed, exception.GetType().Name);
        }
    }

    private async Task<CheckResult> CheckFileInventoryAsync(CancellationToken ct, IProgress<FileInventoryProgress>? progress)
    {
        var scan = await Task.Run(
            () => options.ScanFileMetadata?.Invoke(ct, progress) ??
                FileInventoryScanner.ScanLocalDrives(TimeSpan.FromMinutes(6), ct, progress),
            ct);
        var elapsed = scan.Elapsed.ToString(@"m\:ss");
        var message = $"File metadata checked for {scan.FilesScanned} files across {scan.DirectoriesScanned} folders in {elapsed}. Contents were not opened; {scan.Inaccessible} locations could not be read.";
        if (scan.DirectoriesScanned == 0)
            return new CheckResult("file-inventory", CheckStatus.Unknown, "No accessible, ready local drives were found for metadata scanning.");
        if (!scan.Completed)
            return new CheckResult("file-inventory", CheckStatus.Unknown, $"{message} The scan reached its time limit; the inventory is incomplete.");
        return scan.Inaccessible == 0
            ? new CheckResult("file-inventory", CheckStatus.Healthy, message)
            : new CheckResult("file-inventory", CheckStatus.Unknown, message);
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
            ? new CheckResult("disk", CheckStatus.Problem, text, "Free up space: review the temporary-file cleanup plan or remove files you no longer need.", "clean-temp", free.Value)
            : new CheckResult("disk", CheckStatus.Healthy, text, Metric: free.Value);
    }

    private CheckResult CheckDriveSpace()
    {
        var scanned = 0;
        var inaccessible = 0;
        var low = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
            try
            {
                if (!drive.IsReady)
                {
                    inaccessible++;
                    continue;
                }
                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                if (total <= 0) { inaccessible++; continue; }
                scanned++;
                var percent = free * 100.0 / total;
                if (percent < 10 || free < 10L * 1073741824)
                    low.Add($"{drive.Name} {percent:0}% free");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                inaccessible++;
            }
        }

        if (scanned == 0) return new CheckResult("drive-space", CheckStatus.Unknown, "No ready local drives could be checked.");
        if (low.Count > 0)
            return new CheckResult("drive-space", CheckStatus.Problem,
                $"{string.Join(", ", low)}; {inaccessible} drive(s) unavailable.",
                "Review the affected drive and remove only files you recognize and no longer need.");
        return inaccessible == 0
            ? new CheckResult("drive-space", CheckStatus.Healthy, $"{scanned} local drive(s) checked; no low-space threshold was reached.")
            : new CheckResult("drive-space", CheckStatus.Unknown, $"{scanned} local drive(s) checked; {inaccessible} drive(s) were unavailable.");
    }

    private CheckResult CheckStartup()
    {
        if (!OperatingSystem.IsWindows())
            return new CheckResult("startup", CheckStatus.Unknown, "Startup registry checks are available on Windows only.");
        var scan = StartupInspector.Scan();
        var missing = scan.Entries.Count(entry => !entry.PathExists);
        if (missing > 0)
            return new CheckResult("startup", CheckStatus.Problem,
                $"{missing} Run-key startup entry target(s) could not be found; {scan.InaccessibleLocations} registry location(s) unavailable.",
                "Review these entries in Windows Settings or Task Manager before changing them.");
        return scan.InaccessibleLocations == 0
            ? new CheckResult("startup", CheckStatus.Healthy, $"{scan.Entries.Count} Run-key startup entry(ies) checked.")
            : new CheckResult("startup", CheckStatus.Unknown,
                $"{scan.Entries.Count} Run-key startup entry(ies) checked; {scan.InaccessibleLocations} registry location(s) unavailable.");
    }

    private async Task<CheckResult> CheckDeviceErrorsAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return new CheckResult("device-errors", CheckStatus.Unknown, "Device Manager checks are available on Windows only.");
        var problems = await DeviceInspector.ReadAsync(runner, ct);
        if (problems is null)
            return new CheckResult("device-errors", CheckStatus.Unknown, "Device Manager error status could not be read.");
        return problems.Count == 0
            ? new CheckResult("device-errors", CheckStatus.Healthy, "No non-zero Device Manager error codes were reported.")
            : new CheckResult("device-errors", CheckStatus.Problem,
                $"{problems.Count} device(s) report a Device Manager error code.",
                "Review the device and its driver in Device Manager; NIVEX does not install or remove drivers.");
    }

    private async Task<CheckResult> CheckRecentErrorsAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return new CheckResult("recent-errors", CheckStatus.Unknown, "Windows event log checks are available on Windows only.");
        var errors = await _crash.ReadAsync(15, ct);
        return errors.Count == 0
            ? new CheckResult("recent-errors", CheckStatus.Healthy, "No matching errors were found in the 15 newest queried Application and System log records.")
            : new CheckResult("recent-errors", CheckStatus.Problem,
                $"{errors.Count} matching error record(s) found in the newest queried Application and System log records.",
                "Review the event details and timestamps before deciding whether action is needed.");
    }

    private CheckResult CheckTemp(CancellationToken ct)
    {
        var plan = PlanTempCleanup(ct);
        if (!Directory.Exists(options.TempRoot)) return new CheckResult("temp", CheckStatus.Unknown, "temp folder not found");
        var text = $"{plan.Files.Count} files older than 24h · {Gb(plan.TotalBytes, 2)} reclaimable";
        return plan.TotalBytes >= 500L * 1048576
            ? new CheckResult("temp", CheckStatus.Problem, text, "Review the list and clean up temporary files.", "clean-temp", plan.TotalBytes)
            : new CheckResult("temp", CheckStatus.Healthy, text, Metric: plan.TotalBytes);
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
        if (result.TimedOut) return new CheckResult(id, CheckStatus.Failed, $"{tool} timed out.");
        var evidence = ClipOutput(string.Join(Environment.NewLine, result.Output, result.ErrorOutput));
        if (result.ExitCode != 0)
            return new CheckResult(id, CheckStatus.Failed,
                $"{tool} exited with code {result.ExitCode}" + (evidence.Length == 0 ? "." : $": {evidence}"),
                $"Review the {tool} output and Windows servicing logs before retrying.");

        var normalized = evidence.ToLowerInvariant();
        if (id == "sfc")
        {
            if (normalized.Contains("did not find any integrity violations", StringComparison.Ordinal)
                || evidence.Contains("무결성 위반을 발견하지 못했습니다", StringComparison.Ordinal))
                return new CheckResult(id, CheckStatus.Healthy, $"{tool}: {evidence}");
            if (normalized.Contains("found integrity violations", StringComparison.Ordinal)
                || evidence.Contains("무결성 위반을 발견했습니다", StringComparison.Ordinal))
                return new CheckResult(id, CheckStatus.Problem, $"{tool}: {evidence}", "Review the reported integrity violations before choosing a repair.");
        }
        else if (id == "dism")
        {
            if (normalized.Contains("no component store corruption detected", StringComparison.Ordinal))
                return new CheckResult(id, CheckStatus.Healthy, $"{tool}: {evidence}");
            if (normalized.Contains("component store is repairable", StringComparison.Ordinal)
                || normalized.Contains("component store cannot be repaired", StringComparison.Ordinal))
                return new CheckResult(id, CheckStatus.Problem, $"{tool}: {evidence}", "Review the DISM result before choosing a repair.");
        }

        return new CheckResult(id, CheckStatus.Unknown,
            $"{tool} exited with code 0, but its output did not confirm the system state" + (evidence.Length == 0 ? "." : $": {evidence}"));
    }

    private static string ClipOutput(string output)
    {
        var normalized = output.Replace('\0', ' ').Trim();
        return normalized.Length <= 400 ? normalized : normalized[..400] + "…";
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
        var inaccessible = 0;
        var root = options.TempRoot;
        if (!Directory.Exists(root)) return new TempPlan(root, files, inaccessible);
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
                    files.Add(new TempFileEntry(path, info.Length, info.LastWriteTimeUtc));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                inaccessible++;
            }
        }
        return new TempPlan(root, files, inaccessible);
    }

    public CleanupResult ExecuteTempCleanup(TempPlan plan, IProgress<int>? progress, CancellationToken ct)
    {
        string configuredRoot;
        string requestedRoot;
        try
        {
            configuredRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.TempRoot));
            requestedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Root));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return new CleanupResult(0, 0, plan.Files.Count, false);
        }
        if (!string.Equals(configuredRoot, requestedRoot, StringComparison.OrdinalIgnoreCase))
            return new CleanupResult(0, 0, plan.Files.Count, false);
        if (!Directory.Exists(requestedRoot)) return new CleanupResult(0, 0, plan.Files.Count, false);
        var rootInfo = new DirectoryInfo(requestedRoot);
        try
        {
            if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return new CleanupResult(0, 0, plan.Files.Count, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CleanupResult(0, 0, plan.Files.Count, false);
        }
        var root = rootInfo.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        int deleted = 0, skipped = 0;
        long freed = 0;
        foreach (var entry in plan.Files)
        {
            if (ct.IsCancellationRequested) return new CleanupResult(deleted, freed, skipped, true);
            try
            {
                var full = Path.GetFullPath(entry.Path);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                if (!IsSafeTempPath(rootInfo.FullName, full)) { skipped++; continue; }
                var current = new FileInfo(full);
                if (!current.Exists
                    || current.Length != entry.Size
                    || current.LastWriteTimeUtc != entry.LastWriteTimeUtc
                    || current.LastWriteTimeUtc > DateTime.UtcNow - TempAge)
                {
                    skipped++;
                    continue;
                }
                File.Delete(full);
                if (File.Exists(full)) { skipped++; continue; }
                deleted++;
                freed += entry.Size;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                skipped++;
            }
            progress?.Report(deleted + skipped);
        }
        return new CleanupResult(deleted, freed, skipped, false);
    }

    private static bool IsSafeTempPath(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        if (relative == "." || Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return false;

        var current = root;
        foreach (var part in Path.GetDirectoryName(relative)?.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0;
    }

    private static string Gb(long bytes, int digits = 1) => (bytes / 1073741824d).ToString("F" + digits) + " GB";
}
