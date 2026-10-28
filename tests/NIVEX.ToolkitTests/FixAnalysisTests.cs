using NIVEX.Toolkit;
using Xunit;

namespace NIVEX.ToolkitTests;

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
    public void HealthyAndUnknownChecksProduceNoFindings()
    {
        Assert.Empty(RootCauseAnalyzer.Analyze([R("os", CheckStatus.Healthy), R("sfc", CheckStatus.Unknown), R("dns", CheckStatus.Failed)]));
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
    public void FailedRepairIsReportedAsFailed()
    {
        Assert.Equal(Verdict.RepairFailed, BeforeAfter.Compare(R("dns", CheckStatus.Problem), R("dns", CheckStatus.Healthy), true).Verdict);
    }

    [Fact]
    public void FreedSpaceMakesUnresolvedDiskPartialOnly()
    {
        Assert.Equal(Verdict.Improved, BeforeAfter.WithFreedSpace(Verdict.Unresolved, 1));
        Assert.Equal(Verdict.Unresolved, BeforeAfter.WithFreedSpace(Verdict.Unresolved, 0));
        Assert.Equal(Verdict.Resolved, BeforeAfter.WithFreedSpace(Verdict.Resolved, 5));
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
        const string xml = "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><Provider Name=\"Application Error\"/><EventID>1000</EventID><TimeCreated SystemTime=\"2025-01-02T03:04:05.000Z\"/></System></Event>";
        var e = Assert.Single(CrashLab.Parse(xml, "Application"));
        Assert.Equal("Application Error", e.Provider);
        Assert.Equal(1000, e.EventId);
        Assert.Empty(CrashLab.Parse("<broken", "System"));
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
