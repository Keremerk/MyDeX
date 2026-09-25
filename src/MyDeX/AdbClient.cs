using System.Text.RegularExpressions;

namespace MyDeX;

public sealed record PhoneDevice(string Serial, string State, string Model)
{
    public bool IsReady => State == "device";
    public bool IsWifi => Serial.Contains(':') || Serial.Contains("._adb-tls-connect");

    public override string ToString()
    {
        string link = IsWifi ? "Wi-Fi" : "USB";
        string state = State switch
        {
            "device" => "",
            "unauthorized" => "  [tap Allow on the phone]",
            "offline" => "  [offline]",
            _ => $"  [{State}]",
        };
        return $"{Model}  ({link})  {Serial}{state}";
    }
}

public sealed record BatteryInfo(int Percent, double TemperatureC, bool Charging);

public sealed partial class AdbClient(string adbPath)
{
    public string AdbPath { get; } = adbPath;

    public Task<ProcessResult> RunAsync(IEnumerable<string> args, int timeoutMs = 20_000) =>
        ProcessRunner.RunAsync(AdbPath, args, timeoutMs);

    public async Task<List<PhoneDevice>> GetDevicesAsync()
    {
        var result = await RunAsync(["devices", "-l"], 10_000).ConfigureAwait(false);
        return ParseDevices(result.Output);
    }

