namespace LastEpochHelper.Core;

/// <summary>One thing that keeps the overlay from reading the game, with a key to remember it was mentioned.</summary>
public sealed record ReadinessProblem(string Key, string Short, string Long);

/// <summary>
/// What has to be true for the overlay to read the game's screen: Windows' text recognition, in
/// English, and the game itself in English. Someone for whom one of these fails sees the build
/// tree and the map counters do nothing - so it is said once, plainly, at start.
/// </summary>
public static class Readiness
{
    private const string AddEnglish = "Windows Settings → Time & language → Language & region → Add a language → English (United States), then restart the overlay. "
                                      + "(Or in PowerShell as administrator: Add-WindowsCapability -Online -Name \"Language.OCR~~~en-US~0.0.1.0\")";
    private const string NeedsReading = "the build tree cannot follow the game's panels, and the map counters cannot be read. The campaign guide works without it";

    public const string StreamedKey = "geforce-now";
    public const string StreamedShort = "Last Epoch is streamed from GeForce NOW: the build tree and map counters work, but the campaign guide cannot follow your zones. "
                                        + "Step through it with Ctrl+Shift+Left/Right.";
    public const string StreamedLong = "Last Epoch is streamed from GeForce NOW, so it runs on Nvidia's machine. The build tree and the map counters read the screen and work. "
                                       + "The game's log stays on that machine, so the campaign guide cannot follow your zones (step through it with Ctrl+Shift+Left/Right), "
                                       + "and a loot filter cannot be written into the game's folder.";

    /// <param name="ocrAvailable">Windows can recognise text at all.</param>
    /// <param name="ocrLanguage">The language it reads in ("en-US"), null when unknown.</param>
    /// <param name="gameLanguage">Last Epoch's language setting ("en"), null when unknown.</param>
    public static List<ReadinessProblem> Problems(bool ocrAvailable, string? ocrLanguage, string? gameLanguage)
    {
        var problems = new List<ReadinessProblem>();
        if (!ocrAvailable)
            problems.Add(new("ocr-missing",
                "Windows' text recognition is not installed, so the build tree cannot follow the game. Settings → Following the game → Check says how to add it.",
                $"Windows' text recognition is not installed, so {NeedsReading}. Add it: {AddEnglish}"));
        else if (ocrLanguage is not null && !ocrLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            problems.Add(new("ocr-" + ocrLanguage,
                $"Windows reads text in {ocrLanguage} only, not English, so the game's panels are read badly. Settings → Following the game → Check says how to add English.",
                $"Windows' text recognition is only installed for {ocrLanguage}, not English, so the game's English text is read badly and {NeedsReading}. Add English: {AddEnglish}"));
        if (gameLanguage is not null && !GameLanguage.IsEnglish(gameLanguage))
        {
            string name = GameLanguage.Name(gameLanguage);
            problems.Add(new("game-" + gameLanguage,
                $"Last Epoch is set to {name}. The overlay reads the game's English text, so the build tree and the map counters need the game in English.",
                $"Last Epoch is set to {name}. The overlay reads the game's text in English, so {NeedsReading.Replace("works without it", "follows you in any language")}. "
                + "Set the game's language to English in its settings to use them."));
        }
        return problems;
    }
}
