using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public sealed class UpdaterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"leh-update-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private const string Changelog = """
        # Changelog

        Intro text that is not part of any version.

        ## 0.5.0 - Updates from GitHub

        - Checks for updates.
        - Shows what changed.

        ## 0.4.0

        - Follows the open skill.

        ## v0.3.1 – Shorter text
        - Short lines.
        """;

    [Fact]
    public void ParseChangelog_SplitsIntoVersions_WithTitleAndBody()
    {
        var entries = Updater.ParseChangelog(Changelog.Replace("        ", ""));

        Assert.Equal(new[] { "0.5.0", "0.4.0", "0.3.1" }, entries.Select(e => Updater.Display(e.Version)));
        Assert.Equal("Updates from GitHub", entries[0].Title);
        Assert.Equal("- Checks for updates.\n- Shows what changed.", entries[0].Body);
        Assert.Equal("", entries[1].Title);
        Assert.Equal("Shorter text", entries[2].Title);
    }

    [Fact]
    public void ChangesSince_ListsEverythingNewerThanTheLastRunVersion_UpToTheCurrentOne()
    {
        var entries = Updater.ParseChangelog(Changelog.Replace("        ", ""));

        var changes = Updater.ChangesSince(entries, lastSeen: new Version(0, 3, 1), current: new Version(0, 4, 0));
        Assert.Equal("0.4.0", Updater.Display(Assert.Single(changes).Version));

        Assert.Equal(3, Updater.ChangesSince(entries, new Version(0, 1, 0), new Version(0, 5, 0)).Count);
        Assert.Empty(Updater.ChangesSince(entries, new Version(0, 5, 0), new Version(0, 5, 0)));
    }

    [Theory]
    [InlineData("v0.5.0", true)]
    [InlineData("1.2", true)]
    [InlineData("latest", false)]
    [InlineData("", false)]
    public void TryParseVersion_AcceptsTags(string text, bool valid) => Assert.Equal(valid, Updater.TryParseVersion(text, out _));

    private static string Release(string tag, string url, bool prerelease = false) => $$"""
        { "tag_name": "{{tag}}", "draft": false, "prerelease": {{(prerelease ? "true" : "false")}}, "body": "- notes",
          "assets": [ { "name": "x.txt", "browser_download_url": "https://github.com/Dingobanana/LastEpochHelper/releases/download/{{tag}}/x.txt" },
                      { "name": "app.zip", "browser_download_url": "{{url}}" } ] }
        """;

    [Fact]
    public void ParseRelease_TakesTheZipOfThisRepository()
    {
        var release = Updater.ParseRelease(Release("v9.1.0", "https://github.com/Dingobanana/LastEpochHelper/releases/download/v9.1.0/LastEpochHelper-9.1.0.zip"));

        Assert.Equal(new Version(9, 1, 0), release!.Version);
        Assert.EndsWith("LastEpochHelper-9.1.0.zip", release.DownloadUrl);
        Assert.Equal("- notes", release.Notes);
    }

    [Theory]
    [InlineData("https://evil.example/LastEpochHelper.zip")]
    [InlineData("https://github.com/someone-else/LastEpochHelper/releases/download/v9/app.zip")]
    [InlineData("https://github.com/Dingobanana/LastEpochHelper/releases/download/v9/app.exe")]
    public void ParseRelease_RejectsDownloadsFromAnywhereElse(string url) => Assert.Null(Updater.ParseRelease(Release("v9.0.0", url)));

    [Fact]
    public void ParseRelease_IgnoresPrereleases_AndGarbage()
    {
        Assert.Null(Updater.ParseRelease(Release("v9.0.0", "https://github.com/Dingobanana/LastEpochHelper/releases/download/v9.0.0/app.zip", prerelease: true)));
        Assert.Null(Updater.ParseRelease("not json"));
        Assert.Null(Updater.ParseRelease("{}"));
    }

    private sealed class FixedResponse(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    [Fact]
    public async Task Install_ReplacesFiles_KeepsOthers_AndCleanUpRemovesTheLeftovers()
    {
        string install = Path.Combine(_dir, "install");
        Directory.CreateDirectory(Path.Combine(install, "Data"));
        File.WriteAllText(Path.Combine(install, "LastEpochHelper.exe"), "old exe");
        File.WriteAllText(Path.Combine(install, "Data", "guide.json"), "old guide");
        File.WriteAllText(Path.Combine(install, "notes.txt"), "mine");

        // A release zip as the release script builds it: one top-level folder.
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in new[] { ("LastEpochHelper/LastEpochHelper.exe", "new exe"), ("LastEpochHelper/Data/guide.json", "new guide"), ("LastEpochHelper/Data/CHANGELOG.md", "log") })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }
        using var http = new HttpClient(new FixedResponse(buffer.ToArray()));
        var release = new ReleaseInfo(new Version(9, 0, 0), "https://github.com/Dingobanana/LastEpochHelper/releases/download/v9.0.0/app.zip", "");

        string executable = await Updater.InstallAsync(release, install, http);

        Assert.Equal(Path.Combine(install, "LastEpochHelper.exe"), executable);
        Assert.Equal("new exe", File.ReadAllText(executable));
        Assert.Equal("new guide", File.ReadAllText(Path.Combine(install, "Data", "guide.json")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(install, "Data", "CHANGELOG.md")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(install, "notes.txt")));
        Assert.Equal("old exe", File.ReadAllText(executable + ".old")); // the running copy, moved aside

        Updater.CleanUp(install);
        Assert.Empty(Directory.GetFiles(install, "*.old", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Install_RefusesAZipWithoutTheProgram()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open()))
            writer.Write("nothing here");
        using var http = new HttpClient(new FixedResponse(buffer.ToArray()));
        string install = Path.Combine(_dir, "install");
        Directory.CreateDirectory(install);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Updater.InstallAsync(new ReleaseInfo(new Version(9, 0, 0), "https://github.com/Dingobanana/LastEpochHelper/releases/download/v9/a.zip", ""), install, http));
        Assert.Empty(Directory.GetFiles(install));
    }

    [Fact]
    public void BundledChangelog_HasAnEntryForTheRunningVersion()
    {
        var entries = Updater.ParseChangelog(Updater.LoadBundledChangelog());

        Assert.Contains(entries, e => e.Version == Updater.Current);
    }
}
