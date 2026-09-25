using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyDeX;

public enum DisplayMethod
{
    /// <summary>Android "Simulate secondary displays" (overlay_display_devices). Fallback: Android also shows a floating copy of it on the phone.</summary>
    SimulatedDisplay,
    /// <summary>scrcpy --new-display. Nothing shows on the phone and the display disappears with scrcpy.</summary>
    ScrcpyVirtualDisplay,
}

/// <summary>Which scrcpy window forwards a PC game controller. Only one may, or every button press arrives twice.</summary>
public enum ControllerTarget { Off, DexDesktop, AppWindows }

public enum AppWindowShape { Landscape, Portrait }

/// <summary>Picture and streaming settings you can switch between with one click (e.g. Gaming / Work).</summary>
public sealed class DisplayProfile
{
    public string Name { get; set; } = "Custom";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Dpi { get; set; } = 160;
    public int MaxFps { get; set; } = 60;
    public int BitrateMbps { get; set; } = 16;
    public string VideoCodec { get; set; } = "h264";
    public bool Audio { get; set; } = true;
    public bool Fullscreen { get; set; }
    public bool AlwaysOnTop { get; set; }

    public DisplayProfile CopyAs(string name)
    {
        var copy = (DisplayProfile)MemberwiseClone();
        copy.Name = name;
        return copy;
    }

    /// <summary>
    /// DeX DPI that makes things look as big as Windows does at the given scaling
    /// (160 dpi at 100%, 200 at 125%, 240 at 150%...), rounded to 10.
    /// </summary>
    public static int DpiForWindowsScale(double scale) =>
        Math.Clamp((int)Math.Round(160 * scale / 10.0) * 10, 80, 640);

    public static DisplayProfile Gaming() => new()
    {
        Name = "Gaming", Width = 1920, Height = 1080, Dpi = 160, MaxFps = 120, BitrateMbps = 24, VideoCodec = "h264",
    };

    public static DisplayProfile Work() => new()
    {
        Name = "Work", Width = 2560, Height = 1440, Dpi = 200, MaxFps = 60, BitrateMbps = 20, VideoCodec = "h265",
    };
}

public sealed class AppSettings
{
    // Profiles
    public List<DisplayProfile> Profiles { get; set; } = [];
    public string ActiveProfile { get; set; } = "Gaming";

    [JsonIgnore]
    public DisplayProfile Profile => Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Profiles[0];

    // Shared by all profiles
    public DisplayMethod Method { get; set; } = DisplayMethod.ScrcpyVirtualDisplay;
    public ControllerTarget Controller { get; set; } = ControllerTarget.DexDesktop;
    public bool KeepAppsOnStop { get; set; } = true;
    public bool StayAwake { get; set; }
    /// <summary>Copy/paste between PC and phone. On by default (it is what people expect); can be turned off for privacy.</summary>
    public bool ShareClipboard { get; set; } = true;

    // App windows
    public AppWindowShape AppShape { get; set; } = AppWindowShape.Landscape;
    public int AppDpi { get; set; } = 280;
    /// <summary>Lock the mouse inside app windows (relative mouse for shooters); Left Alt releases it.</summary>
    public bool AppMouseCapture { get; set; }
    public List<PhoneApp> RecentApps { get; set; } = [];
    /// <summary>Apps shown as big buttons on the Home tab.</summary>
    public List<PhoneApp> PinnedApps { get; set; } = [];