    public static List<PhoneDevice> ParseDevices(string output)
    {
        var devices = new List<PhoneDevice>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices") || line.StartsWith('*'))
                continue;
            var parts = Whitespace().Split(line);
            if (parts.Length < 2)
                continue;
            string model = parts.FirstOrDefault(p => p.StartsWith("model:"))?["model:".Length..].Replace('_', ' ') ?? "Android phone";
            devices.Add(new PhoneDevice(parts[0], parts[1], model));
        }
        return devices;
    }

    public Task<ProcessResult> ShellAsync(string serial, params string[] command) =>
        RunAsync(["-s", serial, "shell", .. command]);

    public Task<ProcessResult> PutGlobalSettingAsync(string serial, string key, string value) =>
        ShellAsync(serial, "settings", "put", "global", key, value);

    public Task<ProcessResult> DeleteGlobalSettingAsync(string serial, string key) =>
        ShellAsync(serial, "settings", "delete", "global", key);

    public Task<ProcessResult> PushAsync(string serial, string localPath, string remotePath) =>
        RunAsync(["-s", serial, "push", localPath, remotePath], timeoutMs: 60 * 60_000);

    public async Task<string?> GetWifiIpAsync(string serial)
    {
        var result = await ShellAsync(serial, "ip", "-f", "inet", "addr", "show", "wlan0").ConfigureAwait(false);
        return ParseWifiIp(result.Output);
    }

    public static string? ParseWifiIp(string output)
    {
        var match = InetAddress().Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    public async Task<BatteryInfo?> GetBatteryAsync(string serial)
    {
        var result = await ShellAsync(serial, "dumpsys", "battery").ConfigureAwait(false);
        return result.Ok ? ParseBattery(result.Output) : null;
    }

    /// <summary>Reads level, temperature (reported in tenths of °C) and charging state from <c>dumpsys battery</c>.</summary>
    public static BatteryInfo? ParseBattery(string output)
    {
        var matches = BatteryField().Matches(output.Replace("\r", ""));
        int? Field(string name)
        {
            // First match wins: the main "Current Battery Service state" block comes first.
            foreach (Match m in matches)
                if (m.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return int.Parse(m.Groups[2].Value);
            return null;
        }
        int? percent = Field("level"), temperature = Field("temperature"), status = Field("status");
        if (percent == null || temperature == null)
            return null;
        // BatteryManager.BATTERY_STATUS_CHARGING = 2, BATTERY_STATUS_FULL = 5
        return new BatteryInfo(percent.Value, temperature.Value / 10.0, status is 2 or 5);
    }

    public Task<ProcessResult> KillServerAsync() => RunAsync(["kill-server"], 10_000);

    /// <summary>The phone's built-in hardware serial (the same name it has over USB). Used to recognise it over Wi-Fi.</summary>
    public async Task<string?> GetHardwareSerialAsync(string serial)
    {
        var result = await ShellAsync(serial, "getprop", "ro.serialno").ConfigureAwait(false);
        string value = result.Output.Trim();
        return result.Ok && value.Length > 0 && !value.Contains(' ') ? value : null;
    }

    public sealed record DexLeftovers(int ExtraScreens, bool DexLauncherRunning, bool Overlay)
    {
        public bool IsClean => ExtraScreens == 0 && !DexLauncherRunning && !Overlay;
        public override string ToString() =>
            string.Join(", ", new[]
            {
                ExtraScreens > 0 ? $"{ExtraScreens} extra screen(s) still open" : null,
                DexLauncherRunning ? "DeX launcher still running" : null,
                Overlay ? "simulated display setting still set" : null,
            }.Where(s => s != null));
    }

    /// <summary>
    /// Looks for what a DeX session could leave behind: MyDeX/scrcpy or simulated ("Overlay") screens,
    /// DeX's launcher still active on a screen (not just remembered in Recents), and the overlay setting.
    /// </summary>
    public static DexLeftovers CheckForDexLeftovers(string displayDump, string activitiesDump, string overlaySetting)
    {
        int screens = ExtraScreen().Matches(displayDump).Select(m => m.Groups[1].Value).Distinct().Count();
        // Active tasks are listed per "Display #N"; the Recents section is not part of "dumpsys activity activities".
        bool launcher = activitiesDump.Contains("com.honeyspace.dexservice.SecondaryLauncher", StringComparison.Ordinal);
        string overlay = overlaySetting.Trim();
        bool overlaySet = overlay.Length > 0 && overlay != "null";
        return new DexLeftovers(screens, launcher, overlaySet);
    }

    /// <summary>Switches a phone that is in Wi-Fi mode ("adb tcpip") back to USB-only, so it stops listening on the network.</summary>
    public Task<ProcessResult> UsbModeAsync(string serial) => RunAsync(["-s", serial, "usb"], 10_000);

    public Task<ProcessResult> TcpipAsync(string serial, int port) =>
        RunAsync(["-s", serial, "tcpip", port.ToString()]);

    /// <summary>adb connect exits with 0 even when it fails, so success is read from its message.</summary>
    public async Task<(bool Ok, string Message)> ConnectAsync(string address)
    {
        var result = await RunAsync(["connect", address], 15_000).ConfigureAwait(false);
        return (IsConnectSuccess(result.Output), result.Output);
    }

    public static bool IsConnectSuccess(string output) =>
        output.Contains("connected to", StringComparison.OrdinalIgnoreCase)
        && !output.Contains("cannot", StringComparison.OrdinalIgnoreCase)
        && !output.Contains("failed", StringComparison.OrdinalIgnoreCase);

    public async Task<(bool Ok, string Message)> PairAsync(string address, string code)
    {
        var result = await RunAsync(["pair", address, code], 30_000).ConfigureAwait(false);
        return (result.Output.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase), result.Output);
    }

    public Task<ProcessResult> DisconnectAsync(string address) => RunAsync(["disconnect", address]);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"inet (\d{1,3}(?:\.\d{1,3}){3})/")]
    private static partial Regex InetAddress();

    [GeneratedRegex(@"^\s*([A-Za-z ]+):\s*(-?\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex BatteryField();

    // scrcpy's virtual screens are called "scrcpy"; Android's simulated displays "Overlay #1".
    [GeneratedRegex(@"DisplayInfo\{""(?:scrcpy|Overlay #\d+)"", displayId (\d+)")]
    private static partial Regex ExtraScreen();
}
