namespace MyDeX;

static class ToolLocator
{
    /// <summary>
    /// Finds a folder containing scrcpy.exe and adb.exe: the one chosen in Settings, else the copy bundled
    /// with MyDeX. Deliberately not PATH or the current folder, so a planted scrcpy/adb elsewhere is never run.
    /// </summary>
    public static string? FindScrcpyFolder(string? configuredFolder)
    {
        string?[] candidates =
        [
            configuredFolder,
            Path.Combine(AppContext.BaseDirectory, "tools", "scrcpy"),
        ];
        return candidates
            .Where(dir => !string.IsNullOrWhiteSpace(dir) && Path.IsPathFullyQualified(dir))
            .FirstOrDefault(dir => File.Exists(Path.Combine(dir!, "scrcpy.exe")) && File.Exists(Path.Combine(dir!, "adb.exe")));
    }
}
