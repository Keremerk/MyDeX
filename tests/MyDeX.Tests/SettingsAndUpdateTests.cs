namespace MyDeX.Tests;

public class SettingsTests
{
    [Fact]
    public void FreshInstall_HasGamingAndWorkProfiles()
    {
        var settings = AppSettings.FromJson(null);

        Assert.Equal(["Gaming", "Work"], settings.Profiles.Select(p => p.Name));
        Assert.Equal("Gaming", settings.Profile.Name);
        Assert.Equal(DisplayMethod.ScrcpyVirtualDisplay, settings.Method);
        Assert.Equal(ControllerTarget.DexDesktop, settings.Controller);
        Assert.True(settings.KeepAppsOnStop);
        Assert.True(settings.AutoReconnect);
        Assert.True(settings.HotkeyEnabled);
        Assert.False(settings.RecordSessions);
    }

    [Fact]
    public void SettingsFromVersion01_BecomeMySetupProfile()
    {
        // What MyDeX 0.1 saved (flat display settings, before profiles existed).
        const string legacy = """
            { "Width": 2560, "Height": 1440, "Dpi": 180, "Method": "ScrcpyVirtualDisplay",
              "MaxFps": 90, "BitrateMbps": 30, "VideoCodec": "h265", "Audio": false,
              "Fullscreen": true, "AlwaysOnTop": false, "StayAwake": true,
              "LastSerial": "RFTEST00001", "MinimizeToTray": true }
            """;

        var settings = AppSettings.FromJson(legacy);

        Assert.Equal(["Gaming", "Work", "My setup"], settings.Profiles.Select(p => p.Name));
        var mine = settings.Profile;
        Assert.Equal("My setup", mine.Name);
        Assert.Equal((2560, 1440, 180, 90, 30, "h265"), (mine.Width, mine.Height, mine.Dpi, mine.MaxFps, mine.BitrateMbps, mine.VideoCodec));
        Assert.False(mine.Audio);
        Assert.True(mine.Fullscreen);
        Assert.True(settings.StayAwake);
        Assert.Equal("RFTEST00001", settings.LastSerial);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var settings = AppSettings.FromJson(null);
        settings.Profiles.Add(DisplayProfile.Gaming().CopyAs("Racing"));
        settings.ActiveProfile = "Racing";
        settings.Profile.MaxFps = 144;
        settings.Controller = ControllerTarget.AppWindows;
        settings.AddRecentApp(new PhoneApp("Chrome", "com.android.chrome"));

        var loaded = AppSettings.FromJson(settings.ToJson());

        Assert.Equal(["Gaming", "Work", "Racing"], loaded.Profiles.Select(p => p.Name));
        Assert.Equal(144, loaded.Profile.MaxFps);
        Assert.Equal(ControllerTarget.AppWindows, loaded.Controller);
        Assert.Equal(new PhoneApp("Chrome", "com.android.chrome"), Assert.Single(loaded.RecentApps));
        Assert.Contains("\"AppWindows\"", settings.ToJson());   // enums are stored by name, not number
    }

    [Fact]
    public void BrokenFile_FallsBackToDefaults()
    {
        var settings = AppSettings.FromJson("{ this is not json");
        Assert.Equal("Gaming", settings.Profile.Name);
    }

    [Fact]
    public void UnknownActiveProfile_FallsBackToFirst()
    {
        var settings = AppSettings.FromJson(null);
        settings.ActiveProfile = "deleted";
        Assert.Equal("Gaming", settings.Profile.Name);
    }

    [Fact]
    public void RecentApps_MostRecentFirst_NoDuplicates_Capped()
    {
        var settings = AppSettings.FromJson(null);
        for (int i = 0; i < 10; i++)
            settings.AddRecentApp(new PhoneApp($"App {i}", $"com.example.app{i}"));
        settings.AddRecentApp(new PhoneApp("App 5", "com.example.app5"));

        Assert.Equal(8, settings.RecentApps.Count);
        Assert.Equal("com.example.app5", settings.RecentApps[0].Package);
        Assert.Single(settings.RecentApps, a => a.Package == "com.example.app5");
    }

