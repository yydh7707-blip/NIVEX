using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace NIVEX.Toolkit;

public enum Certainty { Confirmed, Possible, Undetermined }
public enum RiskLevel { Low, Medium, High }
public enum Verdict { Resolved, Improved, Unresolved, Unverifiable, RepairFailed }

public sealed record Finding(string CheckId, Certainty Certainty, string Evidence, string[] RelatedIds, string? RepairId, RiskLevel Risk);
public sealed record CheckComparison(string CheckId, CheckStatus Before, CheckStatus After, Verdict Verdict);
public sealed record StartupEntry(string Name, string Command, string Location, bool PathExists);
public sealed record ErrorEvent(DateTimeOffset Time, string Log, string Provider, int EventId);
public sealed record DumpFile(string Path, long Size, DateTimeOffset Created);
public sealed record LargeFile(string Path, long Size);
public sealed record LargeFileScan(IReadOnlyList<LargeFile> Files, int Inaccessible);

public static class RootCauseAnalyzer
{
    // Rule-based and local. Every finding cites the check result it came from;
    // causes that the data cannot separate stay Undetermined.
    public static IReadOnlyList<Finding> Analyze(IReadOnlyList<CheckResult> results)
    {
        CheckResult? Get(string id) => results.FirstOrDefault(r => r.Id == id);
        bool Is(string id, CheckStatus status) => Get(id)?.Status == status;
        var findings = new List<Finding>();

        foreach (var result in results.Where(r => r.Status == CheckStatus.Problem))
        {
            switch (result.Id)
            {
                case "disk":
                    findings.Add(new Finding("disk", Certainty.Confirmed, result.Message,
                        Is("temp", CheckStatus.Problem) ? ["temp"] : [], "clean-temp", RiskLevel.Medium));
                    break;
                case "temp":
                    findings.Add(new Finding("temp", Certainty.Confirmed, result.Message, [], "clean-temp", RiskLevel.Medium));
                    break;
                case "adapter":
                    findings.Add(new Finding("adapter", Certainty.Confirmed, result.Message, [], null, RiskLevel.Low));
                    break;
                case "dns":
                    if (Is("internet", CheckStatus.Healthy))
                        findings.Add(new Finding("dns", Certainty.Possible, result.Message, ["internet"], "flush-dns", RiskLevel.Low));
                    else
                        findings.Add(new Finding("dns", Certainty.Undetermined, result.Message,
                            Get("internet") is null ? [] : ["internet", "adapter"], "flush-dns", RiskLevel.Low));
                    break;
                case "internet":
                    if (!Is("dns", CheckStatus.Problem))
                        findings.Add(new Finding("internet", Certainty.Undetermined, result.Message, ["dns", "adapter"], null, RiskLevel.Low));
                    break;
                case "sfc" or "dism":
                    findings.Add(new Finding(result.Id, Certainty.Confirmed, result.Message, [], null, RiskLevel.High));
                    break;
                case "nivex":
                    findings.Add(new Finding("nivex", Certainty.Confirmed, result.Message, [], null, RiskLevel.Low));
                    break;
                default:
                    findings.Add(new Finding(result.Id, Certainty.Undetermined, result.Message, [], result.RepairId, RiskLevel.Low));
                    break;
            }
        }
        return findings.OrderBy(f => f.Certainty).ThenByDescending(f => f.Risk).ToList();
    }
}

public static class BeforeAfter
{
    public static CheckComparison Compare(CheckResult before, CheckResult? after, bool repairFailed = false)
    {
        if (repairFailed) return new CheckComparison(before.Id, before.Status, after?.Status ?? CheckStatus.Unknown, Verdict.RepairFailed);
        if (after is null || after.Status is CheckStatus.Unknown or CheckStatus.Failed)
            return new CheckComparison(before.Id, before.Status, after?.Status ?? CheckStatus.Unknown, Verdict.Unverifiable);
        var verdict = (before.Status, after.Status) switch
        {
            (CheckStatus.Problem, CheckStatus.Healthy) => Verdict.Resolved,
            (CheckStatus.Problem, CheckStatus.Problem) => Verdict.Unresolved,
            (CheckStatus.Healthy, CheckStatus.Healthy) => Verdict.Resolved,
            _ => Verdict.Unverifiable,
        };
        return new CheckComparison(before.Id, before.Status, after.Status, verdict);
    }

