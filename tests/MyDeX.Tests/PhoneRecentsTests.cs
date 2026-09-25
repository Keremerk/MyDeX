namespace MyDeX.Tests;

/// <summary>"Recently used on your phone" and the phone check after DeX stops.</summary>
public class PhoneRecentsTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ParseRecents_ListsAppsNewestFirst_WithoutHomeDexOrRecentsTasks()
    {
        var packages = AdbClient.ParseRecents(Fixture("dumpsys-recents.txt").Replace("\n", "\r\n"));

        // Home screen, DeX launcher and the Recents screen are skipped; a second task of the same app counts once.
        Assert.Equal(["com.example.photos", "com.example.notes", "com.example.chat", "com.example.game"], packages);
    }

    [Fact]
    public void ParseRecents_RespectsTheLimit_AndHandlesEmptyOutput()
    {
        Assert.Equal(2, AdbClient.ParseRecents(Fixture("dumpsys-recents.txt"), max: 2).Count);
        Assert.Empty(AdbClient.ParseRecents(""));
        Assert.Empty(AdbClient.ParseRecents("Error: service not found"));
    }

    [Fact]
    public void ParseRecents_OneUi8Format_UsesActivityComponentOrTheHeader()
    {
        // One UI 8 has no realActivity line: mActivityComponent=…, or only the header's A=uid:package.
        const string oneUi8 = """
              * Recent #0: Task{1929b4a #186 type=recents A=10157:com.sec.android.app.launcher.task.recents}
                mActivityComponent=com.sec.android.app.launcher/com.android.quickstep.RecentsActivity
              * Recent #1: Task{892c088 #183 type=standard A=10474:com.example.photos}
                affinity=10474:com.example.photos
                mActivityComponent=com.example.photos/com.example.photos.MainActivity
              * Recent #2: Task{b127513 #157 type=standard A=10500:com.example.reader}
                lastActiveTime=6772579 (inactive for 36s)
              * Recent #3: Task{8e63c8 #160 type=standard I=com.example.contacts/com.example.contacts.PeopleActivity}
            """;
        Assert.Equal(["com.example.photos", "com.example.reader", "com.example.contacts"], AdbClient.ParseRecents(oneUi8));
    }

    [Fact]
    public void ParseUsageStats_NewestFirst_KeepsTheLatestUse_SkipsNeverUsed()
    {
        const string usage = """
              In-memory daily stats
                  package=com.example.maps totalTimeUsed="00:10" lastTimeUsed="2026-09-25 18:00:00" totalTimeVisible="00:10"
                  package=com.example.system totalTimeUsed="00:00" lastTimeUsed="1970-01-01 01:00:00" totalTimeVisible="00:00"
              In-memory monthly stats
                  package=com.example.maps totalTimeUsed="02:00" lastTimeUsed="2026-09-01 09:00:00"
                  package=com.example.game totalTimeUsed="05:00" lastTimeUsed="2026-09-20 21:30:00"
                  package=com.example.bank totalTimeUsed="00:05" lastTimeUsed="2026-09-25 19:15:42"
            """;

        Assert.Equal(["com.example.bank", "com.example.maps", "com.example.game"], AdbClient.ParseUsageStats(usage));
        Assert.Empty(AdbClient.ParseUsageStats(""));
    }

    [Fact]
    public void RecentlyUsed_OpenRecentsFirst_ThenUsageHistory_NoDuplicates_UpTo50()
    {
        var usage = Enumerable.Range(0, 80).Select(i => $"com.example.app{i}").Prepend("com.example.photos").ToList();
        var merged = AdbClient.MergeRecentlyUsed(["com.example.photos", "com.example.notes"], usage, max: 50);

        Assert.Equal(50, merged.Count);
        Assert.Equal(["com.example.photos", "com.example.notes", "com.example.app0"], merged.Take(3));
        Assert.Equal(merged.Count, merged.Distinct().Count());
    }

    [Fact]
    public void ParseRecents_AcceptsTheOlderFormatWithoutBraces()
    {
        const string older = """
              * Recent #0: Task{1 #2 type=standard A=10001:com.example.old}
                realActivity=com.example.old/.MainActivity
            """;
        Assert.Equal(["com.example.old"], AdbClient.ParseRecents(older));
    }

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
