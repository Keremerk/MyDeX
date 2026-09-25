namespace MyDeX.Tests;

/// <summary>Regression tests for the issues found in code review.</summary>
public class ReviewFixTests
{
    [Fact]
    public async Task StopBeforeStartFinishes_CancelsTheStart()
    {
        // No real adb/scrcpy: every tool call fails fast, which is enough to reach the stop checks.
        var settings = AppSettings.FromJson(null);
        var session = new DexSession(new AdbClient("does-not-exist-adb.exe"), "does-not-exist-scrcpy.exe", settings);

        await session.StopAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => session.StartAsync(new PhoneDevice("X", "device", "Test")));
        Assert.False(session.IsRunning);
    }

    [Theory]
    [InlineData("""{ "Profiles": null }""")]
    [InlineData("""{ "Profiles": [ null, { "Name": "" } ], "RecentApps": null, "ActiveProfile": null }""")]
    [InlineData("""{ "Controller": 99, "AppShape": 7, "Method": 5 }""")]
    [InlineData("""{ "Profiles": [ { "Name": "Odd", "VideoCodec": "vp9", "Width": 5, "Height": 99999 } ], "ActiveProfile": "Odd" }""")]
    public void DamagedSettingsFile_StillLoads(string json)
    {
        var settings = AppSettings.FromJson(json);

        Assert.NotEmpty(settings.Profiles);
        Assert.NotNull(settings.Profile);
        Assert.NotNull(settings.RecentApps);
        Assert.True(Enum.IsDefined(settings.Controller));
        Assert.True(Enum.IsDefined(settings.AppShape));
        Assert.True(Enum.IsDefined(settings.Method));
        Assert.Contains(settings.Profile.VideoCodec, new[] { "h264", "h265", "av1" });
        Assert.InRange(settings.Profile.Width, 320, 7680);
        Assert.InRange(settings.Profile.Height, 240, 4320);
    }
}
