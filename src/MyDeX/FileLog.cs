using System.Text.RegularExpressions;

namespace MyDeX;

/// <summary>
/// Copy of the log in %AppData%\MyDeX\mydex.log, for troubleshooting after the window is closed.
/// People share this file when asking for help, so it is written with personal details masked:
/// phone serial numbers, IP addresses and the Windows user name.
/// Starts over once it passes 1 MB (the previous one is kept as mydex.old.log).
/// </summary>
static partial class FileLog
{
    const long MaxBytes = 1024 * 1024;
    static readonly object Gate = new();
    static readonly HashSet<string> Secrets = new(StringComparer.OrdinalIgnoreCase);

    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyDeX", "mydex.log");

    static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Registers a value (e.g. a phone serial) that must never appear in the log file.</summary>
    public static void AddSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4) return;
        lock (Gate) Secrets.Add(value);
    }

    /// <summary>
    /// Masks personal details: known serials ("RZ8N123ABCD" → "RZ8********"), wireless-debugging names
    /// (adb-SERIAL-xxxx → adb-RZ8***-***), IPv4/IPv6 addresses, any C:\Users\&lt;name&gt; path (also 8.3 short
    /// names like JOHNDO~1) and the Windows user name.
    /// </summary>
    public static string Redact(string line)
    {
        string[] secrets;
        lock (Gate) secrets = [.. Secrets.OrderByDescending(s => s.Length)];
        foreach (var secret in secrets)
            line = line.Replace(secret, Mask(secret), StringComparison.OrdinalIgnoreCase);
        line = MdnsName().Replace(line, m => $"adb-{m.Groups[1].Value}***-***");
        line = IPv4().Replace(line, m => $"{m.Groups[1].Value}.*.*.*");
        line = IPv6().Replace(line, m => LooksLikeIPv6(m.Value) ? "*:*:*" : m.Value);
        line = UserFolder().Replace(line, "%USERPROFILE%");
        if (UserProfile.Length > 3)
            line = line.Replace(UserProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (user.Length >= 3)
            line = Regex.Replace(line, $@"(?<![\w]){Regex.Escape(user)}(?![\w])", "%USERNAME%", RegexOptions.IgnoreCase);
        return line;
    }

    /// <summary>Tells real IPv6 addresses apart from times like 15:03:20 (which are only digits, no "::").</summary>
    static bool LooksLikeIPv6(string text) =>
        text.Contains("::") || text.Count(c => c == ':') >= 4 || text.Any(c => c is >= 'a' and <= 'f' or >= 'A' and <= 'F');

    static string Mask(string secret) => secret[..3] + new string('*', Math.Min(secret.Length - 3, 12));

    public static void Write(string line)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd} {Redact(line)}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never break the app.
            }
        }
    }

    [GeneratedRegex(@"\b(\d{1,3})\.\d{1,3}\.\d{1,3}\.\d{1,3}\b")]
    private static partial Regex IPv4();

    // At least two colons between hex groups; LooksLikeIPv6 filters out plain times.
    [GeneratedRegex(@"(?<![\w:])(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?:%\w+)?(?![\w:])")]
    private static partial Regex IPv6();

    // Wireless debugging devices are named adb-<serial>-<random>, sometimes as guid=adb-...
    [GeneratedRegex(@"adb-(?!tls-)([A-Za-z0-9]{1,3})[A-Za-z0-9]*-[A-Za-z0-9]+")]
    private static partial Regex MdnsName();

    [GeneratedRegex(@"(?i)\b[A-Z]:\\Users\\[^\\\s""'<>|:]+")]
    private static partial Regex UserFolder();
}
