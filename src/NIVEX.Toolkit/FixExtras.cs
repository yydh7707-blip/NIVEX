using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NIVEX.Toolkit;

public sealed record StartupChange(string Name, string Location, bool WasEnabled, DateTimeOffset At);
public sealed record DeviceProblem(string Name, int Code);
public sealed record AppReport(string Path, bool Exists, long Size, bool LooksLikeExecutable, long? DriveFreeBytes,
    bool? VcRuntimePresent, IReadOnlyList<ErrorEvent> Crashes);

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class StartupManager
{
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private static readonly string[] ProtectedWords = ["defender", "securityhealth", "windows security", "antivirus", "security"];

    private static RegistryHive HiveOf(string location) => location == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;

    // Protected entries (Windows components and security software) are never offered for change.
    public static bool IsProtected(string name, string command) =>
        ProtectedWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))
        || (StartupInspector.ExtractPath(command) is { } path
            && path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase));

    public static bool IsEnabled(string location, string name)
        => TryGetEnabled(location, name, out var enabled) ? enabled : true;

    public static bool TryGetEnabled(string location, string name, out bool enabled)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(HiveOf(location), RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(ApprovedKey, false);
            enabled = ReadEnabled(key, name);
            return true;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            enabled = false;
            return false;
        }
    }

    // Uses the same StartupApproved flag as Task Manager, so the Run entry itself is never deleted.
    public static bool SetEnabled(string location, string name, bool enabled) =>
        TryGetEnabled(location, name, out var current) && SetEnabled(location, name, current, enabled);

    public static bool SetEnabled(string location, string name, bool expected, bool enabled)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(HiveOf(location), RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(ApprovedKey, true);
            if (ReadEnabled(key, name) != expected) return false;
            var data = new byte[12];
            data[0] = enabled ? (byte)2 : (byte)3;
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            key.SetValue(name, data, RegistryValueKind.Binary);
            return ReadEnabled(key, name) == enabled;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool ReadEnabled(RegistryKey? key, string name) =>
        key?.GetValue(name) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
}

public sealed class StartupJournal(string path)
{
    public IReadOnlyList<StartupChange> Load()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<StartupChange>>(File.ReadAllText(path)) ?? [] : []; }
        catch (Exception exception) when (exception is IOException or JsonException) { return []; }
    }

    public void Add(StartupChange change)
    {
        var list = Load().ToList();
        list.Add(change);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(list.TakeLast(100)));
    }

    public void Remove(StartupChange change)
    {
        var list = Load().Where(c => c != change).ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(list));
    }
}

public static class DeviceInspector
{
    // Fixed command, no user input. Reports Device Manager error codes only; it never installs or removes drivers.
    private const string Script = "Get-CimInstance Win32_PnPEntity | Where-Object { $_.ConfigManagerErrorCode -ne 0 } | Select-Object Name,ConfigManagerErrorCode | ConvertTo-Json -Compress";

    public static async Task<IReadOnlyList<DeviceProblem>?> ReadAsync(ICommandRunner runner, CancellationToken ct)
    {
        var output = await runner.RunAsync("powershell", ["-NoProfile", "-NonInteractive", "-Command", Script], TimeSpan.FromSeconds(45), Encoding.UTF8, ct);
        if (output.TimedOut || output.ExitCode != 0) return null;
        return Parse(output.Output);
    }

    public static IReadOnlyList<DeviceProblem> Parse(string json)
    {
        var text = json.Trim();
        if (text.Length == 0) return [];
        try
        {
            using var doc = JsonDocument.Parse(text);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
            return items
                .Where(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("ConfigManagerErrorCode", out _))
                .Select(i => new DeviceProblem(i.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "", i.GetProperty("ConfigManagerErrorCode").GetInt32()))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public static partial class AppTroubleshooter
{
    [GeneratedRegex(@"^[A-Za-z0-9_\-. ]{1,100}\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex SafeExeName();

    public static bool IsSafeExeName(string name) => SafeExeName().IsMatch(name);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static async Task<AppReport> InspectAsync(string path, ICommandRunner runner, CancellationToken ct)
    {
        var info = new FileInfo(path);
        var exists = info.Exists;
        var pe = false;
        long? free = null;
        if (exists)
        {
            try
            {
                using var stream = info.OpenRead();
                var head = new byte[2];
                pe = stream.Read(head, 0, 2) == 2 && head[0] == 'M' && head[1] == 'Z';
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { pe = false; }
            try { free = new DriveInfo(Path.GetPathRoot(info.FullName)!).AvailableFreeSpace; }
            catch (Exception exception) when (exception is IOException or ArgumentException) { free = null; }
        }
        var crashes = new List<ErrorEvent>();
        if (exists && IsSafeExeName(info.Name))
        {
            var query = $"*[System[(EventID=1000)]] and *[EventData[Data='{info.Name}']]";
            var output = await runner.RunAsync("wevtutil", ["qe", "Application", "/q:" + query, "/c:10", "/rd:true", "/f:xml"], TimeSpan.FromSeconds(30), Encoding.UTF8, ct);
            if (output.ExitCode == 0 && !output.TimedOut) crashes.AddRange(CrashLab.Parse(output.Output, "Application"));
        }
        return new AppReport(info.FullName, exists, exists ? info.Length : 0, pe, free, VcRuntime(), crashes);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool? VcRuntime()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64", false);
            return key is null ? false : (key.GetValue("Installed") as int?) == 1;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException) { return null; }
    }
}

public sealed record MaintProfile(string Name, string[] CheckIds, int IntervalHours);

public sealed class MaintProfileStore(string path)
{
    public static readonly MaintProfile[] Defaults =
    [
        new("General Use", ["os", "disk", "temp", "nivex", "adapter", "dns", "internet"], 24),
        new("Gaming PC", ["os", "disk", "temp", "adapter", "dns", "internet", "gateway"], 12),
        new("Workstation", ["os", "disk", "temp", "nivex", "adapter", "dns", "internet", "sfc", "dism"], 24),
    ];

    public List<MaintProfile> Load()
    {
        try
        {
            if (File.Exists(path))
            {
                var list = JsonSerializer.Deserialize<List<MaintProfile>>(File.ReadAllText(path));
                if (list is { Count: > 0 }) return Sanitize(list);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException) { }
        return [.. Defaults];
    }

    public void Save(IEnumerable<MaintProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(Sanitize(profiles.ToList())));
    }

    // Only known check ids survive, so a hand-edited file cannot inject anything into a scan.
    public static List<MaintProfile> Sanitize(List<MaintProfile> list) => list
        .Where(p => !string.IsNullOrWhiteSpace(p.Name))
        .Select(p => new MaintProfile(p.Name.Trim()[..Math.Min(40, p.Name.Trim().Length)],
            p.CheckIds.Where(id => FixService.AllIds.Contains(id)).Distinct().ToArray(), Math.Clamp(p.IntervalHours, 1, 168)))
        .Where(p => p.CheckIds.Length > 0)
        .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

    public static MaintProfile Duplicate(MaintProfile source, IEnumerable<MaintProfile> existing)
    {
        var names = existing.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = source.Name + " copy";
        for (var i = 2; names.Contains(name); i++) name = $"{source.Name} copy {i}";
        return source with { Name = name };
    }
}
