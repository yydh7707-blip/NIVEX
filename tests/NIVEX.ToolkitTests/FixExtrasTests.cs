using NIVEX.Toolkit;
using Xunit;

namespace NIVEX.ToolkitTests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class FixExtrasTests
{
    [Fact]
    public void DeviceParseHandlesSingleObjectArrayAndGarbage()
    {
        Assert.Single(DeviceInspector.Parse("{\"Name\":\"X\",\"ConfigManagerErrorCode\":43}"));
        Assert.Equal(2, DeviceInspector.Parse("[{\"Name\":\"A\",\"ConfigManagerErrorCode\":10},{\"Name\":\"B\",\"ConfigManagerErrorCode\":28}]").Count);
        Assert.Empty(DeviceInspector.Parse(""));
        Assert.Empty(DeviceInspector.Parse("not json"));
    }

    [Fact]
    public async Task DeviceReadDoesNotTreatMalformedJsonAsAHealthyEmptyResult()
    {
        var result = await DeviceInspector.ReadAsync(new FakeRunner(output: "not json"), CancellationToken.None);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("game.exe", true)]
    [InlineData("a b-1.exe", true)]
    [InlineData("..\\x.exe", false)]
    [InlineData("a\" /c calc.exe", false)]
    [InlineData("", false)]
    public void SafeExeName(string name, bool expected) => Assert.Equal(expected, AppTroubleshooter.IsSafeExeName(name));

    [Fact]
    public void SanitizeDropsUnknownIdsClampsIntervalAndDedupesNames()
    {
        var id = FixService.AllIds[0];
        var list = MaintProfileStore.Sanitize([
            new("A", [id, "bogus"], 9999),
            new("a", [id], 1),
            new("B", [id], 0)]);
        Assert.Equal(2, list.Count);
        Assert.Equal([id], list[0].CheckIds);
        Assert.Equal(168, list[0].IntervalHours);
        Assert.Equal(1, list[1].IntervalHours);
    }

    [Fact]
    public void DuplicateGetsUniqueName()
    {
        var p = new MaintProfile("A", [FixService.AllIds[0]], 24);
        var copy = MaintProfileStore.Duplicate(p, [p]);
        Assert.NotEqual(p.Name, copy.Name);
    }

    [Fact]
    public void SecurityEntriesAreProtected()
    {
        Assert.True(StartupManager.IsProtected("SecurityHealth", "x"));
        Assert.False(StartupManager.IsProtected("MyApp", "C:\\Apps\\my.exe"));
    }
}
