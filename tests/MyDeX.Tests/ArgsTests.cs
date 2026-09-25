namespace MyDeX.Tests;

/// <summary>The scrcpy options each window gets – this is where most features actually take effect.</summary>
public class ArgsTests
{
    static readonly PhoneDevice Phone = new("RFTEST00001", "device", "SM S928B");
    static readonly PhoneApp Game = new("Highway Overtake", "com.hypermonkgames.HighwayOvertake");

    static AppSettings Defaults() => AppSettings.FromJson(null);

    [Fact]
    public void DexDefaults_UseInvisibleVirtualDisplayAndController()
    {
        var args = DexSession.BuildArgs(Defaults(), Phone, displayId: null, recordPath: null);

        Assert.Equal(["-s", "RFTEST00001"], args.Take(2));
        Assert.Contains("--new-display=1920x1080/160", args);     // Gaming profile
        Assert.Contains("--max-fps=120", args);
        Assert.Contains("--video-bit-rate=24M", args);
        Assert.Contains("--video-codec=h264", args);
        Assert.Contains("--no-vd-destroy-content", args);          // keep apps when DeX stops
        Assert.Contains("--gamepad=uhid", args);                   // controller → DeX desktop
        Assert.Contains($"--push-target={Scrcpy.PushTarget}", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--display-id"));
        Assert.DoesNotContain(args, a => a.StartsWith("--record"));
        Assert.DoesNotContain("--no-audio", args);
    }

    [Fact]
    public void Dex_SimulatedDisplay_TargetsTheDisplayId()
    {
        var settings = Defaults();
        settings.Method = DisplayMethod.SimulatedDisplay;

        var args = DexSession.BuildArgs(settings, Phone, displayId: 3, recordPath: null);

        Assert.Contains("--display-id=3", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--new-display"));
        Assert.DoesNotContain("--no-vd-destroy-content", args);    // only meaningful for virtual displays
    }

    [Fact]
    public void Dex_OptionsFollowSettings()
    {
        var settings = Defaults();
        settings.ActiveProfile = "Work";
        settings.Profile.Audio = false;
        settings.Profile.Fullscreen = true;
        settings.Controller = ControllerTarget.AppWindows;
        settings.KeepAppsOnStop = false;
        settings.StayAwake = true;

        var args = DexSession.BuildArgs(settings, Phone, null, @"C:\Videos\MyDeX\clip.mp4");

        Assert.Contains("--new-display=2560x1440/200", args);
        Assert.Contains("--video-codec=h265", args);
        Assert.Contains("--no-audio", args);
        Assert.Contains("--fullscreen", args);
        Assert.Contains("--stay-awake", args);
        Assert.Contains(@"--record=C:\Videos\MyDeX\clip.mp4", args);
        Assert.DoesNotContain("--gamepad=uhid", args);            // controller goes to app windows instead
        Assert.DoesNotContain("--no-vd-destroy-content", args);
    }

    [Fact]
    public void AppWindow_StartsTheAppOnItsOwnDisplay()
    {
        var args = AppWindow.BuildArgs(Defaults(), Phone, Game, recordPath: null, soundElsewhere: false);

        Assert.Contains("--new-display=1920x1080/280", args);
        Assert.Contains("--start-app=com.hypermonkgames.HighwayOvertake", args);
        Assert.Contains("--window-title=Highway Overtake - MyDeX", args);
        Assert.DoesNotContain("--gamepad=uhid", args);             // default controller target is the DeX desktop
        Assert.DoesNotContain("--mouse=uhid", args);
    }

    [Fact]
    public void AppWindow_PortraitControllerAndMouseCapture()
    {
        var settings = Defaults();
        settings.AppShape = AppWindowShape.Portrait;
        settings.Controller = ControllerTarget.AppWindows;
        settings.AppMouseCapture = true;

        var args = AppWindow.BuildArgs(settings, Phone, Game, recordPath: null, soundElsewhere: false);

        Assert.Contains("--new-display=1080x1920/280", args);
        Assert.Contains("--gamepad=uhid", args);
        Assert.Contains("--mouse=uhid", args);
    }

    [Fact]
    public void ControllerNeverGoesToBothPlaces()
    {
        foreach (var target in Enum.GetValues<ControllerTarget>())
        {
            var settings = Defaults();
            settings.Controller = target;
            int count = DexSession.BuildArgs(settings, Phone, null, null).Count(a => a == "--gamepad=uhid")
                        + AppWindow.BuildArgs(settings, Phone, Game, null, false).Count(a => a == "--gamepad=uhid");
            Assert.Equal(target == ControllerTarget.Off ? 0 : 1, count);
        }
    }

    [Fact]
    public void RecordingPath_OffByDefault_SafeFileNameWhenOn()
    {
        var settings = Defaults();
        var when = new DateTime(2026, 9, 25, 14, 30, 5);
        Assert.Null(Scrcpy.RecordingPath(settings, "DeX SM S928B", when));

        settings.RecordSessions = true;
        settings.RecordingsFolder = @"C:\Videos\MyDeX";
        var path = Scrcpy.RecordingPath(settings, "Game: <Best>?", when);

        Assert.Equal(@"C:\Videos\MyDeX\Game_ _Best__ 2026-09-25 14-30-05.mp4", path);
    }
}
