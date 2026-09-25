namespace MyDeX.Tests;

/// <summary>Regression tests for the second code review (real cable pull, one controller window).</summary>
public class ReconnectAndControllerTests
{
    static readonly PhoneDevice Phone = new("RFTEST00001", "device", "SM S928B");

    [Theory]
    // A session that was running and then failed = the connection went away, whatever the code.
    [InlineData(2, 300, false, true)]     // scrcpy: "Device disconnected"
    [InlineData(1, 300, false, true)]     // real cable pull while sending input: "Controller error"
    [InlineData(-1, 60, false, true)]     // process gone without a code
    [InlineData(2, 1, false, true)]       // disconnect right after starting still counts
    // Closed normally – never reconnect.
    [InlineData(0, 300, false, false)]
    [InlineData(0, 1, true, false)]
    // A brand-new start that fails at once is a real error, not a disconnect (no retry loop)…
    [InlineData(1, 2, false, false)]
    // …but right after a reconnect the phone may still be settling, so keep trying (capped elsewhere).
    [InlineData(1, 2, true, true)]
    public void LooksLikeConnectionLoss(int exitCode, int secondsRunning, bool wasReconnect, bool expected) =>
        Assert.Equal(expected, Scrcpy.LooksLikeConnectionLoss(exitCode, TimeSpan.FromSeconds(secondsRunning), wasReconnect));

    [Fact]
    public void Controller_GoesToTheFirstAppWindowOnly()
    {
        var settings = AppSettings.FromJson(null);
        settings.Controller = ControllerTarget.AppWindows;
        var game = new PhoneApp("Game", "com.example.game");

        Assert.Contains("--gamepad=uhid", AppWindow.BuildArgs(settings, Phone, game, null, soundElsewhere: false, gamepadElsewhere: false));
        Assert.DoesNotContain("--gamepad=uhid", AppWindow.BuildArgs(settings, Phone, game, null, soundElsewhere: false, gamepadElsewhere: true));
    }
}