    [Fact]
    public void CopyAs_IsIndependent()
    {
        var gaming = DisplayProfile.Gaming();
        var copy = gaming.CopyAs("Copy");
        copy.MaxFps = 30;
        Assert.Equal(120, gaming.MaxFps);
        Assert.Equal("Copy", copy.Name);
    }

    [Theory]
    [InlineData(1.0, 160)]
    [InlineData(1.25, 200)]
    [InlineData(1.5, 240)]
    [InlineData(1.75, 280)]
    [InlineData(0.1, 80)]    // clamped
    [InlineData(10.0, 640)]  // clamped
    public void DpiForWindowsScale(double scale, int expected) => Assert.Equal(expected, DisplayProfile.DpiForWindowsScale(scale));
}

public class UpdateTests
{
    [Theory]
    [InlineData("v4.1", "4.1.0")]
    [InlineData("4.1.2", "4.1.2")]
    [InlineData("v0.2.0-beta", "0.2.0")]
    [InlineData("release", null)]
    [InlineData(null, null)]
    public void ParseVersion(string? text, string? expected) => Assert.Equal(expected, UpdateChecker.ParseVersion(text)?.ToString());

    [Theory]
    [InlineData("4.2.0", "4.1.0", true)]
    [InlineData("4.1.0", "4.1.0", false)]
    [InlineData("4.0.9", "4.1.0", false)]
    public void IsNewer(string latest, string current, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Fact]
    public void IsNewer_UnknownVersions_IsFalse()
    {
        Assert.False(UpdateChecker.IsNewer(null, new Version(1, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(1, 0, 0), null));
    }

    [Fact]
    public void ParseRelease_PicksWin64ZipAndDigest()
    {
        const string json = """
            {
              "tag_name": "v4.1",
              "html_url": "https://github.com/Genymobile/scrcpy/releases/tag/v4.1",
              "assets": [
                { "name": "scrcpy-win32-v4.1.zip", "browser_download_url": "https://github.com/Genymobile/scrcpy/releases/download/v4.1/scrcpy-win32-v4.1.zip", "digest": "sha256:aaaa" },
                { "name": "scrcpy-win64-v4.1.zip", "browser_download_url": "https://github.com/Genymobile/scrcpy/releases/download/v4.1/scrcpy-win64-v4.1.zip",
                  "digest": "sha256:5B12172B3264B2889F4583EE64752CE832E29BC8B1089DCA81093459697165DB" },
                { "name": "SHA256SUMS.txt", "browser_download_url": "https://example/sums" }
              ]
            }
            """;

        var release = UpdateChecker.ParseRelease(json, "Genymobile/scrcpy", "scrcpy-win64-");

        Assert.NotNull(release);
        Assert.Equal(new Version(4, 1, 0), release.Version);
        Assert.Equal("scrcpy-win64-v4.1.zip", release.AssetName);
        Assert.Equal("https://github.com/Genymobile/scrcpy/releases/download/v4.1/scrcpy-win64-v4.1.zip", release.AssetUrl);
        Assert.Equal("5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db", release.Sha256);
    }

    [Fact]
    public void ParseRelease_WithoutAssets()
    {
        var release = UpdateChecker.ParseRelease("""{ "tag_name": "v0.3.0", "html_url": "https://github.com/x/y/releases/tag/v0.3.0" }""", "x/y", null);
        Assert.Equal(new Version(0, 3, 0), release!.Version);
        Assert.Null(release.AssetUrl);
        Assert.Null(UpdateChecker.ParseRelease("""{ "message": "Not Found" }""", "x/y", null));
    }

    [Fact]
    public void CurrentVersion_MatchesProject() => Assert.Equal(new Version(0, 4, 0), UpdateChecker.CurrentVersion);
}
