using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LastEpochHelper.Core;

/// <param name="Facts">Lines about the machine and the overlay's state (version, screen size, ...).</param>
/// <param name="PlayerLog">The game's log; only its last lines are taken.</param>
/// <param name="Screenshot">A picture of the game to include, if the player agreed.</param>
/// <param name="Private">Names to blank out wherever they occur (account, Windows user, characters).</param>
/// <param name="Extra">Further files worth having, e.g. the imported build; stored under "build/".</param>
public sealed record BugReportInput(string DataDir, string Description, IReadOnlyList<string> Facts,
    string? PlayerLog, string? Screenshot, IReadOnlyList<string> Private, IReadOnlyList<string>? Extra = null);

/// <summary>
/// Packs what is needed to understand a problem into one zip file: the player's description, the
/// overlay's error and activity logs, what it last read off the screen, its settings and the end of
/// the game's log. Nothing is sent anywhere - the player passes the file on.
/// </summary>
public static partial class BugReport
{
    private const int PlayerLogLines = 400;

    [GeneratedRegex(@"(Connected to chat as ')[^']*(')")]
    private static partial Regex ChatAccount();

    [GeneratedRegex(@"(Character Created: )\S+")]
    private static partial Regex CreatedCharacter();

    /// <returns>Path of the zip file written to <paramref name="outputDir"/>.</returns>
    public static string Create(BugReportInput input, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        string zipPath = Path.Combine(outputDir, $"LastEpochHelper-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var report = new StringBuilder();
        report.AppendLine("Last Epoch Helper - bug report").AppendLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}").AppendLine();
        report.AppendLine("WHAT HAPPENED").AppendLine(input.Description.Trim().Length > 0 ? input.Description.Trim() : "(no description)").AppendLine();
        report.AppendLine("DETAILS");
        foreach (string fact in input.Facts) report.AppendLine(fact);
        AddText(zip, "report.txt", Scrub(report.ToString(), input.Private));

        foreach (string name in new[] { "errors.log", ActivityLog.FileName, ActivityLog.OldFileName })
            AddFile(zip, Path.Combine(input.DataDir, name), name, input.Private);
        if (Directory.Exists(input.DataDir))
            foreach (string file in Directory.GetFiles(input.DataDir, "panel-ocr-*.txt"))
                AddFile(zip, file, "screen/" + Path.GetFileName(file), input.Private);

        foreach (string file in input.Extra ?? Array.Empty<string>())
            AddFile(zip, file, "build/" + Path.GetFileName(file), input.Private);
        AddJson(zip, Path.Combine(input.DataDir, "settings.json"), "settings.json", input.Private, HideSettings);
        AddJson(zip, Path.Combine(input.DataDir, "profiles.json"), "profiles.json", input.Private, HideCharacterNames);

        if (input.PlayerLog is { } log && TailOf(log, PlayerLogLines) is { } tail)
            AddText(zip, "player-log-tail.txt", Scrub(CreatedCharacter().Replace(ChatAccount().Replace(tail, "$1(account)$2"), "$1(character)"), input.Private));
        if (input.Screenshot is { } shot && File.Exists(shot))
            zip.CreateEntryFromFile(shot, "screenshot" + Path.GetExtension(shot));
        return zipPath;
    }

    /// <summary>Blanks out every private name, whatever its casing.</summary>
    public static string Scrub(string text, IEnumerable<string> names)
    {
        foreach (string name in names.Where(n => n.Trim().Length >= 3).OrderByDescending(n => n.Length))
            text = Regex.Replace(text, Regex.Escape(name.Trim()), "(name)", RegexOptions.IgnoreCase);
        return text;
    }

    private static void HideSettings(JsonNode root)
    {
        if (root is not JsonObject settings) return;
        foreach (string key in settings.Select(kv => kv.Key).Where(k => k.Equals("AccountName", StringComparison.OrdinalIgnoreCase)
                     || k.Equals("LogPath", StringComparison.OrdinalIgnoreCase)).ToList())
            settings[key] = "";
    }

    private static void HideCharacterNames(JsonNode root)
    {
        if (root is not JsonObject store) return;
        var profiles = store.FirstOrDefault(kv => kv.Key.Equals("Profiles", StringComparison.OrdinalIgnoreCase)).Value as JsonArray;
        if (profiles is null) return;
        int number = 1;
        foreach (var profile in profiles.OfType<JsonObject>())
        {
            string? key = profile.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("Name", StringComparison.OrdinalIgnoreCase));
            if (key is not null) profile[key] = $"character {number}";
            number++;
        }
    }

    private static void AddJson(ZipArchive zip, string path, string entry, IReadOnlyList<string> names, Action<JsonNode> hide)
    {
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not { } root) return;
            hide(root);
            AddText(zip, entry, Scrub(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), names));
        }
        catch (IOException) { }
        catch (System.Text.Json.JsonException) { }
    }

    private static void AddFile(ZipArchive zip, string path, string entry, IReadOnlyList<string> names)
    {
        try
        {
            if (File.Exists(path)) AddText(zip, entry, Scrub(ReadShared(path), names));
        }
        catch (IOException) { }
    }

    private static void AddText(ZipArchive zip, string entry, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>The game keeps its log open for writing, so it has to be read without asking for exclusive access.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? TailOf(string path, int lines)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // The log can be large; its last stretch is plenty.
            stream.Seek(-Math.Min(stream.Length, 400_000), SeekOrigin.End);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Split('\n');
            return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
        }
        catch (IOException) { return null; }
    }
}
