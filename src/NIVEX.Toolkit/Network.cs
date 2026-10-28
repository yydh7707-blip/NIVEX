using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NIVEX.Toolkit;

public sealed record PingSample(bool Ok, double Ms, string Status);
public sealed record PingStats(string Host, int Sent, int Received, double? MinMs, double? AvgMs, double? MaxMs, double LossPercent, string? Note);
public sealed record DnsResult(string Domain, bool Ok, double Ms, IReadOnlyList<string> Addresses, string? Error);
public sealed record AdapterInfo(string Name, string Kind, bool Up, IReadOnlyList<string> Addresses, IReadOnlyList<string> Gateways, IReadOnlyList<string> DnsServers);
public sealed record NetworkIssue(string Code, string Message, string? FixId);
public sealed record NetworkReport(DateTimeOffset At, IReadOnlyList<AdapterInfo> Adapters, PingStats? Gateway, IReadOnlyList<PingStats> External, DnsResult Dns, IReadOnlyList<NetworkIssue> Issues);
public sealed record CompareLine(string Metric, string Previous, string Current);

public interface IPinger
{
    Task<PingSample> PingAsync(string host, int timeoutMs, CancellationToken ct);
}

public interface IDnsResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string domain, CancellationToken ct);
}

public sealed class SystemPinger : IPinger
{
    public async Task<PingSample> PingAsync(string host, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken: ct);
            return new PingSample(reply.Status == IPStatus.Success, reply.RoundtripTime, reply.Status.ToString());
        }
        catch (PingException exception)
        {
            return new PingSample(false, 0, exception.InnerException is SocketException ? "HostNotFound" : "Error");
        }
    }
}

public sealed class SystemDnsResolver : IDnsResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string domain, CancellationToken ct) =>
        await Dns.GetHostAddressesAsync(domain, ct);
}

public sealed class NetworkLab(IPinger pinger, IDnsResolver resolver, Func<IReadOnlyList<AdapterInfo>>? adapters = null)
{
    public static readonly string[] ExternalTargets = ["1.1.1.1", "8.8.8.8"];
    private readonly Func<IReadOnlyList<AdapterInfo>> _adapters = adapters ?? ReadAdapters;

    public static bool TryValidateHost(string? input, out string host, out string error)
    {
        host = (input ?? "").Trim();
        error = "";
        if (host.Length == 0) { error = "empty"; return false; }
        if (host.Length > 253) { error = "too long"; return false; }
        if (host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '\\' or '@' or '?' or '#')) { error = "invalid characters"; return false; }
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown) { error = "invalid host"; return false; }
        return true;
    }

    public async Task<PingStats> PingAsync(string host, int count, int timeoutMs, CancellationToken ct)
    {
        if (!TryValidateHost(host, out var clean, out var error)) throw new ArgumentException(error, nameof(host));
        count = Math.Clamp(count, 1, 10);
        timeoutMs = Math.Clamp(timeoutMs, 200, 5000);
        var times = new List<double>();
        string? lastStatus = null;
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sample = await pinger.PingAsync(clean, timeoutMs, ct);
            lastStatus = sample.Status;
            if (sample.Ok) times.Add(sample.Ms);
            if (i < count - 1) await Task.Delay(250, ct);
        }
        var loss = (count - times.Count) * 100.0 / count;
        string? note = null;
        if (times.Count == 0)
            note = $"No reply ({lastStatus}). The target may block ICMP, so the cause cannot be determined from this test alone.";
        else if (times.Count < count)
            note = "Some replies were lost. A single short test cannot identify the cause.";
        return new PingStats(clean, count, times.Count,
            times.Count == 0 ? null : times.Min(), times.Count == 0 ? null : times.Average(), times.Count == 0 ? null : times.Max(), loss, note);
    }

    public async Task<DnsResult> DnsAsync(string domain, CancellationToken ct)
    {
        if (!TryValidateHost(domain, out var clean, out var error)) return new DnsResult(domain, false, 0, [], error);
        var watch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var addresses = await resolver.ResolveAsync(clean, timeout.Token);
            watch.Stop();
            return new DnsResult(clean, addresses.Count > 0, watch.Elapsed.TotalMilliseconds, addresses.Select(a => a.ToString()).Take(6).ToList(),
                addresses.Count > 0 ? null : "no records");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DnsResult(clean, false, watch.Elapsed.TotalMilliseconds, [], "timeout");
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return new DnsResult(clean, false, watch.Elapsed.TotalMilliseconds, [], exception is SocketException s ? s.SocketErrorCode.ToString() : "invalid");
        }
    }

    public async Task<NetworkReport> DiagnoseAsync(string dnsDomain, CancellationToken ct)
    {
        var adapters = _adapters();
        var issues = new List<NetworkIssue>();
        var active = adapters.Where(a => a.Up && a.Addresses.Count > 0).ToList();
        if (active.Count == 0) issues.Add(new NetworkIssue("adapter", "No active network adapter with an IP address was found.", null));
        PingStats? gateway = null;
        var gatewayIp = active.SelectMany(a => a.Gateways).FirstOrDefault();
        if (gatewayIp is not null)
        {
            gateway = await PingAsync(gatewayIp, 3, 1500, ct);
            if (gateway.Received == 0) issues.Add(new NetworkIssue("gateway", "The default gateway did not answer. Some routers ignore ICMP.", null));
        }
        else if (active.Count > 0) issues.Add(new NetworkIssue("gateway", "No default gateway is configured.", null));
        var external = new List<PingStats>();
        foreach (var target in ExternalTargets) external.Add(await PingAsync(target, 3, 2000, ct));
        if (external.All(e => e.Received == 0)) issues.Add(new NetworkIssue("internet", "Neither public test server answered.", "internet"));
        var dns = await DnsAsync(dnsDomain, ct);
        if (!dns.Ok) issues.Add(new NetworkIssue("dns", $"DNS lookup failed ({dns.Error}).", "flush-dns"));
        return new NetworkReport(DateTimeOffset.Now, adapters, gateway, external, dns, issues);
    }

    public static IReadOnlyList<CompareLine> Compare(NetworkReport? previous, NetworkReport current)
    {
        if (previous is null) return [];
        var lines = new List<CompareLine>
        {
            new("DNS", Ms(previous.Dns.Ok ? previous.Dns.Ms : null), Ms(current.Dns.Ok ? current.Dns.Ms : null)),
            new("Gateway", Ms(previous.Gateway?.AvgMs), Ms(current.Gateway?.AvgMs)),
        };
        foreach (var target in current.External)
        {
            var before = previous.External.FirstOrDefault(e => e.Host == target.Host);
            lines.Add(new CompareLine(target.Host, before is null ? "—" : $"{Ms(before.AvgMs)} / {before.LossPercent:0}%", $"{Ms(target.AvgMs)} / {target.LossPercent:0}%"));
        }
        return lines;
    }

    private static string Ms(double? value) => value is null ? "—" : $"{value.Value:0} ms";

    public static IReadOnlyList<AdapterInfo> ReadAdapters()
    {
        var list = new List<AdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                var props = nic.GetIPProperties();
                list.Add(new AdapterInfo(nic.Name, nic.NetworkInterfaceType.ToString(), nic.OperationalStatus == OperationalStatus.Up,
                    props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToList(),
                    props.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Select(g => g.Address.ToString()).ToList(),
                    props.DnsAddresses.Select(d => d.ToString()).ToList()));
            }
            catch (NetworkInformationException)
            {
                list.Add(new AdapterInfo(nic.Name, nic.NetworkInterfaceType.ToString(), false, [], [], []));
            }
        }
        return list;
    }
}
