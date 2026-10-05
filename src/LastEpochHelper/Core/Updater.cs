using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LastEpochHelper.Core;

public sealed record ChangelogEntry(Version Version, string Title, string Body);

public sealed record ReleaseInfo(Version Version, string DownloadUrl, string Notes);

/// <summary>
/// Finds, downloads and installs new versions from the project's GitHub releases. Only a zip
/// attached to a release of <see cref="Repository"/> is ever downloaded, and nothing is installed
/// without the user asking for it.
/// </summary>
public static partial class Updater
{
    public const string Repository = "Dingobanana/LastEpochHelper";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    private const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";
    private const string ExeName = "LastEpochHelper.exe";
    private const string OldSuffix = ".old";

    public static Version Current { get; } = Normalise(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static string Display(Version version) => $"{version.Major}.{version.Minor}.{version.Build}";

    private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!Version.TryParse(text.Trim().TrimStart('v', 'V'), out var parsed) || parsed.Minor < 0) return false;
        version = Normalise(parsed);
        return true;
    }

    // ------------------------------------------------------------------ changelog

    [GeneratedRegex(@"^##\s+v?(\d+\.\d+(?:\.\d+)?)\s*(?:[-–—]\s*(.*))?$")]
    private static partial Regex VersionHeading();

    /// <summary>Parses a CHANGELOG.md whose sections start with "## 1.2.3 - optional title".</summary>
    public static List<ChangelogEntry> ParseChangelog(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        Version? version = null;
        string title = "";
        var body = new List<string>();
        void Flush()
        {
            if (version is not null) entries.Add(new ChangelogEntry(version, title, string.Join("\n", body).Trim()));
            body.Clear();
        }
        foreach (string raw in markdown.Replace("\r", "").Split('\n'))
        {
            var m = VersionHeading().Match(raw);
            if (m.Success && TryParseVersion(m.Groups[1].Value, out var parsed))
            {
                Flush();
                version = parsed;
                title = m.Groups[2].Value.Trim();
            }
            else if (version is not null) body.Add(raw);
        }
        Flush();
        return entries;
    }

    /// <summary>Everything newer than <paramref name="lastSeen"/> up to the running version, newest first.</summary>
    public static List<ChangelogEntry> ChangesSince(IEnumerable<ChangelogEntry> entries, Version lastSeen, Version current) =>
        entries.Where(e => e.Version > lastSeen && e.Version <= current).OrderByDescending(e => e.Version).ToList();

    public static string LoadBundledChangelog() => Bundled.Text(Bundled.Changelog);

    // ------------------------------------------------------------------ checking

    /// <summary>Reads a GitHub "release" JSON document. Null if it has no usable zip from this repository.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        JsonNode? release;
        try { release = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { return null; }
        if (release is null || !TryParseVersion(release["tag_name"]?.GetValue<string>(), out var version)) return null;
        if (release["draft"]?.GetValue<bool>() == true || release["prerelease"]?.GetValue<bool>() == true) return null;

        foreach (var asset in release["assets"] as JsonArray ?? new JsonArray())
        {
            string? url = asset?["browser_download_url"]?.GetValue<string>();
            // Never follow a link that does not point at this repository's own release files.
            if (url is null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;
            if (!url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            return new ReleaseInfo(version, url, release["body"]?.GetValue<string>() ?? "");
        }
        return null;
    }

    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"LastEpochHelper/{Display(Current)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>The latest release if it is newer than the running version; null when up to date.</summary>
    /// <exception cref="HttpRequestException">GitHub could not be reached.</exception>
    public static async Task<ReleaseInfo?> CheckAsync(HttpClient http)
    {
        var release = ParseRelease(await http.GetStringAsync(LatestReleaseApi));
        return release is not null && release.Version > Current ? release : null;
    }

    // ------------------------------------------------------------------ installing

    /// <summary>
    /// Downloads the release and swaps it in under <paramref name="installDir"/>. A running program
    /// cannot be overwritten on Windows but it can be renamed, so current files are moved aside as
    /// "*.old" and removed by <see cref="CleanUp"/> the next time the app starts.
    /// </summary>
    /// <returns>Path of the new executable to start.</returns>
    public static async Task<string> InstallAsync(ReleaseInfo release, string installDir, HttpClient http)
    {
        string work = Path.Combine(Path.GetTempPath(), "LastEpochHelper-update");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);

        string zip = Path.Combine(work, "update.zip");
        await File.WriteAllBytesAsync(zip, await http.GetByteArrayAsync(release.DownloadUrl));
        string extracted = Path.Combine(work, "files");
        ZipFile.ExtractToDirectory(zip, extracted); // refuses entries that would land outside the folder

        // The zip holds either the files directly or a single top-level folder with them.
        string? exe = Directory.GetFiles(extracted, ExeName, SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault();
        if (exe is null) throw new InvalidDataException($"The release does not contain {ExeName}.");
        string source = Path.GetDirectoryName(exe)!;

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(installDir, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                string old = target + OldSuffix;
                File.Delete(old);
                File.Move(target, old);
            }
            File.Copy(file, target);
        }
        return Path.Combine(installDir, ExeName);
    }

    /// <summary>Removes what an earlier update moved aside. Safe to call on every start.</summary>
    public static void CleanUp(string installDir)
    {
        try
        {
            foreach (string old in Directory.GetFiles(installDir, "*" + OldSuffix, SearchOption.AllDirectories))
            {
                try { File.Delete(old); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
