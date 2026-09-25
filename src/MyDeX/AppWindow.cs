using System.Diagnostics;

namespace MyDeX;

/// <summary>
/// An extra PC window next to the DeX desktop: either one phone app (e.g. a game) on its own
/// virtual display, or the phone's normal screen (<see cref="PhoneScreen"/>).
/// </summary>
public sealed class AppWindow(AdbClient adb, string scrcpyExe, AppSettings settings, PhoneApp app)
{
    /// <summary>Stands for "the phone's own screen" instead of an app.</summary>
    public static readonly PhoneApp PhoneScreen = new("Phone screen", "");

    Process? scrcpy;

    public PhoneApp App { get; } = app;
    public bool IsPhoneScreen => App.Package.Length == 0;
    /// <summary>Whether this window plays the phone's sound (only one window can – see <see cref="BuildArgs"/>).</summary>
    public bool HasSound { get; private set; }
    /// <summary>Whether this window receives the PC game controller.</summary>
    public bool HasGamepad { get; private set; }

    public event Action<string>? Log;
    public event Action? Ended;

    public bool IsRunning
    {
        get
        {
            try { return scrcpy is { HasExited: false }; }
            catch { return false; }
        }
    }

    /// <param name="soundElsewhere">
    /// Another MyDeX window already plays the phone's sound. Android captures the phone's sound as a
    /// whole (not per screen), so a second window would only get silence – this one stays muted and
    /// the app's sound comes out of that other window.
    /// </param>
    public static List<string> BuildArgs(AppSettings settings, PhoneDevice device, PhoneApp app, string? recordPath, bool soundElsewhere, bool gamepadElsewhere = false)
    {
        var profile = settings.Profile;
        bool phoneScreen = app.Package.Length == 0;
        var args = new List<string> { "-s", device.Serial, $"--window-title={app.Name} - MyDeX" };
        if (!phoneScreen)
        {
            string size = settings.AppShape == AppWindowShape.Portrait ? "1080x1920" : "1920x1080";
            args.Add($"--new-display={size}/{settings.AppDpi}");
            args.Add($"--start-app={app.Package}");
            if (settings.KeepAppsOnStop) args.Add("--no-vd-destroy-content");
        }
        args.AddRange(Scrcpy.StreamArgs(settings));
        if (soundElsewhere && profile.Audio) args.Add("--no-audio");
        // Like sound, the controller goes to one window only, or every button press would arrive twice.
        if (settings.Controller == ControllerTarget.AppWindows && !gamepadElsewhere) args.Add("--gamepad=uhid");
        // Relative mouse for shooters; Left Alt gives the mouse back to Windows.
        if (settings.AppMouseCapture && !phoneScreen) args.Add("--mouse=uhid");
        if (profile.AlwaysOnTop) args.Add("--always-on-top");
        if (recordPath != null) args.Add($"--record={recordPath}");
        return args;
    }

    public void Start(PhoneDevice device, bool soundElsewhere, bool gamepadElsewhere)
    {
        string? recordPath = Scrcpy.RecordingPath(settings, App.Name, DateTime.Now);
        if (recordPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
            Log?.Invoke($"Recording to {recordPath}");
        }

        var args = BuildArgs(settings, device, App, recordPath, soundElsewhere, gamepadElsewhere);
        HasGamepad = args.Contains("--gamepad=uhid");
        HasSound = !args.Contains("--no-audio");
        if (!HasSound && settings.Profile.Audio)
            Log?.Invoke($"{App.Name}: its sound plays through the window that already has the phone's sound.");
        try
        {
            scrcpy = Scrcpy.Start(scrcpyExe, adb.AdbPath, args, Log, code =>
            {
                Log?.Invoke($"{App.Name} window closed (scrcpy exit code {code}).");
                Ended?.Invoke();
            });
        }
        catch (Exception ex)
        {
            throw new DexException($"Could not open {App.Name}: {ex.Message}");
        }
    }

    public Task StopAsync() => scrcpy != null && IsRunning ? Scrcpy.CloseAsync(scrcpy) : Task.CompletedTask;
}
