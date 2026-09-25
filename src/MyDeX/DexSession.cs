using System.Diagnostics;

namespace MyDeX;

public sealed class DexException(string message) : Exception(message);

/// <summary>
/// One running DeX window: creates a second display on the phone (DeX starts on it because One UI
/// treats it like an external monitor) and shows that display on the PC with scrcpy.
/// The phone's own screen stays free, so you can keep using it while DeX runs.
/// </summary>
public sealed class DexSession(AdbClient adb, string scrcpyExe, AppSettings settings)
{
    const string OverlaySetting = "overlay_display_devices";

    readonly SemaphoreSlim cleanupLock = new(1, 1);
    Process? scrcpy;
    string? overlaySerial;
    /// <summary>Set by <see cref="StopAsync"/>; a start that is still in progress gives up at its next step.</summary>
    volatile bool stopRequested;

    async Task ThrowIfStoppedAsync()
    {
        if (!stopRequested) return;
        await CleanupAsync().ConfigureAwait(false);
        throw new OperationCanceledException("DeX was stopped while starting.");
    }

    public event Action<string>? Log;
    /// <summary>Raised when the DeX window has closed, with scrcpy's exit code (see <see cref="Scrcpy.ExitDisconnected"/>).</summary>
    public event Action<int>? Ended;

    public string? RecordingPath { get; private set; }
    /// <summary>Whether this DeX window plays the phone's sound (only one MyDeX window can).</summary>
    public bool HasSound { get; private set; }

    public bool IsRunning
    {
        get
        {
            try { return scrcpy is { HasExited: false }; }
            catch { return false; }
        }
    }

    /// <summary>The scrcpy options for a DeX window. <paramref name="displayId"/> is set for the simulated-display method.</summary>
    public static List<string> BuildArgs(AppSettings settings, PhoneDevice device, int? displayId, string? recordPath, bool soundElsewhere = false)
    {
        var profile = settings.Profile;
        var args = new List<string> { "-s", device.Serial, $"--window-title=MyDeX - {device.Model}" };
        if (displayId != null)
        {
            args.Add($"--display-id={displayId}");
        }
        else
        {
            args.Add($"--new-display={profile.Width}x{profile.Height}/{profile.Dpi}");
            // Apps left open in DeX move to the phone screen when DeX stops, instead of closing.
            if (settings.KeepAppsOnStop)
                args.Add("--no-vd-destroy-content");
        }
        args.AddRange(Scrcpy.StreamArgs(settings));
        // An app window already plays the phone's sound; a second capture would only get silence.
        if (soundElsewhere && profile.Audio) args.Add("--no-audio");
        if (settings.Controller == ControllerTarget.DexDesktop) args.Add("--gamepad=uhid");
        if (profile.Fullscreen) args.Add("--fullscreen");
        if (profile.AlwaysOnTop) args.Add("--always-on-top");
        if (settings.StayAwake) args.Add("--stay-awake");
        if (recordPath != null) args.Add($"--record={recordPath}");
        return args;
    }

    public async Task StartAsync(PhoneDevice device, bool soundElsewhere = false)
    {
        if (IsRunning)
            throw new DexException("DeX is already running.");

        var profile = settings.Profile;
        string size = $"{profile.Width}x{profile.Height}/{profile.Dpi}";

        // Folder that files dropped on the DeX window (or the Files tab) are sent to.
        await adb.ShellAsync(device.Serial, "mkdir", "-p", Scrcpy.PushTarget).ConfigureAwait(false);
        await ThrowIfStoppedAsync().ConfigureAwait(false);

        int? displayId = null;
        if (settings.Method == DisplayMethod.SimulatedDisplay)
        {
            var before = await Scrcpy.ListDisplaysAsync(scrcpyExe, adb.AdbPath, device.Serial).ConfigureAwait(false);
            Log?.Invoke($"Creating a {size} display on the phone...");
            var put = await adb.PutGlobalSettingAsync(device.Serial, OverlaySetting, size).ConfigureAwait(false);
            if (!put.Ok)
                throw new DexException($"The phone refused to create the display: {put.Output}");

            overlaySerial = device.Serial;
            settings.PendingOverlayCleanup = device.Serial;
            settings.Save();

            for (int attempt = 0; attempt < 16 && displayId == null; attempt++)
            {
                await Task.Delay(500).ConfigureAwait(false);
                await ThrowIfStoppedAsync().ConfigureAwait(false);
                var now = await Scrcpy.ListDisplaysAsync(scrcpyExe, adb.AdbPath, device.Serial).ConfigureAwait(false);
                displayId = now.Except(before).Select(id => (int?)id).FirstOrDefault();
            }
            if (displayId == null)
            {
                await CleanupAsync().ConfigureAwait(false);
                throw new DexException("The phone did not create the second display. " +
                                       "Try the \"scrcpy virtual display\" method on the Display tab.");
            }
            Log?.Invoke($"Display {displayId} is ready. If the phone asks to start DeX, tap Start.");
        }

        RecordingPath = Scrcpy.RecordingPath(settings, $"DeX {device.Model}", DateTime.Now);
        if (RecordingPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordingPath)!);
            Log?.Invoke($"Recording to {RecordingPath}");
        }

        var args = BuildArgs(settings, device, displayId, RecordingPath, soundElsewhere);
        HasSound = !args.Contains("--no-audio");
        await ThrowIfStoppedAsync().ConfigureAwait(false);
        try
        {
            scrcpy = Scrcpy.Start(scrcpyExe, adb.AdbPath, args, Log, async code =>
            {
                Log?.Invoke($"DeX window closed (scrcpy exit code {code}).");
                await CleanupAsync().ConfigureAwait(false);
                Ended?.Invoke(code);
            });
        }
        catch (Exception ex)
        {
            await CleanupAsync().ConfigureAwait(false);
            throw new DexException($"Could not start scrcpy: {ex.Message}");
        }
        // Stop arrived in the instant between the last check and scrcpy starting.
        if (stopRequested)
            await Scrcpy.CloseAsync(scrcpy).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        stopRequested = true;
        if (scrcpy != null && IsRunning)
            await Scrcpy.CloseAsync(scrcpy).ConfigureAwait(false);
        await CleanupAsync().ConfigureAwait(false);
    }

    async Task CleanupAsync()
    {
        await cleanupLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (overlaySerial == null)
                return;
            var result = await adb.DeleteGlobalSettingAsync(overlaySerial, OverlaySetting).ConfigureAwait(false);
            if (result.Ok)
            {
                Log?.Invoke("Removed the extra display from the phone.");
                settings.PendingOverlayCleanup = null;
                settings.Save();
            }
            else
            {
                // Phone probably got unplugged; MainForm retries when it reconnects.
                Log?.Invoke("Could not remove the extra display yet; it will be removed when the phone reconnects.");
            }
            overlaySerial = null;
        }
        finally
        {
            cleanupLock.Release();
        }
    }
}
