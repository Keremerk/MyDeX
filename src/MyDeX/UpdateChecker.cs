using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyDeX;

public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string? AssetName, string? AssetUrl, string? Sha256);

/// <summary>Looks up the newest MyDeX and scrcpy releases on GitHub.</summary>
public static partial class UpdateChecker
{
    public const string MyDexRepo = "Keremerk/MyDeX";
    public const string ScrcpyRepo = "Genymobile/scrcpy";
    const string ScrcpyAssetPrefix = "scrcpy-win64-";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MyDeX");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>"v4.1", "4.1.2", "scrcpy 4.1" → Version with 3 parts, so 4.1 == 4.1.0.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (text == null) return null;
        var m = VersionPattern().Match(text);
        if (!m.Success) return null;
        int Part(int group) => m.Groups[group].Success ? int.Parse(m.Groups[group].Value) : 0;
        return new Version(Part(1), Part(2), Part(3));
    }

    public static bool IsNewer(Version? latest, Version? current) => latest != null && current != null && latest > current;

    /// <summary>
    /// True only for https links on github.com inside <paramref name="repo"/> (e.g. "Genymobile/scrcpy").
    /// Whatever the API returns, MyDeX never opens or downloads anything else.
    /// </summary>
    public static bool IsTrustedGitHubUrl(string? url, string repo) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.Port == 443
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath.StartsWith($"/{repo}/", StringComparison.OrdinalIgnoreCase)
        && !uri.AbsolutePath.Contains("..");

    /// <summary>
    /// Reads a GitHub "latest release" response for <paramref name="repo"/>; picks the first asset whose
    /// name starts with <paramref name="assetPrefix"/>. Links outside that repo on github.com are dropped.
    /// </summary>
    public static ReleaseInfo? ParseRelease(string json, string repo, string? assetPrefix)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.GetString() is not { } tag)
            return null;
        var version = ParseVersion(tag);
        if (version == null) return null;
        string? htmlUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;
        string page = htmlUrl != null && IsTrustedGitHubUrl(htmlUrl, repo) ? htmlUrl : $"https://github.com/{repo}/releases/latest";

        string? assetName = null, assetUrl = null, sha = null;
        if (assetPrefix != null && root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                assetName = name;
                assetUrl = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                if (!IsTrustedGitHubUrl(assetUrl, repo))
                    assetUrl = null;
                if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } dg && dg.StartsWith("sha256:"))
                    sha = dg["sha256:".Length..].ToLowerInvariant();
                break;
            }
        }
        return new ReleaseInfo(version, tag, page, assetName, assetUrl, sha);
    }

    public static async Task<ReleaseInfo?> GetLatestScrcpyAsync()
    {
        var json = await GetPublicAsync($"https://api.github.com/repos/{ScrcpyRepo}/releases/latest").ConfigureAwait(false);
        return json == null ? null : ParseRelease(json, ScrcpyRepo, ScrcpyAssetPrefix);
    }

    /// <summary>Anonymous request only: no account, token or personal data is ever sent.</summary>
    public static async Task<ReleaseInfo?> GetLatestMyDexAsync()
    {
        var json = await GetPublicAsync($"https://api.github.com/repos/{MyDexRepo}/releases/latest").ConfigureAwait(false);
        return json == null ? null : ParseRelease(json, MyDexRepo, null);
    }

    static async Task<string?> GetPublicAsync(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
        }
        catch
        {
            return null;   // offline, rate-limited, ...
        }
    }

    /// <summary>
    /// Downloads the scrcpy release, checks its SHA-256 against GitHub's published digest, and
    /// replaces <paramref name="targetFolder"/> with it. adb must not be running from that folder.
    /// </summary>
    public static async Task UpdateScrcpyAsync(ReleaseInfo release, string targetFolder, Action<string> log)
    {
        if (release.AssetUrl == null || release.AssetName == null || !IsTrustedGitHubUrl(release.AssetUrl, ScrcpyRepo))
            throw new InvalidOperationException("This scrcpy release has no Windows 64-bit download on github.com/Genymobile/scrcpy.");
        if (release.AssetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || release.AssetName.Contains(".."))
            throw new InvalidOperationException("The download has an unexpected file name, so it won't be installed.");
        if (release.Sha256 == null)
            throw new InvalidOperationException("GitHub did not publish a checksum for this download, so it won't be installed automatically.");

        string work = Path.Combine(Path.GetTempPath(), "MyDeX-scrcpy-update");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        string zip = Path.Combine(work, release.AssetName);
        try
        {
            log($"Downloading {release.AssetName}...");
            using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(zip);
                await response.Content.CopyToAsync(file).ConfigureAwait(false);
            }

            string actual;
            await using (var stream = File.OpenRead(zip))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false)).ToLowerInvariant();
            if (actual != release.Sha256)
                throw new InvalidOperationException("The download's checksum doesn't match GitHub's. Nothing was installed.");
            log("Checksum OK.");

            string extract = Path.Combine(work, "extract");
            ZipFile.ExtractToDirectory(zip, extract);
            string source = Directory.GetDirectories(extract).FirstOrDefault(d => File.Exists(Path.Combine(d, "scrcpy.exe")))
                            ?? (File.Exists(Path.Combine(extract, "scrcpy.exe")) ? extract : throw new InvalidOperationException("scrcpy.exe is missing from the download."));

            string backup = targetFolder.TrimEnd('\\') + ".old";
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            Directory.Move(targetFolder, backup);
            try
            {
                Directory.Move(source, targetFolder);
            }
            catch
            {
                Directory.Move(backup, targetFolder);   // put the old version back
                throw;
            }
            log($"scrcpy {release.Tag} installed.");
            // The update already worked; a leftover backup folder is only clutter.
            try { Directory.Delete(backup, true); }
            catch (Exception ex) { log($"Could not remove the old scrcpy copy in {backup}: {ex.Message}"); }
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* temp leftovers are harmless */ }
        }
    }

    [GeneratedRegex(@"(\d+)(?:\.(\d+))?(?:\.(\d+))?")]
    private static partial Regex VersionPattern();
}
