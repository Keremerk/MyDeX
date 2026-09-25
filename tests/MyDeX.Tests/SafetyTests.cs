namespace MyDeX.Tests;

/// <summary>User safety: only trusted links, nothing personal in the shareable log, privacy options work.</summary>
public class SafetyTests
{
    [Theory]
    [InlineData("https://github.com/Genymobile/scrcpy/releases/download/v4.1/scrcpy-win64-v4.1.zip", true)]
    [InlineData("https://github.com/Genymobile/scrcpy/releases/tag/v4.1", true)]
    [InlineData("https://GitHub.com/genymobile/scrcpy/releases/latest", true)]
    [InlineData("http://github.com/Genymobile/scrcpy/releases/latest", false)]              // not https
    [InlineData("https://github.com.evil.example/Genymobile/scrcpy/x.zip", false)]          // look-alike host
    [InlineData("https://evil.example/github.com/Genymobile/scrcpy/x.zip", false)]
    [InlineData("https://github.com/SomeoneElse/scrcpy/releases/download/v4.1/x.zip", false)] // other repo
    [InlineData("https://github.com/Genymobile/scrcpy-fake/releases/x.zip", false)]         // prefix trick
    [InlineData("https://user:pass@github.com/Genymobile/scrcpy/x.zip", false)]
    [InlineData("https://github.com:8443/Genymobile/scrcpy/x.zip", false)]
    [InlineData("https://github.com/Genymobile/scrcpy/../../evil/x.zip", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyGitHubLinksOfTheExactRepoAreTrusted(string? url, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsTrustedGitHubUrl(url, "Genymobile/scrcpy"));

    [Fact]
    public void ParseRelease_DropsDownloadAndPageLinksOutsideTheRepo()
    {
        const string json = """
            { "tag_name": "v9.9", "html_url": "https://evil.example/phish",
              "assets": [ { "name": "scrcpy-win64-v9.9.zip", "browser_download_url": "https://evil.example/malware.zip",
                            "digest": "sha256:abcd" } ] }
            """;

        var release = UpdateChecker.ParseRelease(json, "Genymobile/scrcpy", "scrcpy-win64-")!;

        Assert.Null(release.AssetUrl);                                               // never downloaded
        Assert.Equal("https://github.com/Genymobile/scrcpy/releases/latest", release.PageUrl);  // never opened
    }

    [Fact]
    public async Task UpdateScrcpy_RefusesUntrustedDownloads()
    {
        var evil = new ReleaseInfo(new Version(9, 9, 0), "v9.9", "https://github.com/Genymobile/scrcpy/releases/latest",
            "scrcpy-win64-v9.9.zip", "https://evil.example/malware.zip", new string('a', 64));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateChecker.UpdateScrcpyAsync(evil, Path.Combine(Path.GetTempPath(), "mydex-should-not-exist"), _ => { }));
        Assert.Contains("github.com", ex.Message);
    }

    [Fact]
    public async Task UpdateScrcpy_RefusesDownloadsWithoutChecksum()
    {
        var noSum = new ReleaseInfo(new Version(9, 9, 0), "v9.9", "https://github.com/Genymobile/scrcpy/releases/latest",
            "scrcpy-win64-v9.9.zip", "https://github.com/Genymobile/scrcpy/releases/download/v9.9/scrcpy-win64-v9.9.zip", null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateChecker.UpdateScrcpyAsync(noSum, Path.Combine(Path.GetTempPath(), "mydex-should-not-exist"), _ => { }));
        Assert.Contains("checksum", ex.Message);
    }

    [Fact]
    public void LogFile_MasksSerialsAddressesAndUserName()
    {
        FileLog.AddSecret("RZ8N123ABCD");
        FileLog.AddSecret("192.168.1.23:5555");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string line = FileLog.Redact(
            $"INFO:  -->   (usb)  RZ8N123ABCD  device  SM_S928B | connected to 192.168.1.23:5555 | pushed {profile}\\Videos\\clip.mp4 | 10.0.0.7");

        Assert.DoesNotContain("RZ8N123ABCD", line);
        Assert.Contains("RZ8********", line);
        Assert.DoesNotContain("192.168.1.23", line);
        Assert.DoesNotContain("10.0.0.7", line);
        Assert.DoesNotContain(profile, line);
        Assert.Contains("%USERPROFILE%\\Videos\\clip.mp4", line);
        Assert.Contains("SM_S928B", line);   // the phone model stays – it's useful and not personal
    }

    [Theory]
    // Wireless-debugging names carry the phone's serial, even if it was never seen over USB.
    [InlineData("[guid=adb-RZ9Q777XYZW-AbCdEf] connected to adb-RZ9Q777XYZW-AbCdEf._adb-tls-connect._tcp", "RZ9Q777XYZW")]
    [InlineData("paired with fe80::1c2b:3aff:fe4d:5e6f%wlan0 and 2001:db8:85a3::8a2e:370:7334", "fe80::1c2b")]
    [InlineData("pushed C:\\Users\\JOHNDO~1\\AppData\\Local\\Temp\\x.txt", "JOHNDO~1")]
    [InlineData("pushed D:\\Users\\SomeoneElse\\Music\\song.mp3", "SomeoneElse")]
    public void LogFile_MasksOtherPersonalDetails(string line, string mustDisappear) =>
        Assert.DoesNotContain(mustDisappear, FileLog.Redact(line));

    [Fact]
    public void LogFile_KeepsUsefulNonPersonalText()
    {
        string line = FileLog.Redact("[15:03:20] DeX window closed (scrcpy exit code 2). _adb-tls-connect._tcp --display-id=12");
        Assert.Equal("[15:03:20] DeX window closed (scrcpy exit code 2). _adb-tls-connect._tcp --display-id=12", line);
    }

    [Fact]
    public void LogFile_MasksTheWindowsUserName()
    {
        string user = Environment.UserName;
        if (user.Length < 3) return;
        Assert.DoesNotContain(user, FileLog.Redact($"started by {user} on this PC"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClipboardSharing_CanBeTurnedOff()
    {
        var phone = new PhoneDevice("RFTEST00001", "device", "SM S928B");
        var settings = AppSettings.FromJson(null);
        Assert.True(settings.ShareClipboard);
        Assert.DoesNotContain("--no-clipboard-autosync", DexSession.BuildArgs(settings, phone, null, null));

        settings.ShareClipboard = false;
        Assert.Contains("--no-clipboard-autosync", DexSession.BuildArgs(settings, phone, null, null));
        Assert.Contains("--no-clipboard-autosync", AppWindow.BuildArgs(settings, phone, AppWindow.PhoneScreen, null, false));
    }

    [Fact]
    public void NoPersonalDataInTheRepository()
    {
        // Walk up from the test output to the repo root and scan every tracked text file.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MyDeX.sln"))) dir = dir.Parent;
        if (dir == null) return;   // not run from a checkout (e.g. copied test binaries)

        // Generic leak patterns: battery serial/usage fields from dumpsys, and GitHub tokens.
        string[] forbidden = ["QrData", "FirstUseDate", "ACTION_POWER_CONNECTED", "ghp_", "github_pat_", "BEGIN RSA PRIVATE KEY", "adbkey"];
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir.FullName, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dir.FullName, file);
            if (rel.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj" or ".git" or "dist" or "tools" or ".vs")) continue;
            if (!new[] { ".cs", ".md", ".txt", ".iss", ".ps1", ".csproj", ".sln", ".json", ".gitignore" }.Contains(Path.GetExtension(file)) && Path.GetFileName(file) != "LICENSE") continue;
            string text = File.ReadAllText(file);
            if (rel.EndsWith("SafetyTests.cs")) continue;   // this file lists the forbidden words
            hits.AddRange(forbidden.Where(f => text.Contains(f, StringComparison.OrdinalIgnoreCase)).Select(f => $"{rel}: {f}"));
        }
        Assert.Empty(hits);
    }
}
