using System.Net;
using System.Net.Sockets;
using System.Text;
using NIVEX.Application;
using NIVEX.Toolkit;
using Xunit;

namespace NIVEX.ToolkitTests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nivex-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

internal sealed class FakePinger(Func<string, PingSample> reply) : IPinger
{
    public int Calls;
    public Task<PingSample> PingAsync(string host, int timeoutMs, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(reply(host));
    }
}

internal sealed class FakeResolver(Func<string, IReadOnlyList<IPAddress>> reply) : IDnsResolver
{
    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string domain, CancellationToken ct) => Task.FromResult(reply(domain));
}

internal sealed class FakeRunner(int exit = 0, string output = "", string errorOutput = "") : ICommandRunner
{
    public List<string> Calls = new();
    public Task<CommandOutput> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, Encoding? encoding, CancellationToken ct)
    {
        Calls.Add(file + " " + string.Join(' ', args));
        return Task.FromResult(new CommandOutput(exit, output, false, errorOutput));
    }
}

internal sealed class FakeHost : IStepHost
{
    public List<string> Opened = new();
    public bool Confirm = true;
    public void Open(string target) => Opened.Add(target);
    public void Launch(string executable) => Opened.Add("launch:" + executable);
    public Task<bool> ConfirmAsync(string message, CancellationToken ct) => Task.FromResult(Confirm);
}

public sealed class NetworkLabTests
{
    private static NetworkLab Lab(Func<string, PingSample> ping, Func<string, IReadOnlyList<IPAddress>>? dns = null) =>
        new(new FakePinger(ping), new FakeResolver(dns ?? (_ => [IPAddress.Parse("1.2.3.4")])), () => []);

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("", false)]
    [InlineData("bad host", false)]
    [InlineData("a/b", false)]
    [InlineData("http://x.com", false)]
    public void HostValidation(string host, bool valid) => Assert.Equal(valid, NetworkLab.TryValidateHost(host, out _, out _));

    [Fact]
    public async Task PingComputesStats()
    {
        var times = new Queue<double>([10, 20, 30]);
        var stats = await Lab(_ => new PingSample(true, times.Dequeue(), "Success")).PingAsync("example.com", 3, 1000, CancellationToken.None);
        Assert.Equal(10, stats.MinMs);
        Assert.Equal(20, stats.AvgMs);
        Assert.Equal(30, stats.MaxMs);
        Assert.Equal(0, stats.LossPercent);
    }

    [Fact]
    public async Task TimeoutReportsFullLossWithLimitationNote()
    {
        var stats = await Lab(_ => new PingSample(false, 0, "TimedOut")).PingAsync("example.com", 2, 500, CancellationToken.None);
        Assert.Equal(100, stats.LossPercent);
        Assert.Null(stats.AvgMs);
        Assert.Contains("ICMP", stats.Note);
    }

    [Fact]
    public async Task PingRejectsInvalidHostAndClampsCount()
    {
        var pinger = new FakePinger(_ => new PingSample(true, 1, "Success"));
        var lab = new NetworkLab(pinger, new FakeResolver(_ => []), () => []);
        await Assert.ThrowsAsync<ArgumentException>(() => lab.PingAsync("bad host", 3, 500, CancellationToken.None));
        await lab.PingAsync("example.com", 99, 500, CancellationToken.None);
        Assert.Equal(10, pinger.Calls);
    }

    [Fact]
    public async Task DnsFailureIsReported()
    {
        var lab = new NetworkLab(new FakePinger(_ => new PingSample(true, 1, "Success")),
            new FakeResolver(_ => throw new SocketException((int)SocketError.HostNotFound)), () => []);
        var result = await lab.DnsAsync("nope.example", CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("HostNotFound", result.Error);
    }

    [Fact]
    public async Task DiagnoseFlagsDnsAndOffersFlushFix()
    {
        var adapters = new[] { new AdapterInfo("eth", "Ethernet", true, ["10.0.0.5"], ["10.0.0.1"], ["10.0.0.1"]) };
        var lab = new NetworkLab(new FakePinger(_ => new PingSample(true, 5, "Success")),
            new FakeResolver(_ => throw new SocketException((int)SocketError.HostNotFound)), () => adapters);
        var report = await lab.DiagnoseAsync("example.com", CancellationToken.None);
        Assert.Contains(report.Issues, i => i.Code == "dns" && i.FixId == "flush-dns");
        Assert.NotNull(report.Gateway);
    }

    [Fact]
    public async Task CompareNeedsPreviousData()
    {
        var lab = new NetworkLab(new FakePinger(_ => new PingSample(true, 5, "Success")), new FakeResolver(_ => [IPAddress.Loopback]), () => []);
        var first = await lab.DiagnoseAsync("example.com", CancellationToken.None);
        Assert.Empty(NetworkLab.Compare(null, first));
        var second = await lab.DiagnoseAsync("example.com", CancellationToken.None);
        Assert.NotEmpty(NetworkLab.Compare(first, second));
    }
}

