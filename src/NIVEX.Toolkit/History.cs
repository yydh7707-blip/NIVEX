using System.Net;
using System.Text;
using System.Text.Json;

namespace NIVEX.Toolkit;

public enum HistoryKind { Fix, Repair, Network, Monitor, Flow, Profile }

public sealed class HistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
    public HistoryKind Kind { get; set; }
    public string Title { get; set; } = "";
    public bool Success { get; set; }
    public string Summary { get; set; } = "";
    public string? ErrorType { get; set; }
    public string? Detail { get; set; }
    public string? Advice { get; set; }
}

public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly int _max;
    private readonly object _gate = new();
    private List<HistoryRecord>? _cache;

    public HistoryStore(string path, int max = 1000)
    {
        _path = path;
        _max = Math.Max(10, max);
    }

    public string? LastError { get; private set; }
    public bool RecoveredFromCorruption { get; private set; }

    public IReadOnlyList<HistoryRecord> All()
    {
        lock (_gate) return Load().OrderByDescending(r => r.Time).ToList();
    }

    public bool Add(HistoryRecord record)
    {
        lock (_gate)
        {
            var list = Load();
            if (list.Any(r => r.Id == record.Id)) return false;
            record.Summary = Clip(record.Summary, 600);
            record.Detail = record.Detail is null ? null : Clip(record.Detail, 4000);
            list.Add(record);
            if (list.Count > _max) list = list.OrderByDescending(r => r.Time).Take(_max).ToList();
            _cache = list;
            return Save(list);
        }
    }

    public bool Clear()
    {
        lock (_gate)
        {
            _cache = new List<HistoryRecord>();
            return Save(_cache);
        }
    }

    public int RemoveOlderThan(DateTimeOffset cutoff)
    {
        lock (_gate)
        {
            var list = Load();
            var removed = list.RemoveAll(r => r.Time < cutoff);
            if (removed > 0) Save(list);
            return removed;
        }
    }

    public static IEnumerable<HistoryRecord> Query(IEnumerable<HistoryRecord> source, DateTimeOffset? from, DateTimeOffset? to,
        HistoryKind? kind, bool? success, string? keyword)
    {
        var query = source;
        if (from is not null) query = query.Where(r => r.Time >= from);
        if (to is not null) query = query.Where(r => r.Time <= to);
        if (kind is not null) query = query.Where(r => r.Kind == kind);
        if (success is not null) query = query.Where(r => r.Success == success);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var word = keyword.Trim();
            query = query.Where(r => r.Title.Contains(word, StringComparison.OrdinalIgnoreCase)
                || r.Summary.Contains(word, StringComparison.OrdinalIgnoreCase)
                || (r.Detail?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        return query;
    }

    private List<HistoryRecord> Load()
    {
        if (_cache is not null) return _cache;
        _cache = new List<HistoryRecord>();
        try
        {
            if (!File.Exists(_path)) return _cache;
            var parsed = JsonSerializer.Deserialize<List<HistoryRecord>>(File.ReadAllText(_path), Json);
            if (parsed is null) throw new JsonException("empty");
            var seen = new HashSet<string>();
            foreach (var record in parsed)
            {
                if (string.IsNullOrWhiteSpace(record.Id) || !Enum.IsDefined(record.Kind)) throw new JsonException("invalid record");
                if (seen.Add(record.Id)) _cache.Add(record);
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _cache = new List<HistoryRecord>();
            RecoveredFromCorruption = exception is JsonException;
            LastError = exception.Message;
            TryQuarantine();
        }
        return _cache;
    }

    private void TryQuarantine()
    {
        try
        {
            if (File.Exists(_path)) File.Move(_path, _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
        }
        catch (Exception)
        {
            // keep running with an empty history
        }
    }

    private bool Save(List<HistoryRecord> list)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(list, Json));
            File.Move(temp, _path, true);
            LastError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = exception.Message;
            return false;
        }
    }

    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];
}