    // Disk free space can improve without leaving the Problem threshold.
    public static Verdict WithFreedSpace(Verdict verdict, long freedBytes) =>
        verdict == Verdict.Unresolved && freedBytes > 0 ? Verdict.Improved : verdict;
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class StartupInspector
{
    private static readonly (RegistryHive Hive, string Label)[] Hives =
        [(RegistryHive.CurrentUser, "HKCU"), (RegistryHive.LocalMachine, "HKLM")];

    // Read-only: lists Run-key entries and whether the target file exists. Nothing is changed.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static IReadOnlyList<StartupEntry> Read()
    {
        var entries = new List<StartupEntry>();
        foreach (var (hive, label) in Hives)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    var command = key.GetValue(name) as string ?? "";
                    entries.Add(new StartupEntry(name, command, label, TargetExists(command)));
                }
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // unreadable hive: skipped
            }
        }
        return entries.OrderBy(e => e.PathExists).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? ExtractPath(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.Length == 0) return null;
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }
        if (File.Exists(text)) return text;
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? text[..(exe + 4)] : text.Split(' ')[0];
    }

    private static bool TargetExists(string command)
    {
        var path = ExtractPath(command);
        return path is not null && File.Exists(path);
    }
}

public sealed class CrashLab(ICommandRunner runner)
{
    private const string ApplicationQuery = "*[System[(Level=2) and (EventID=1000 or EventID=1001)]]";
    private const string SystemQuery = "*[System[(Level=1 or Level=2)]]";

    // Reads only the newest error records of the Application and System logs through wevtutil.
    public async Task<IReadOnlyList<ErrorEvent>> ReadAsync(int perLog, CancellationToken ct)
    {
        var all = new List<ErrorEvent>();
        foreach (var (log, query) in new[] { ("Application", ApplicationQuery), ("System", SystemQuery) })
        {
            var output = await runner.RunAsync("wevtutil",
                ["qe", log, "/q:" + query, "/c:" + perLog, "/rd:true", "/f:xml"], TimeSpan.FromSeconds(30), Encoding.UTF8, ct);
            if (output.ExitCode != 0 || output.TimedOut) continue;
            all.AddRange(Parse(output.Output, log));
        }
        return all.OrderByDescending(e => e.Time).ToList();
    }

    public static IReadOnlyList<ErrorEvent> Parse(string xml, string log)
    {
        var events = new List<ErrorEvent>();
        try
        {
            var root = XElement.Parse("<r>" + xml + "</r>");
            foreach (var item in root.Elements().Where(e => e.Name.LocalName == "Event"))
            {
                var system = item.Elements().FirstOrDefault(e => e.Name.LocalName == "System");
                if (system is null) continue;
                string? Value(string name) => system.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
                var provider = system.Elements().FirstOrDefault(e => e.Name.LocalName == "Provider")?.Attribute("Name")?.Value ?? "";
                var stamp = system.Elements().FirstOrDefault(e => e.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
                if (!int.TryParse(Value("EventID"), out var id) || !DateTimeOffset.TryParse(stamp, out var time)) continue;
                events.Add(new ErrorEvent(time, log, provider, id));
            }
        }
        catch (System.Xml.XmlException)
        {
            // malformed output yields no records
        }
        return events;
    }

    public static IReadOnlyList<DumpFile> ReadDumps()
    {
        var dumps = new List<DumpFile>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var path in new[] { Path.Combine(windows, "Minidump"), windows })
        {
            try
            {
                if (!Directory.Exists(path)) continue;
                foreach (var file in Directory.EnumerateFiles(path, "*.dmp", SearchOption.TopDirectoryOnly))
                {
                    var info = new FileInfo(file);
                    dumps.Add(new DumpFile(file, info.Length, info.CreationTimeUtc));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // protected folder: skipped
            }
        }
        return dumps.OrderByDescending(d => d.Created).ToList();
    }
}

public static class LargeFileFinder
{
    public static LargeFileScan Find(string root, int top, long minBytes, CancellationToken ct)
    {
        var files = new List<LargeFile>();
        var denied = 0;
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
                    if (info.Length >= minBytes) files.Add(new LargeFile(path, info.Length));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                denied++;
            }
        }
        return new LargeFileScan(files.OrderByDescending(f => f.Size).Take(top).ToList(), denied);
    }
}