    // Recording
    public bool RecordSessions { get; set; }
    public string RecordingsFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "MyDeX");

    // App behaviour
    public bool StartWithWindows { get; set; }
    public bool AutoStartDex { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    /// <summary>Ctrl+Alt+D starts/stops DeX from anywhere in Windows.</summary>
    public bool HotkeyEnabled { get; set; } = true;
    public int HeatWarningC { get; set; } = 42;
    public bool CheckForUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }

    // Remembered state
    public string? ScrcpyFolder { get; set; }
    public string? LastSerial { get; set; }
    public string? LastWifiAddress { get; set; }
    /// <summary>
    /// Hardware serial of the phone at <see cref="LastWifiAddress"/>. MyDeX only reconnects to that address
    /// by itself if the phone answering there has this serial – on another network the same address could
    /// belong to someone else's device.
    /// </summary>
    public string? WifiPhoneSerial { get; set; }
    /// <summary>Switch the phone back to USB-only when MyDeX closes, so it doesn't keep listening on the network.</summary>
    public bool WifiOffOnExit { get; set; } = true;
    /// <summary>Serial of a phone that still has our simulated display, e.g. because it was unplugged mid-session.</summary>
    public string? PendingOverlayCleanup { get; set; }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static string FolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyDeX");
    static string FilePath => Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        string? json = null;
        try
        {
            if (File.Exists(FilePath))
                json = File.ReadAllText(FilePath);
        }
        catch
        {
            // Unreadable file: start with defaults.
        }
        return FromJson(json);
    }

    public static AppSettings FromJson(string? json)
    {
        AppSettings settings = new();
        if (json != null)
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch
            {
                // A broken settings file should never stop the app from starting.
            }
        }
        settings.Sanitize();
        settings.EnsureProfiles(json);
        return settings;
    }

    /// <summary>Repairs values a hand-edited or damaged settings file could contain, so the window can always open.</summary>
    void Sanitize()
    {
        Profiles = (Profiles ?? []).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).ToList();
        RecentApps = (RecentApps ?? []).Where(a => a is { Name: not null, Package.Length: > 0 }).ToList();
        PinnedApps = (PinnedApps ?? []).Where(a => a is { Name: not null, Package.Length: > 0 }).DistinctBy(a => a.Package).ToList();
        ActiveProfile ??= "Gaming";
        RecordingsFolder = string.IsNullOrWhiteSpace(RecordingsFolder) ? new AppSettings().RecordingsFolder : RecordingsFolder;
        if (!Enum.IsDefined(Method)) Method = DisplayMethod.ScrcpyVirtualDisplay;
        if (!Enum.IsDefined(Controller)) Controller = ControllerTarget.DexDesktop;
        if (!Enum.IsDefined(AppShape)) AppShape = AppWindowShape.Landscape;
        foreach (var p in Profiles)
        {
            p.VideoCodec = p.VideoCodec is "h264" or "h265" or "av1" ? p.VideoCodec : "h264";
            p.Width = Math.Clamp(p.Width, 320, 7680);
            p.Height = Math.Clamp(p.Height, 240, 4320);
        }
    }

    public string ToJson()
    {
        lock (saveLock)
            return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>Pins an app to the Home tab, or unpins it if it is already there. Returns whether it is pinned now.</summary>
    public bool TogglePinned(PhoneApp app)
    {
        if (PinnedApps.RemoveAll(a => a.Package == app.Package) > 0)
            return false;
        PinnedApps.Add(app);
        return true;
    }

    public bool IsPinned(PhoneApp app) => PinnedApps.Any(a => a.Package == app.Package);

    /// <summary>Remembers an app for the tray's "Open app" menu (most recent first). The phone screen isn't an app.</summary>
    public void AddRecentApp(PhoneApp app, int max = 8)
    {
        if (app.Package.Length == 0) return;
        RecentApps.RemoveAll(a => a.Package == app.Package);
        RecentApps.Insert(0, app);
        if (RecentApps.Count > max)
            RecentApps.RemoveRange(max, RecentApps.Count - max);
    }

    /// <summary>Creates the built-in profiles; settings from before profiles existed become "My setup".</summary>
    void EnsureProfiles(string? json)
    {
        if (Profiles.Count > 0)
            return;

        Profiles = [DisplayProfile.Gaming(), DisplayProfile.Work()];
        ActiveProfile = "Gaming";
        if (json == null)
            return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Width", out _))
                return;
            var legacy = new DisplayProfile { Name = "My setup" };
            if (root.TryGetProperty("Width", out var v)) legacy.Width = v.GetInt32();
            if (root.TryGetProperty("Height", out v)) legacy.Height = v.GetInt32();
            if (root.TryGetProperty("Dpi", out v)) legacy.Dpi = v.GetInt32();
            if (root.TryGetProperty("MaxFps", out v)) legacy.MaxFps = v.GetInt32();
            if (root.TryGetProperty("BitrateMbps", out v)) legacy.BitrateMbps = v.GetInt32();
            if (root.TryGetProperty("VideoCodec", out v)) legacy.VideoCodec = v.GetString() ?? "h264";
            if (root.TryGetProperty("Audio", out v)) legacy.Audio = v.GetBoolean();
            if (root.TryGetProperty("Fullscreen", out v)) legacy.Fullscreen = v.GetBoolean();
            if (root.TryGetProperty("AlwaysOnTop", out v)) legacy.AlwaysOnTop = v.GetBoolean();
            Profiles.Add(legacy);
            ActiveProfile = legacy.Name;
        }
        catch
        {
            // Keep the built-in profiles.
        }
    }

    readonly object saveLock = new();

    /// <summary>
    /// Saves never overlap (DeX sessions save from background threads, the window from the UI thread).
    /// If the UI changes a list mid-save, that one save is skipped; the next save writes it.
    /// </summary>
    public void Save()
    {
        lock (saveLock)
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                File.WriteAllText(FilePath, ToJson());
            }
            catch
            {
                // Not fatal; settings just won't persist.
            }
        }
    }
}
