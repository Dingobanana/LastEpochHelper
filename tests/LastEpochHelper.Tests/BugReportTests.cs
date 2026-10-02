using System.IO;
using System.IO.Compression;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public sealed class BugReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "leh-report-" + Guid.NewGuid().ToString("N"));

    public BugReportTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Read(ZipArchive zip, string entry)
    {
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void PacksDescriptionLogsAndSettings_WithoutPrivateNames()
    {
        string data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "errors.log"), @"System.IO.IOException at C:\Users\SomeUser\AppData\thing");
        File.WriteAllText(Path.Combine(data, "panel-ocr-skills.txt"), "21 @ 10,10  SMITE\n12 @ 60,250  Herotest 4 AOC");
        File.WriteAllText(Path.Combine(data, "settings.json"), """{"AccountName":"FakeAccount","LogPath":"D:\\x\\Player.log","Opacity":0.9}""");
        File.WriteAllText(Path.Combine(data, "profiles.json"), """{"ActiveId":"a","Profiles":[{"Id":"a","Name":"Herotest","Level":12}]}""");
        string log = Path.Combine(_dir, "Player.log");
        File.WriteAllLines(log, Enumerable.Range(0, 1000).Select(i => $"line {i}")
            .Append("Connected to chat as 'FakeAccount'").Append("Character Created: Herotest 123 class: 0"));

        ActivityLog.Start(data);
        ActivityLog.Change("panel", "game shows Skills");
        ActivityLog.Change("panel", "game shows Skills"); // unchanged: not written twice
        ActivityLog.Change("panel", "game shows None");

        string zipPath = BugReport.Create(new BugReportInput(data, "The tree closed when I opened Smite.", new[] { "Version: 9.9.9" },
            log, null, new[] { "Herotest", "FakeAccount", "SomeUser", "ab" }), Path.Combine(_dir, "out"));

        using var zip = ZipFile.OpenRead(zipPath);
        string report = Read(zip, "report.txt");
        Assert.Contains("The tree closed when I opened Smite.", report);
        Assert.Contains("Version: 9.9.9", report);

        string activity = Read(zip, ActivityLog.FileName);
        Assert.Single(activity.Split('\n').Where(l => l.Contains("game shows Skills")));
        Assert.Contains("game shows None", activity);

        Assert.Contains("Opacity", Read(zip, "settings.json"));
        Assert.Contains("character 1", Read(zip, "profiles.json"));
        string tail = Read(zip, "player-log-tail.txt");
        Assert.Contains("line 999", tail);
        Assert.DoesNotContain("line 100\n", tail.Replace("\r", "")); // only the end of the game's log

        foreach (var entry in zip.Entries)
        {
            string text = Read(zip, entry.FullName);
            Assert.DoesNotContain("Herotest", text);
            Assert.DoesNotContain("FakeAccount", text);
            Assert.DoesNotContain("SomeUser", text);
            Assert.DoesNotContain("Player.log", text);
        }
        Assert.Contains("SMITE", Read(zip, "screen/panel-ocr-skills.txt"));
    }

    [Fact]
    public void MissingFiles_AreSimplyLeftOut()
    {
        string zipPath = BugReport.Create(new BugReportInput(Path.Combine(_dir, "nothing-here"), "", Array.Empty<string>(),
            Path.Combine(_dir, "no.log"), Path.Combine(_dir, "no.png"), Array.Empty<string>()), Path.Combine(_dir, "out"));
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal("report.txt", Assert.Single(zip.Entries).FullName);
        Assert.Contains("(no description)", Read(zip, "report.txt"));
    }
}
