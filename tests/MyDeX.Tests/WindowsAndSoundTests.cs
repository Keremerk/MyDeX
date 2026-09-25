namespace MyDeX.Tests;

/// <summary>Extra windows (apps, phone screen) and the "only one window plays the phone's sound" rule.</summary>
public class WindowsAndSoundTests
{
    static readonly PhoneDevice Phone = new("RFTEST00001", "device", "SM S928B");
    static readonly PhoneApp Game = new("Highway Overtake", "com.hypermonkgames.HighwayOvertake");

    [Fact]
    public void AppWindow_IsMutedWhenAnotherWindowHasTheSound()
    {
        var settings = AppSettings.FromJson(null);

        Assert.DoesNotContain("--no-audio", AppWindow.BuildArgs(settings, Phone, Game, null, soundElsewhere: false));
        Assert.Contains("--no-audio", AppWindow.BuildArgs(settings, Phone, Game, null, soundElsewhere: true));
    }

    [Fact]
    public void DexWindow_IsMutedWhenAnAppWindowHasTheSound()
    {
        var settings = AppSettings.FromJson(null);

        Assert.DoesNotContain("--no-audio", DexSession.BuildArgs(settings, Phone, null, null, soundElsewhere: false));
        Assert.Contains("--no-audio", DexSession.BuildArgs(settings, Phone, null, null, soundElsewhere: true));
    }

    [Fact]
    public void SoundOff_GivesExactlyOneNoAudioFlag()
    {
        var settings = AppSettings.FromJson(null);
        settings.Profile.Audio = false;

        Assert.Single(AppWindow.BuildArgs(settings, Phone, Game, null, soundElsewhere: true), a => a == "--no-audio");
        Assert.Single(DexSession.BuildArgs(settings, Phone, null, null, soundElsewhere: true), a => a == "--no-audio");
    }

    [Fact]
    public void PhoneScreenWindow_MirrorsThePhoneInsteadOfMakingANewScreen()
    {
        var settings = AppSettings.FromJson(null);
        settings.AppMouseCapture = true;

        var args = AppWindow.BuildArgs(settings, Phone, AppWindow.PhoneScreen, null, soundElsewhere: true);

        Assert.Contains("--window-title=Phone screen - MyDeX", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--new-display"));
        Assert.DoesNotContain(args, a => a.StartsWith("--start-app"));
        Assert.DoesNotContain("--no-vd-destroy-content", args);
        Assert.DoesNotContain("--mouse=uhid", args);   // you use the phone screen like a normal window
        Assert.Contains("--no-audio", args);
    }

    [Fact]
    public void PhoneScreen_IsNeverSavedAsARecentApp()
    {
        var settings = AppSettings.FromJson(null);
        settings.AddRecentApp(AppWindow.PhoneScreen);
        Assert.Empty(settings.RecentApps);
    }

    [Fact]
    public void Pinning_TogglesAndSurvivesSaveLoad()
    {
        var settings = AppSettings.FromJson(null);

        Assert.True(settings.TogglePinned(Game));
        Assert.True(settings.IsPinned(Game));
        var loaded = AppSettings.FromJson(settings.ToJson());
        Assert.True(loaded.IsPinned(Game));

        Assert.False(loaded.TogglePinned(Game));
        Assert.False(loaded.IsPinned(Game));
    }

    [Fact]
    public void DamagedPinnedList_IsCleanedUp()
    {
        var settings = AppSettings.FromJson("""
            { "PinnedApps": [ { "Name": "A", "Package": "com.a" }, { "Name": "A again", "Package": "com.a" },
                              { "Name": "Empty", "Package": "" }, null ] }
            """);
        Assert.Equal("com.a", Assert.Single(settings.PinnedApps).Package);
    }
}