public static class ReportBuilder
{
    public static string BuildHtml(IReadOnlyList<HistoryRecord> records, string title, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, bool korean)
    {
        string L(string ko, string en) => korean ? ko : en;
        var measured = records.Where(r => r.Kind is not HistoryKind.Repair).OrderBy(r => r.Time).ToList();
        var problems = records.Where(r => !r.Success && r.Kind is not HistoryKind.Repair).OrderBy(r => r.Time).ToList();
        var repairs = records.Where(r => r.Kind == HistoryKind.Repair).OrderBy(r => r.Time).ToList();
        var advice = records.Where(r => !string.IsNullOrWhiteSpace(r.Advice)).Select(r => r.Advice!).Distinct().ToList();
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(korean ? "ko" : "en").Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(E(title)).Append("</title><style>body{background:#000;color:#f4f6f8;font:14px/1.6 'Segoe UI',sans-serif;max-width:860px;margin:40px auto;padding:0 20px}")
            .Append("h1{font-weight:600;letter-spacing:.02em}h2{margin-top:32px;border-bottom:1px solid #2a2a2a;padding-bottom:6px;font-size:16px}")
            .Append("table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:6px 8px;border-bottom:1px solid #1c1c1c;vertical-align:top}")
            .Append(".ok{color:#d8dde3}.bad{color:#fff;font-weight:600}.muted{color:#8a9098}</style></head><body>");
        html.Append("<h1>").Append(E(title)).Append("</h1><p class=\"muted\">")
            .Append(E(L("생성 시각", "Generated"))).Append(": ").Append(now.ToString("yyyy-MM-dd HH:mm")).Append(" · ")
            .Append(E(L("기간", "Period"))).Append(": ").Append(from.ToString("yyyy-MM-dd")).Append(" – ").Append(to.ToString("yyyy-MM-dd")).Append("</p>");
        if (records.Count == 0)
        {
            html.Append("<p>").Append(E(L("선택한 기간에 저장된 기록이 없습니다.", "No records were stored for the selected period."))).Append("</p></body></html>");
            return html.ToString();
        }
        Section(html, L("측정 및 검사 결과 (실제 기록)", "Measured and checked results (recorded)"), measured, korean);
        html.Append("<h2>").Append(E(L("발견된 문제", "Problems found"))).Append("</h2>");
        if (problems.Count == 0) html.Append("<p class=\"muted\">").Append(E(L("기록된 문제가 없습니다. 기록되지 않은 항목은 검사하지 않은 것입니다.", "No problems were recorded. Items without a record were not checked."))).Append("</p>");
        else Section(html, null, problems, korean);
        html.Append("<h2>").Append(E(L("수행한 복구 작업", "Repairs performed"))).Append("</h2>");
        if (repairs.Count == 0) html.Append("<p class=\"muted\">").Append(E(L("수행한 복구가 없습니다.", "No repairs were performed."))).Append("</p>");
        else Section(html, null, repairs, korean);
        html.Append("<h2>").Append(E(L("후속 권장 조치 (측정값이 아닌 권장 사항)", "Recommended follow-up (advice, not measurements)"))).Append("</h2>");
        if (advice.Count == 0) html.Append("<p class=\"muted\">—</p>");
        else
        {
            html.Append("<ul>");
            foreach (var item in advice) html.Append("<li>").Append(E(item)).Append("</li>");
            html.Append("</ul>");
        }
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void Section(StringBuilder html, string? heading, List<HistoryRecord> rows, bool korean)
    {
        if (heading is not null) html.Append("<h2>").Append(E(heading)).Append("</h2>");
        html.Append("<table><tr><th>").Append(korean ? "시각" : "Time").Append("</th><th>").Append(korean ? "종류" : "Type")
            .Append("</th><th>").Append(korean ? "항목" : "Item").Append("</th><th>").Append(korean ? "결과" : "Result").Append("</th></tr>");
        foreach (var row in rows)
        {
            html.Append("<tr><td>").Append(row.Time.ToString("MM-dd HH:mm")).Append("</td><td>").Append(row.Kind)
                .Append("</td><td>").Append(E(row.Title)).Append("</td><td class=\"").Append(row.Success ? "ok" : "bad").Append("\">")
                .Append(row.Success ? (korean ? "성공" : "OK") : (korean ? "실패/문제" : "Problem")).Append(" — ").Append(E(row.Summary)).Append("</td></tr>");
        }
        html.Append("</table>");
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}

public static class ReportWriter
{
    public static (bool Ok, string? Error) Save(string path, string html, bool overwrite)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return (false, "invalid path");
            if (!string.Equals(Path.GetExtension(path), ".html", StringComparison.OrdinalIgnoreCase)) return (false, "extension must be .html");
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return (false, "folder not found");
            if (File.Exists(path) && !overwrite) return (false, "file exists");
            var temp = path + ".nivex-tmp";
            File.WriteAllText(temp, html, new UTF8Encoding(false));
            File.Move(temp, path, overwrite);
            return (true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (false, exception.Message);
        }
    }
}
