using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MyDeX;

public sealed record PhoneApp(string Name, string Package)
{
    public override string ToString() => $"{Name}   ({Package})";
}

/// <summary>Shared helpers for launching and querying scrcpy.</summary>
public static partial class Scrcpy
{
    public const string PushTarget = "/sdcard/Download/MyDeX/";

    /// <summary>scrcpy's exit code when the phone disconnected (as opposed to 0 = closed normally, 1 = error).</summary>
    public const int ExitDisconnected = 2;

    /// <summary>
    /// Whether a scrcpy exit means "the phone went away" (so auto-reconnect should wait for it).
    /// Exit code 2 is scrcpy noticing the disconnect. A real cable pull can also end with 1: if scrcpy
    /// was sending input at that moment the write fails ("Controller error"). So any failure of a
    /// session that was already running counts. A normal start that fails within seconds is a real
    /// error (bad option, ...) – retrying that would only loop.
    /// </summary>
    public static bool LooksLikeConnectionLoss(int exitCode, TimeSpan ranFor, bool wasReconnectStart) =>
        exitCode == ExitDisconnected
        || (exitCode != 0 && (ranFor > TimeSpan.FromSeconds(5) || wasReconnectStart));

    /// <summary>Options every scrcpy window gets from the active profile.</summary>
    public static IEnumerable<string> StreamArgs(AppSettings settings)
    {
        var profile = settings.Profile;
        yield return $"--max-fps={profile.MaxFps}";
        yield return $"--video-bit-rate={profile.BitrateMbps}M";
        yield return $"--video-codec={profile.VideoCodec}";
        yield return $"--push-target={PushTarget}";
        if (!profile.Audio)
            yield return "--no-audio";
        // Privacy: without this, text copied on the PC (passwords too) is copied to the phone and back.
        if (!settings.ShareClipboard)
            yield return "--no-clipboard-autosync";
    }

    /// <summary>Where a recording of this window goes, or null when recording is off.</summary>
    public static string? RecordingPath(AppSettings settings, string label, DateTime now)
    {
        if (!settings.RecordSessions)
            return null;
        string safe = string.Concat(label.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return Path.Combine(settings.RecordingsFolder, $"{safe} {now:yyyy-MM-dd HH-mm-ss}.mp4");
    }

    /// <summary>Starts scrcpy hidden (only its video window shows) and streams its output to <paramref name="log"/>.</summary>
    public static Process Start(string scrcpyExe, string adbPath, IReadOnlyList<string> args, Action<string>? log, Action<int> onExit)
    {
        var psi = new ProcessStartInfo(scrcpyExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["ADB"] = adbPath;   // make scrcpy use the same adb as MyDeX

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log?.Invoke(e.Data); };
        process.Exited += (_, _) =>
        {
            int code = -1;
            try { code = process.ExitCode; } catch { /* ignore */ }
            onExit(code);
        };

        log?.Invoke("scrcpy " + string.Join(' ', args));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>
    /// Closes the scrcpy window like the user would (so recordings are finalized and the phone is
    /// cleaned up), and only kills the process if it doesn't exit in time.
    /// </summary>
    public static async Task CloseAsync(Process process, int timeoutMs = 4000)
    {
        try
        {
            if (process.HasExited) return;
            process.Refresh();
            if (process.CloseMainWindow())
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) { /* fall through to kill */ }
            }
            process.Kill();
            using var killWait = new CancellationTokenSource(3000);
            await process.WaitForExitAsync(killWait.Token).ConfigureAwait(false);
        }
        catch
        {
            // Already gone.
        }
    }

    public static async Task<HashSet<int>> ListDisplaysAsync(string scrcpyExe, string adbPath, string serial)
    {
        var result = await RunAsync(scrcpyExe, adbPath, ["-s", serial, "--list-displays"], 20_000).ConfigureAwait(false);
        return ParseDisplayIds(result.Output);
    }

    public static HashSet<int> ParseDisplayIds(string output) =>
        DisplayIdPattern().Matches(output).Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();

    /// <summary>Launchable apps on the phone, sorted by name. Takes a few seconds.</summary>
    public static async Task<List<PhoneApp>> ListAppsAsync(string scrcpyExe, string adbPath, string serial)
    {
        var result = await RunAsync(scrcpyExe, adbPath, ["-s", serial, "--list-apps"], 90_000).ConfigureAwait(false);
        return ParseApps(result.Output);
    }

    public static List<PhoneApp> ParseApps(string output) =>
        AppLinePattern().Matches(output.Replace("\r", ""))
            .Select(m => new PhoneApp(m.Groups[1].Value.Trim(), m.Groups[2].Value))
            .DistinctBy(a => a.Package)
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static async Task<Version?> GetVersionAsync(string scrcpyExe)
    {
        var result = await ProcessRunner.RunAsync(scrcpyExe, ["--version"], 10_000).ConfigureAwait(false);
        return ParseVersion(result.Output);
    }

    /// <summary>"scrcpy 4.1 &lt;https://...&gt;" → 4.1.0</summary>
    public static Version? ParseVersion(string output)
    {
        var match = VersionPattern().Match(output);
        return match.Success ? UpdateChecker.ParseVersion(match.Groups[1].Value) : null;
    }

    static Task<ProcessResult> RunAsync(string scrcpyExe, string adbPath, IEnumerable<string> args, int timeoutMs) =>
        ProcessRunner.RunAsync(scrcpyExe, args, timeoutMs, new Dictionary<string, string> { ["ADB"] = adbPath });

    [GeneratedRegex(@"--display-id=(\d+)")]
    private static partial Regex DisplayIdPattern();

    // " * Chrome                         com.android.chrome"  (* = system app, - = installed app)
    [GeneratedRegex(@"^\s*[*-]\s+(.+?)\s+([A-Za-z][\w]*(?:\.[\w]+)+)\s*$", RegexOptions.Multiline)]
    private static partial Regex AppLinePattern();

    [GeneratedRegex(@"scrcpy\s+v?(\d+(?:\.\d+){0,3})")]
    private static partial Regex VersionPattern();
}