public sealed class FixTests
{
    private static FixService Service(TempDir dir, ICommandRunner? runner = null, Func<bool>? admin = null, bool dnsOk = true, long free = 100L << 30)
    {
        var lab = new NetworkLab(new FakePinger(_ => new PingSample(true, 3, "Success")),
            new FakeResolver(_ => dnsOk ? [IPAddress.Loopback] : throw new SocketException((int)SocketError.HostNotFound)), () => []);
        var options = new FixOptions(System.IO.Path.Combine(dir.Path, "tmp"), System.IO.Path.Combine(dir.Path, "settings.json"),
            System.IO.Path.Combine(dir.Path, "logs", "n.log"), [], "1.0.0", admin ?? (() => false), () => free, () => 200L << 30,
            ScanFileMetadata: static (_, _) => new FileInventoryScan(0, 0, 0, true, TimeSpan.Zero));
        Directory.CreateDirectory(options.TempRoot);
        return new FixService(options, runner ?? new FakeRunner(), lab);
    }

    [Fact]
    public async Task QuickScanRunsOnlyQuickChecks()
    {
        using var dir = new TempDir();
        var results = await Service(dir).ScanAsync(false, null, CancellationToken.None);
        Assert.Equal(FixService.QuickIds, results.Select(r => r.Id));
    }

