using System.IO;

namespace LastEpochHelper.Core;

/// <summary>
/// The files in Data\ and the changelog, built into the program. Players move LastEpochHelper.exe out of
/// its folder (to the desktop, say), so nothing it needs to start may live in a file beside it.
/// </summary>
public static class Bundled
{
    public const string Guide = "guide.json";
    public const string Scenes = "scenes.json";
    public const string Endgame = "endgame.json";
    public const string Changelog = "CHANGELOG.md";

    /// <summary>The file's text; empty if the build left it out.</summary>
    public static string Text(string name)
    {
        using Stream? stream = typeof(Bundled).Assembly.GetManifestResourceStream("Data/" + name);
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
