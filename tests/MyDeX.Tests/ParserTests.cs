namespace MyDeX.Tests;

/// <summary>Parsing of adb and scrcpy output. Samples match what a Galaxy S24 Ultra / scrcpy 4.1 print.</summary>
public class ParserTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ParseDevices_ReadsUsbWifiAndUnauthorizedPhones()
    {
        const string output = """
            * daemon not running; starting now at tcp:5037
            * daemon started successfully
            List of devices attached
            RFTEST00001            device product:e3qxxx model:SM_S928B device:e3q transport_id:10
            192.168.1.20:5555      device product:e3qxxx model:SM_S928B device:e3q transport_id:11
            adb-R5CX-abc._adb-tls-connect._tcp device product:x model:SM_A556B device:a55 transport_id:12
            R58M0000000            unauthorized transport_id:13

            """;

        var devices = AdbClient.ParseDevices(output);

        Assert.Equal(4, devices.Count);
        Assert.Equal(new PhoneDevice("RFTEST00001", "device", "SM S928B"), devices[0]);
        Assert.False(devices[0].IsWifi);
        Assert.True(devices[0].IsReady);
        Assert.True(devices[1].IsWifi);
        Assert.True(devices[2].IsWifi);          // mDNS (wireless debugging) serials count as Wi-Fi too
        Assert.False(devices[3].IsReady);
        Assert.Equal("Android phone", devices[3].Model);
        Assert.Contains("tap Allow", devices[3].ToString());
    }

    [Fact]
    public void ParseDevices_EmptyList()
    {
        Assert.Empty(AdbClient.ParseDevices("List of devices attached\r\n\r\n"));
        Assert.Empty(AdbClient.ParseDevices(""));
    }

    [Theory]
    [InlineData("    inet 192.168.1.23/24 brd 192.168.1.255 scope global wlan0", "192.168.1.23")]
    [InlineData("Device \"wlan0\" does not exist.", null)]
    public void ParseWifiIp(string output, string? expected) => Assert.Equal(expected, AdbClient.ParseWifiIp(output));

    [Theory]
    [InlineData("connected to 192.168.1.20:5555", true)]
    [InlineData("already connected to 192.168.1.20:5555", true)]
    [InlineData("failed to connect to '192.168.1.20:5555': Connection refused", false)]
    [InlineData("cannot connect to 192.168.1.20:5555: No route to host (10065)", false)]
    [InlineData("", false)]
    public void IsConnectSuccess(string output, bool expected) => Assert.Equal(expected, AdbClient.IsConnectSuccess(output));

    [Fact]
    public void ParseBattery_RealS24UltraOutput()
    {
        var battery = AdbClient.ParseBattery(Fixture("dumpsys-battery-s24ultra.txt"));

        Assert.NotNull(battery);
        Assert.Equal(59, battery.Percent);
        Assert.Equal(37.7, battery.TemperatureC, precision: 1);
        Assert.True(battery.Charging);   // status: 2
    }

    [Fact]
    public void ParseBattery_NotCharging_AndGarbage()
    {
        var battery = AdbClient.ParseBattery("Current Battery Service state:\n  status: 3\n  level: 12\n  temperature: 305\n");
        Assert.Equal(new BatteryInfo(12, 30.5, false), battery);
        Assert.Null(AdbClient.ParseBattery("error: device offline"));
    }

    [Fact]
    public void ParseApps_HandlesLongNamesDuplicatesAndBothPrefixes()
    {
        const string output = """
            [server] INFO: List of apps:
             * Chrome                         com.android.chrome
             - Highway Overtake - Car Racing  com.hypermonkgames.HighwayOvertake
             * Live Transcribe & Sound Notifications com.google.audio.hearing.visualization.accessibility.scribe
             * Chrome                         com.android.chrome
             - 2048                           com.example.game2048
            """;

        var apps = Scrcpy.ParseApps(output.Replace("\n", "\r\n"));   // scrcpy on Windows prints CRLF

        Assert.Equal(4, apps.Count);
        Assert.Equal(["2048", "Chrome", "Highway Overtake - Car Racing", "Live Transcribe & Sound Notifications"], apps.Select(a => a.Name));
        Assert.Equal("com.hypermonkgames.HighwayOvertake", apps[2].Package);
        Assert.Equal("com.google.audio.hearing.visualization.accessibility.scribe", apps[3].Package);
    }

    [Fact]
    public void ParseDisplayIds()
    {
        const string output = """
            [server] INFO: List of displays:
                --display-id=0    (1440x3120)
                --display-id=2    (1920x1080)
            """;
        Assert.Equal([0, 2], Scrcpy.ParseDisplayIds(output).Order());
    }

    [Theory]
    [InlineData("scrcpy 4.1 <https://github.com/Genymobile/scrcpy>", "4.1.0")]
    [InlineData("scrcpy 3.3.2 <https://github.com/Genymobile/scrcpy>", "3.3.2")]
    [InlineData("'scrcpy' is not recognized", null)]
    public void ParseScrcpyVersion(string output, string? expected) =>
        Assert.Equal(expected, Scrcpy.ParseVersion(output)?.ToString());
}