public static class FixReport
{
    public sealed record Labels(
        string Title, string Generated, string Facts, string Findings, string Recommendation, string Unresolved,
        string Checks, string Errors, string Note, Func<CheckResult, string> CheckName, Func<CheckStatus, string> Status,
        Func<Finding, string> FindingName, Func<Certainty, string> CertaintyName, Func<Finding, string> Advice);

    // Only check results and Windows error records are included. Activation codes, tokens, file paths
    // and startup commands are never part of the report.
    public static string Html(Labels l, IReadOnlyList<CheckResult> results, IReadOnlyList<Finding> findings,
        IReadOnlyList<ErrorEvent> errors, string version, DateTimeOffset when)
    {
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var b = new StringBuilder();
        b.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E(l.Title)).Append("</title><style>")
         .Append("body{font:14px/1.6 Segoe UI,Arial,sans-serif;background:#fff;color:#111;max-width:860px;margin:32px auto;padding:0 20px}")
         .Append("h1{font-size:22px}h2{font-size:15px;margin-top:28px;border-bottom:1px solid #ccc;padding-bottom:4px}")
         .Append("table{border-collapse:collapse;width:100%}td,th{text-align:left;padding:6px 8px;border-bottom:1px solid #e4e4e4;vertical-align:top}")
         .Append(".m{color:#555}</style></head><body>");
        b.Append("<h1>").Append(E(l.Title)).Append("</h1><p class=\"m\">")
         .Append(E(l.Generated)).Append(": ").Append(E(when.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))).Append(" · NIVEX v").Append(E(version))
         .Append(" · ").Append(E(System.Runtime.InteropServices.RuntimeInformation.OSDescription)).Append("</p>");

        b.Append("<h2>").Append(E(l.Findings)).Append("</h2>");
        if (findings.Count == 0) b.Append("<p>—</p>");
        foreach (var f in findings)
        {
            b.Append("<p><strong>").Append(E(l.FindingName(f))).Append("</strong> <span class=\"m\">(")
             .Append(E(l.CertaintyName(f.Certainty))).Append(")</span><br>")
             .Append(E(f.Evidence)).Append("<br><span class=\"m\">").Append(E(l.Recommendation)).Append(": ")
             .Append(E(l.Advice(f))).Append("</span></p>");
        }

        b.Append("<h2>").Append(E(l.Checks)).Append("</h2><table>");
        foreach (var r in results)
            b.Append("<tr><td>").Append(E(l.CheckName(r))).Append("</td><td>").Append(E(l.Status(r.Status)))
             .Append("</td><td class=\"m\">").Append(E(r.Message)).Append("</td></tr>");
        b.Append("</table>");

        b.Append("<h2>").Append(E(l.Unresolved)).Append("</h2>");
        var open = results.Where(r => r.Status is CheckStatus.Problem or CheckStatus.Failed).ToList();
        b.Append(open.Count == 0 ? "<p>—</p>" : "<ul>" + string.Concat(open.Select(r => "<li>" + E(l.CheckName(r)) + "</li>")) + "</ul>");

        b.Append("<h2>").Append(E(l.Errors)).Append("</h2>");
        if (errors.Count == 0) b.Append("<p>—</p>");
        else
        {
            b.Append("<table>");
            foreach (var e in errors.Take(20))
                b.Append("<tr><td>").Append(E(e.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))).Append("</td><td>").Append(E(e.Log))
                 .Append("</td><td>").Append(E(e.Provider)).Append("</td><td>").Append(e.EventId).Append("</td></tr>");
            b.Append("</table>");
        }
        b.Append("<p class=\"m\">").Append(E(l.Note)).Append("</p></body></html>");
        return b.ToString();
    }
}
