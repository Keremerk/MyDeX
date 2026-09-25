using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MyDeX;

public readonly record struct ProcessResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

static class ProcessRunner
{
    /// <summary>Runs a console tool hidden and returns its exit code plus combined stdout/stderr.</summary>
    public static async Task<ProcessResult> RunAsync(string exe, IEnumerable<string> args, int timeoutMs = 20_000,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo(exe)
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
        if (environment != null)
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}.");
        }
        catch (Win32Exception ex)
        {
            return new ProcessResult(-1, $"Could not start {Path.GetFileName(exe)}: {ex.Message}");
        }

        using (process)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            // The timeout also covers reading the output: when "adb devices" starts a new adb server,
            // that server can inherit the output pipe and keep it open long after adb itself exited.
            var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = process.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                string output = (await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false));
                return new ProcessResult(process.ExitCode, output.Trim());
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { /* already gone */ }
                    return new ProcessResult(-1, $"{Path.GetFileName(exe)} timed out.");
                }
                // The tool finished but its output pipe stayed open: use what arrived.
                string partial = (stdout.IsCompletedSuccessfully ? stdout.Result : "") + (stderr.IsCompletedSuccessfully ? stderr.Result : "");
                return new ProcessResult(process.ExitCode, partial.Trim());
            }
        }
    }
}
