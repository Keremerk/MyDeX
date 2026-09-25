namespace MyDeX.Tests;

/// <summary>The phone check after DeX stops (looks for anything DeX left behind).</summary>
public class DexLeftoverTests
{
    [Fact]
    public void DexLeftovers_CleanAfterANormalStop()
    {
        const string displays = """DisplayInfo{"Built-in Screen", displayId 0, FLAG_SECURE} DisplayInfo{"Built-in Screen", displayId 0}""";
        var report = AdbClient.CheckForDexLeftovers(displays, "Display #0 (activities from top to bottom):", "null");
        Assert.True(report.IsClean);
        Assert.Equal("", report.ToString());
    }

    [Fact]
    public void DexLeftovers_FindsScreensLauncherAndOverlay()
    {
        const string displays = """
            DisplayInfo{"Built-in Screen", displayId 0}
            DisplayInfo{"scrcpy", displayId 9, FLAG_PRESENTATION} DisplayInfo{"scrcpy", displayId 9}
            DisplayInfo{"Overlay #1", displayId 11}
            """;
        const string activities = "Display #9 (activities from top to bottom):\n  * Task{643238 #170 type=home I=com.sec.android.app.launcher/com.honeyspace.dexservice.SecondaryLauncher}";

        var report = AdbClient.CheckForDexLeftovers(displays, activities, "1920x1080/160\n");

        Assert.False(report.IsClean);
        Assert.Equal(2, report.ExtraScreens);   // display 9 is listed twice but counts once
        Assert.True(report.DexLauncherRunning);
        Assert.True(report.Overlay);
        Assert.Contains("2 extra screen(s)", report.ToString());
    }
}
