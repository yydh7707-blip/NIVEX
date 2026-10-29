using NIVEX.Toolkit;
using Xunit;

namespace NIVEX.ToolkitTests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class FixAnalysisTests
{
    private static CheckResult R(string id, CheckStatus status, string message = "m") => new(id, status, message);

    [Fact]
    public void DnsFailureWithWorkingInternetIsOnlyPossible()
    {
        var findings = RootCauseAnalyzer.Analyze([R("dns", CheckStatus.Problem), R("internet", CheckStatus.Healthy)]);
        var f = Assert.Single(findings);
        Assert.Equal(Certainty.Possible, f.Certainty);
        Assert.Equal("flush-dns", f.RepairId);
    }

    [Fact]
    public void DnsAndInternetFailingTogetherStayUndetermined()
    {
        var findings = RootCauseAnalyzer.Analyze([R("dns", CheckStatus.Problem), R("internet", CheckStatus.Problem), R("adapter", CheckStatus.Healthy)]);
        var f = Assert.Single(findings);
        Assert.Equal("dns", f.CheckId);
        Assert.Equal(Certainty.Undetermined, f.Certainty);
    }

    [Fact]
    public void FailedCheckIsReportedAsUndeterminedWithSafeNextStep()
    {
        var finding = Assert.Single(RootCauseAnalyzer.Analyze([R("dns", CheckStatus.Failed, "process failed")]));
        Assert.Equal(Certainty.Undetermined, finding.Certainty);
        Assert.Equal("process failed", finding.Evidence);
        Assert.Contains("did not complete", finding.SuggestedNextStep);
    }

    [Fact]
    public void HealthyCheckThatBecomesProblemIsNotReportedAsUnverifiable()
    {
        var result = BeforeAfter.Compare(R("dns", CheckStatus.Healthy), R("dns", CheckStatus.Problem));
        Assert.Equal(Verdict.Unresolved, result.Verdict);
    }

    [Theory]
    [InlineData("disk", 5L, 6L, Verdict.Improved)]
    [InlineData("disk", 6L, 5L, Verdict.Unresolved)]
    [InlineData("temp", 6L, 5L, Verdict.Improved)]
    [InlineData("temp", 5L, 6L, Verdict.Unresolved)]
    public void PartialImprovementRequiresMeasuredProgress(string id, long before, long after, Verdict expected)
    {
        var comparison = BeforeAfter.Compare(
            new CheckResult(id, CheckStatus.Problem, "before", Metric: before),
            new CheckResult(id, CheckStatus.Problem, "after", Metric: after));
        Assert.Equal(expected, comparison.Verdict);
    }

    [Fact]
    public void LowDiskIsConfirmedAndLinksTempFiles()
    {
        var findings = RootCauseAnalyzer.Analyze([R("disk", CheckStatus.Problem, "5 GB free"), R("temp", CheckStatus.Problem)]);
        var disk = findings.First(f => f.CheckId == "disk");
        Assert.Equal(Certainty.Confirmed, disk.Certainty);
        Assert.Contains("temp", disk.RelatedIds);
        Assert.Equal("5 GB free", disk.Evidence);
    }

    [Theory]
    [InlineData(CheckStatus.Healthy, Verdict.Resolved)]
    [InlineData(CheckStatus.Problem, Verdict.Unresolved)]
    [InlineData(CheckStatus.Unknown, Verdict.Unverifiable)]
    [InlineData(CheckStatus.Failed, Verdict.Unverifiable)]
    public void ComparisonUsesTheRecheckNotTheRepair(CheckStatus after, Verdict expected)
    {
        Assert.Equal(expected, BeforeAfter.Compare(R("dns", CheckStatus.Problem), R("dns", after)).Verdict);
    }

    [Fact]
    public void RepairExecutionAndProblemResolutionAreReportedSeparately()
    {
        var fixedDespiteToolError = BeforeAfter.Compare(R("dns", CheckStatus.Problem), R("dns", CheckStatus.Healthy), true);
        Assert.Equal(Verdict.Resolved, fixedDespiteToolError.Verdict);
        Assert.Equal(RepairExecution.Failed, fixedDespiteToolError.Execution);

        var stillBroken = BeforeAfter.Compare(R("dns", CheckStatus.Problem), R("dns", CheckStatus.Problem), true);
        Assert.Equal(Verdict.Unresolved, stillBroken.Verdict);
        Assert.Equal(RepairExecution.Failed, stillBroken.Execution);

        var cancelled = BeforeAfter.Compare(R("dns", CheckStatus.Problem), R("dns", CheckStatus.Problem), cancelled: true);
        Assert.Equal(Verdict.Unresolved, cancelled.Verdict);
        Assert.Equal(RepairExecution.Cancelled, cancelled.Execution);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --silent", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\x.exe /min", "C:\\Tools\\x.exe")]
    [InlineData("", null)]
    public void StartupPathIsExtracted(string command, string? expected)
    {
        Assert.Equal(expected, StartupInspector.ExtractPath(command));
    }

    [Fact]
    public void EventXmlIsParsedAndMalformedXmlIsIgnored()
    {
        const string xml = "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><Provider Name=\"Application Error\"/><EventID>1000</EventID><TimeCreated SystemTime=\"2025-01-02T03:04:05.000Z\"/></System><EventData><Data Name=\"ApplicationName\">app.exe</Data><Data Name=\"ExceptionCode\">0xc0000005</Data><Data Name=\"FaultingModuleName\">module.dll</Data></EventData></Event>";
        var e = Assert.Single(CrashLab.Parse(xml, "Application"));
        Assert.Equal("Application Error", e.Provider);
        Assert.Equal(1000, e.EventId);
        Assert.Equal("app.exe", e.Application);
        Assert.Equal("0xc0000005", e.ErrorCode);
        Assert.Equal("module.dll", e.Module);
        Assert.Empty(CrashLab.Parse("<broken", "System"));
    }

    [Fact]
    public async Task EventLogReadFailureIsNotReportedAsAnEmptyLog()
    {
        var lab = new CrashLab(new FakeRunner(5, errorOutput: "access denied"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lab.ReadAsync(15, CancellationToken.None));
    }

    [Fact]
    public void LargeFileFinderReturnsLargestFirst()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "a.bin"), new byte[3000]);
        File.WriteAllBytes(Path.Combine(dir.Path, "b.bin"), new byte[9000]);
        File.WriteAllBytes(Path.Combine(dir.Path, "c.bin"), new byte[10]);
        var scan = LargeFileFinder.Find(dir.Path, 5, 1000, CancellationToken.None);
        Assert.Equal(["b.bin", "a.bin"], scan.Files.Select(f => Path.GetFileName(f.Path)).ToArray());
        Assert.Equal(3, scan.ScannedFiles);
    }

    [Fact]
    public void ReportEscapesHtmlAndOmitsPaths()
    {
        var labels = new FixReport.Labels("T", "G", "", "F", "Rec", "U", "C", "E", "N",
            r => r.Id, s => s.ToString(), f => f.CheckId, c => c.ToString(), f => "adv");
        var results = new[] { R("dns", CheckStatus.Problem, "<script>x</script>") };
        var html = FixReport.Html(labels, results, RootCauseAnalyzer.Analyze(results), [], "1.0", DateTimeOffset.Now);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