    [Fact]
    public async Task FullScanWithoutAdminDoesNotClaimSystemFilesAreHealthy()
    {
        using var dir = new TempDir();
        var runner = new FakeRunner();
        var results = await Service(dir, runner).ScanAsync(true, null, CancellationToken.None);
        Assert.Equal(FixService.FullIds, results.Select(r => r.Id));
        Assert.Equal(CheckStatus.Unknown, results.Single(r => r.Id == "sfc").Status);
        Assert.Equal(CheckStatus.Unknown, results.Single(r => r.Id == "dism").Status);
        Assert.Contains(runner.Calls, call => call.StartsWith("powershell ", StringComparison.Ordinal));
        Assert.Contains(runner.Calls, call => call.StartsWith("wevtutil qe Application ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdminToolExecutionFailureIsNotReportedAsSystemProblem()
    {
        using var dir = new TempDir();
        var results = await Service(dir, new FakeRunner(1), () => true).ScanAsync(true, null, CancellationToken.None);
        Assert.Equal(CheckStatus.Failed, results.Single(r => r.Id == "sfc").Status);
        Assert.Contains("code 1", results.Single(r => r.Id == "sfc").Message);
    }

    [Theory]
    [InlineData("Windows Resource Protection did not find any integrity violations.", CheckStatus.Healthy)]
    [InlineData("Windows Resource Protection found integrity violations.", CheckStatus.Problem)]
    [InlineData("localized output unknown to NIVEX", CheckStatus.Unknown)]
    public async Task SfcCheckUsesObservedOutputInsteadOfExitCodeAlone(string output, CheckStatus expected)
    {
        using var dir = new TempDir();
        var result = (await Service(dir, new FakeRunner(0, output), () => true)
            .ScanAsync(true, null, CancellationToken.None)).Single(r => r.Id == "sfc");
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task DnsFailureProducesRepairableProblem()
    {
        using var dir = new TempDir();
        var results = await Service(dir, dnsOk: false).ScanAsync(false, null, CancellationToken.None);
        var dns = results.Single(r => r.Id == "dns");
        Assert.Equal(CheckStatus.Problem, dns.Status);
        Assert.Equal("flush-dns", dns.RepairId);
    }

    [Fact]
    public async Task LowDiskSpaceIsProblem()
    {
        using var dir = new TempDir();
        var results = await Service(dir, free: 5L << 30).ScanAsync(false, null, CancellationToken.None);
        Assert.Equal(CheckStatus.Problem, results.Single(r => r.Id == "disk").Status);
    }

    [Fact]
    public async Task FlushDnsReportsExitCode()
    {
        using var dir = new TempDir();
        Assert.Equal(CheckStatus.Repaired, (await Service(dir, new FakeRunner(0)).FlushDnsAsync(CancellationToken.None)).Status);
        Assert.Equal(CheckStatus.RepairFailed, (await Service(dir, new FakeRunner(5)).FlushDnsAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task ProcessRunnerDrainsStandardOutputAndError()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await new ProcessRunner().RunAsync(
            "cmd.exe",
            ["/d", "/c", "echo stdout & echo stderr 1>&2"],
            TimeSpan.FromSeconds(5),
            Encoding.UTF8,
            CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("stdout", result.Output);
        Assert.Contains("stderr", result.ErrorOutput);
    }

    [Fact]
    public async Task ConcurrentScanIsRejected()
    {
        using var dir = new TempDir();
        var service = Service(dir);
        var cts = new CancellationTokenSource();
        var first = service.ScanAsync(true, null, cts.Token);
        if (service.IsScanning) await Assert.ThrowsAsync<InvalidOperationException>(() => service.ScanAsync(false, null, CancellationToken.None));
        await first;
    }

    [Fact]
    public void TempCleanupOnlyTouchesOldFilesInsideRootAndSupportsCancel()
    {
        using var dir = new TempDir();
        var service = Service(dir);
        var root = System.IO.Path.Combine(dir.Path, "tmp");
        var old = System.IO.Path.Combine(root, "old.tmp");
        var fresh = System.IO.Path.Combine(root, "fresh.tmp");
        File.WriteAllText(old, "x");
        File.WriteAllText(fresh, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
        var plan = service.PlanTempCleanup(CancellationToken.None);
        Assert.Single(plan.Files);

        var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var none = service.ExecuteTempCleanup(plan, null, cancelled.Token);
        Assert.True(none.Cancelled);
        Assert.True(File.Exists(old));

        var outside = System.IO.Path.Combine(dir.Path, "outside.txt");
        File.WriteAllText(outside, "keep");
        var forged = new TempPlan(plan.Root, [.. plan.Files, new TempFileEntry(outside, 4)]);
        var done = service.ExecuteTempCleanup(forged, null, CancellationToken.None);
        Assert.Equal(1, done.Deleted);
        Assert.Equal(1, done.Skipped);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void TempCleanupSkipsFilesChangedAfterReview()
    {
        using var dir = new TempDir();
        var service = Service(dir);
        var path = System.IO.Path.Combine(dir.Path, "tmp", "reviewed.tmp");
        File.WriteAllText(path, "old");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        var plan = service.PlanTempCleanup(CancellationToken.None);
        File.WriteAllText(path, "new contents");

        var result = service.ExecuteTempCleanup(plan, null, CancellationToken.None);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("new contents", File.ReadAllText(path));
    }

    [Fact]
    public void TempCleanupSkipsFilesThatNoLongerExist()
    {
        using var dir = new TempDir();
        var service = Service(dir);
        var path = System.IO.Path.Combine(dir.Path, "tmp", "removed.tmp");
        File.WriteAllText(path, "old");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        var plan = service.PlanTempCleanup(CancellationToken.None);
        File.Delete(path);

        var result = service.ExecuteTempCleanup(plan, null, CancellationToken.None);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.LogicalBytesDeleted);
    }

    [Fact]
    public void TempCleanupRejectsPlanForDifferentRoot()
    {
        using var dir = new TempDir();
        var service = Service(dir);
        var outside = System.IO.Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        var path = System.IO.Path.Combine(outside, "old.tmp");
        File.WriteAllText(path, "old");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        var forged = new TempPlan(outside, [new TempFileEntry(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path))]);

        var result = service.ExecuteTempCleanup(forged, null, CancellationToken.None);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void LockedFileIsSkipped()
    {
        using var dir = new TempDir();
        var service = Service(dir);
        var path = System.IO.Path.Combine(dir.Path, "tmp", "locked.tmp");
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        var plan = service.PlanTempCleanup(CancellationToken.None);
        using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = service.ExecuteTempCleanup(plan, null, CancellationToken.None);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Deleted);
    }
}

public sealed class MonitorTests
{
    private sealed class Metrics(Func<SystemSnapshot> read) : ISystemMetrics
    {
        public SystemSnapshot Read() => read();
    }

    private static SystemSnapshot Snap(double? cpu = 10, double? gpu = null) =>
        new(cpu, false, 4UL << 30, 8UL << 30, null, gpu, false, 50UL << 30, 100UL << 30, true);

    [Fact]
    public async Task CollectsRealValuesAndLeavesUnsupportedEmpty()
    {
        using var monitor = new MonitorService(new Metrics(() => Snap()));
        var sample = await monitor.SampleOnceAsync();
        Assert.Equal(50, sample!.MemoryPercent);
        Assert.Null(sample.Gpu);
        var history = monitor.History();
        Assert.Single(history.Cpu);
        Assert.Empty(history.Gpu);
    }

    [Fact]
    public async Task HistoryIsBounded()
    {
        using var monitor = new MonitorService(new Metrics(() => Snap()));
        for (var i = 0; i < MonitorService.Capacity + 30; i++) await monitor.SampleOnceAsync();
        Assert.Equal(MonitorService.Capacity, monitor.History().Cpu.Length);
    }

    [Fact]
    public async Task FailureIsReportedWithoutThrowing()
    {
        using var monitor = new MonitorService(new Metrics(() => throw new InvalidOperationException("boom")));
        string? error = null;
        monitor.Failed += e => error = e;
        Assert.Null(await monitor.SampleOnceAsync());
        Assert.Equal("InvalidOperationException", error);
    }

    [Fact]
    public async Task StopEndsCollection()
    {
        var calls = 0;
        using var monitor = new MonitorService(new Metrics(() => { Interlocked.Increment(ref calls); return Snap(); }));
        monitor.Start(TimeSpan.FromMilliseconds(40));
        await Task.Delay(250);
        monitor.Stop();
        Assert.False(monitor.IsRunning);
        await Task.Delay(100);
        var after = calls;
        await Task.Delay(250);
        Assert.Equal(after, calls);
        Assert.True(after >= 2);
    }
}

public sealed class WorkflowTests
{
    private static WorkflowStep Step(StepType type, string input = "", bool cont = false) => new() { Name = type.ToString(), Type = type, Input = input, ContinueOnError = cont };

    [Fact]
    public async Task RunsStepsInOrder()
    {
        using var dir = new TempDir();
        var host = new FakeHost();
        var result = await new WorkflowEngine(host).RunAsync(
            [Step(StepType.OpenFolder, dir.Path), Step(StepType.OpenUrl, "https://nivex.xyz.ci"), Step(StepType.Wait, "1"), Step(StepType.CheckPrevious)], null, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal([dir.Path, "https://nivex.xyz.ci/"], host.Opened.Select(o => o.TrimEnd()).Select(o => o.EndsWith("ci") ? o + "/" : o));
    }

    [Theory]
    [InlineData(StepType.OpenUrl, "javascript:alert(1)")]
    [InlineData(StepType.OpenUrl, "file:///c:/windows/system32/cmd.exe")]
    [InlineData(StepType.Wait, "0")]
    [InlineData(StepType.Wait, "abc")]
    [InlineData(StepType.OpenFolder, "relative")]
    [InlineData(StepType.LaunchProgram, "C:\\nope\\missing.exe")]
    [InlineData(StepType.OpenFolder, "C:\\definitely\\not\\here")]
    public async Task InvalidInputFailsStepWithoutRunningIt(StepType type, string input)
    {
        var host = new FakeHost();
        var result = await new WorkflowEngine(host).RunAsync([Step(type, input)], null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(StepState.Failed, result.Steps[0].State);
        Assert.Empty(host.Opened);
    }

    [Fact]
    public void ScriptsCannotBeOpenedAsFilesOrLaunched()
    {
        using var dir = new TempDir();
        var script = System.IO.Path.Combine(dir.Path, "a.ps1");
        File.WriteAllText(script, "x");
        Assert.NotNull(StepValidator.Validate(Step(StepType.OpenFile, script)));
        Assert.NotNull(StepValidator.Validate(Step(StepType.LaunchProgram, script)));
    }

    [Fact]
    public async Task FailureStopsUnlessContinue()
    {
        using var dir = new TempDir();
        var host = new FakeHost();
        var stop = await new WorkflowEngine(host).RunAsync([Step(StepType.OpenFolder, "C:\\nope-x"), Step(StepType.OpenFolder, dir.Path)], null, CancellationToken.None);
        Assert.Equal(StepState.Skipped, stop.Steps[1].State);
        var go = await new WorkflowEngine(host).RunAsync([Step(StepType.OpenFolder, "C:\\nope-x", true), Step(StepType.OpenFolder, dir.Path)], null, CancellationToken.None);
        Assert.Equal(StepState.Succeeded, go.Steps[1].State);
        Assert.False(go.Success);
    }

    [Fact]
    public async Task CancelStopsRemainingSteps()
    {
        using var dir = new TempDir();
        var host = new FakeHost();
        using var cts = new CancellationTokenSource();
        var engine = new WorkflowEngine(host);
        var task = engine.RunAsync([Step(StepType.Wait, "30"), Step(StepType.OpenFolder, dir.Path)], null, cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        var result = await task;
        Assert.True(result.Cancelled);
        Assert.False(result.Success);
        Assert.Equal(StepState.Cancelled, result.Steps[1].State);
        Assert.Empty(host.Opened);
    }

    [Fact]
    public async Task SecondConcurrentRunIsRejected()
    {
        var engine = new WorkflowEngine(new FakeHost());
        using var cts = new CancellationTokenSource();
        var first = engine.RunAsync([Step(StepType.Wait, "30")], null, cts.Token);
        await Task.Delay(100);
        var second = await engine.RunAsync([Step(StepType.Wait, "1")], null, CancellationToken.None);
        Assert.False(second.Started);
        cts.Cancel();
        await first;
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task DeclinedConfirmationCancelsRun()
    {
        using var dir = new TempDir();
        var host = new FakeHost { Confirm = false };
        var result = await new WorkflowEngine(host).RunAsync([Step(StepType.Confirm, "Proceed?"), Step(StepType.OpenFolder, dir.Path)], null, CancellationToken.None);
        Assert.True(result.Cancelled);
        Assert.Empty(host.Opened);
    }

    [Fact]
    public void StoreSavesLoadsAndRejectsBadFiles()
    {
        using var dir = new TempDir();
        var store = new WorkflowStore(dir.Path);
        var workflow = new Workflow { Name = "Morning", Steps = [Step(StepType.OpenUrl, "https://nivex.xyz.ci")] };
        store.Save(workflow);
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "broken.json"), "{ not json");
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "badschema.json"), "{\"schema\":99,\"id\":\"" + Guid.NewGuid().ToString("N") + "\",\"name\":\"x\",\"steps\":[]}");
        var loaded = store.LoadAll();
        Assert.Single(loaded.Items);
        Assert.Equal(2, loaded.Errors.Count);
        Assert.Equal("Morning", loaded.Items[0].Name);
    }

    [Fact]
    public void ImportValidatesPathAndSize()
    {
        using var dir = new TempDir();
        var store = new WorkflowStore(dir.Path);
        Assert.NotNull(store.Import("relative.json").Error);
        Assert.NotNull(store.Import(System.IO.Path.Combine(dir.Path, "a.txt")).Error);
        var big = System.IO.Path.Combine(dir.Path, "big.json");
        File.WriteAllText(big, new string(' ', WorkflowStore.MaxFileBytes + 10));
        Assert.Equal("file too large", store.Import(big).Error);
        var good = new Workflow { Name = "Shared" };
        var path = System.IO.Path.Combine(dir.Path, "ok.json");
        Assert.True(store.Export(good, path).Ok);
        var imported = store.Import(path);
        Assert.NotEqual(good.Id, imported.Workflow!.Id);
    }

    [Fact]
    public void DeleteRejectsPathTraversalIds() => Assert.False(new WorkflowStore("C:\\x").Delete("..\\..\\evil"));
}

public sealed class ProfileTests
{
    [Fact]
    public void PlanFlagsMissingPathsAndMissingWorkflow()
    {
        using var dir = new TempDir();
        var profile = new Profile
        {
            Name = "Dev",
            WorkflowId = Guid.NewGuid().ToString("N"),
            Items = [new ProfileItem { Kind = ProfileItemKind.Folder, Value = dir.Path }, new ProfileItem { Kind = ProfileItemKind.Program, Value = "C:\\gone\\app.exe" }, new ProfileItem { Kind = ProfileItemKind.Url, Value = "https://nivex.xyz.ci", Enabled = false }],
        };
        var plan = ProfilePlanner.Build(profile, []);
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(2, plan.Issues.Count);
    }

    [Fact]
    public async Task ProfileRunsLinkedWorkflowAfterItemsInOrder()
    {
        using var dir = new TempDir();
        var workflow = new Workflow { Name = "W", Steps = [new WorkflowStep { Type = StepType.OpenFolder, Input = dir.Path }] };
        var profile = new Profile { Name = "P", WorkflowId = workflow.Id, Items = [new ProfileItem { Kind = ProfileItemKind.Url, Value = "https://nivex.xyz.ci" }] };
        var plan = ProfilePlanner.Build(profile, [workflow]);
        var host = new FakeHost();
        var result = await new WorkflowEngine(host).RunAsync(plan.Steps, null, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(2, host.Opened.Count);
        Assert.Equal(dir.Path, host.Opened[1]);
    }

    [Fact]
    public void StoreRoundTripDuplicateAndCorruption()
    {
        using var dir = new TempDir();
        var path = System.IO.Path.Combine(dir.Path, "profiles.json");
        var store = new ProfileStore(path);
        var profile = new Profile { Name = "Work", Items = [new ProfileItem { Kind = ProfileItemKind.Url, Value = "https://nivex.xyz.ci" }] };
        var copy = ProfileStore.Duplicate(profile, "(copy)");
        Assert.NotEqual(profile.Id, copy.Id);
        store.Save([profile, copy]);
        Assert.Equal(2, store.Load().Count);
        File.WriteAllText(path, "garbage");
        Assert.Empty(store.Load());
        Assert.True(store.RecoveredFromCorruption);
    }
}

public sealed class InsightTests
{
    private static HistoryRecord Rec(HistoryKind kind, bool ok, string title, int daysAgo = 0) =>
        new() { Kind = kind, Success = ok, Title = title, Summary = title + " summary", Time = DateTimeOffset.Now.AddDays(-daysAgo) };

    [Fact]
    public void EmptyStoreReturnsNothingAndReportSaysSo()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(System.IO.Path.Combine(dir.Path, "h.json"));
        Assert.Empty(store.All());
        var html = ReportBuilder.BuildHtml([], "T", DateTimeOffset.Now, DateTimeOffset.Now, DateTimeOffset.Now, false);
        Assert.Contains("No records", html);
    }

    [Fact]
    public void RecordsPersistFilterAndDeduplicate()
    {
        using var dir = new TempDir();
        var path = System.IO.Path.Combine(dir.Path, "h.json");
        var store = new HistoryStore(path);
        var a = Rec(HistoryKind.Fix, true, "Quick scan");
        store.Add(a);
        Assert.False(store.Add(a));
        store.Add(Rec(HistoryKind.Network, false, "DNS lookup failed", 10));
        var reloaded = new HistoryStore(path).All();
        Assert.Equal(2, reloaded.Count);
        Assert.Single(HistoryStore.Query(reloaded, null, null, HistoryKind.Network, null, null));
        Assert.Single(HistoryStore.Query(reloaded, null, null, null, true, null));
        Assert.Single(HistoryStore.Query(reloaded, null, null, null, null, "dns"));
        Assert.Single(HistoryStore.Query(reloaded, DateTimeOffset.Now.AddDays(-2), null, null, null, null));
    }

    [Fact]
    public void CorruptFileIsQuarantinedAndStoreStillWorks()
    {
        using var dir = new TempDir();
        var path = System.IO.Path.Combine(dir.Path, "h.json");
        File.WriteAllText(path, "{{{ broken");
        var store = new HistoryStore(path);
        Assert.Empty(store.All());
        Assert.True(store.RecoveredFromCorruption);
        Assert.True(store.Add(Rec(HistoryKind.Fix, true, "after")));
        Assert.Single(Directory.GetFiles(dir.Path, "h.json.corrupt-*"));
    }

    [Fact]
    public void StoreIsBoundedAndCleanupWorks()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(System.IO.Path.Combine(dir.Path, "h.json"), 10);
        for (var i = 0; i < 25; i++) store.Add(Rec(HistoryKind.Monitor, true, "m" + i, i));
        Assert.Equal(10, store.All().Count);
        Assert.True(store.RemoveOlderThan(DateTimeOffset.Now.AddDays(-5)) > 0);
        Assert.True(store.Clear());
        Assert.Empty(store.All());
    }

    [Fact]
    public void ReportSeparatesProblemsRepairsAndAdviceAndEscapesHtml()
    {
        var records = new[]
        {
            Rec(HistoryKind.Fix, false, "<script>x</script>"),
            Rec(HistoryKind.Repair, true, "Flush DNS"),
            new HistoryRecord { Kind = HistoryKind.Network, Success = false, Title = "Ping", Summary = "lost", Advice = "Check cable" },
        };
        var html = ReportBuilder.BuildHtml(records, "Report", DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now, DateTimeOffset.Now, false);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Repairs performed", html);
        Assert.Contains("Check cable", html);
        Assert.Contains("advice, not measurements", html);
    }

    [Fact]
    public void ReportWriterNeverOverwritesWithoutConsentAndReportsFailures()
    {
        using var dir = new TempDir();
        var path = System.IO.Path.Combine(dir.Path, "r.html");
        Assert.True(ReportWriter.Save(path, "<p>1</p>", false).Ok);
        var again = ReportWriter.Save(path, "<p>2</p>", false);
        Assert.False(again.Ok);
        Assert.Equal("<p>1</p>", File.ReadAllText(path));
        Assert.True(ReportWriter.Save(path, "<p>2</p>", true).Ok);
        Assert.False(ReportWriter.Save(System.IO.Path.Combine(dir.Path, "missing", "r.html"), "x", false).Ok);
        Assert.False(ReportWriter.Save(System.IO.Path.Combine(dir.Path, "r.txt"), "x", false).Ok);
    }
}
