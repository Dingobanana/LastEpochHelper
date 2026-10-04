using Microsoft.Win32;

namespace LastEpochHelper.Core;

/// <summary>
/// The language Last Epoch is set to. Everything the overlay reads off the screen - panel headings,
/// tooltips, the map's counters - is matched against the English game, so another language makes
/// those parts go quiet. The game keeps its choice among its saved preferences in the registry,
/// as "selected-locale_h&lt;number&gt;" holding a code like "en".
/// </summary>
public static class GameLanguage
{
    private const string Key = @"Software\Eleventh Hour Games\Last Epoch";

    /// <summary>"en", "de", ... or null when the game has not saved a choice (or is not installed).</summary>
    public static string? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            if (key is null) return null;
            foreach (string name in key.GetValueNames())
                if (name.StartsWith("selected-locale", StringComparison.OrdinalIgnoreCase))
                    return Parse(key.GetValue(name));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { }
        return null;
    }

    /// <summary>The stored value: bytes of the code ending in a zero, or the code as text.</summary>
    public static string? Parse(object? value)
    {
        string text = value switch
        {
            byte[] bytes => new string(bytes.TakeWhile(b => b != 0).Select(b => (char)b).ToArray()),
            string s => s,
            _ => "",
        };
        text = text.Trim();
        return text.Length is >= 2 and <= 12 && text.All(c => char.IsAsciiLetter(c) || c is '-' or '_') ? text : null;
    }

    public static bool IsEnglish(string? code) => code is not null && code.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>"German" for "de", or the code itself when Windows does not know it.</summary>
    public static string Name(string code)
    {
        if (code.Equals("jp", StringComparison.OrdinalIgnoreCase)) return "Japanese"; // the game's own code for it
        try { return new System.Globalization.CultureInfo(code).EnglishName; }
        catch (System.Globalization.CultureNotFoundException) { return code; }
    }
}
