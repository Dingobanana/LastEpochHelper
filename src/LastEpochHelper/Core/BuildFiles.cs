using System.IO;

namespace LastEpochHelper.Core;

/// <summary>
/// Puts imported builds on disk: a plan text and a tree file per build, under a file name made from
/// the build's name. Build names are whatever their author typed - any language, any punctuation,
/// any length, and sometimes the same name twice - so the name is made safe and unique here.
/// </summary>
public static class BuildFiles
{
    private const int MaxLength = 100;
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A file name (without extension) Windows accepts, as close to the build's name as it can be.</summary>
    public static string SafeName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = string.Concat((name ?? "").Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c));
        safe = string.Join(' ', safe.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (safe.Length > MaxLength) safe = safe[..MaxLength];
        // Windows drops trailing dots and spaces, which would make two names one.
        safe = safe.Trim().TrimEnd('.', ' ');
        if (safe.Length == 0) safe = "build";
        if (Reserved.Contains(safe)) safe += "_";
        return safe;
    }

    /// <summary>
    /// Writes every build of one import and returns their plan file names, in the same order. Builds
    /// from one guide know each other's files, so switching between them needs no new download.
    /// </summary>
    public static List<string> Write(string buildsDir, IReadOnlyList<MaxrollImporter.Result> results)
    {
        Directory.CreateDirectory(buildsDir);
        var files = new List<string>();
        foreach (var result in results)
        {
            string name = SafeName(result.Name);
            string file = name + ".txt";
            // Two versions whose names come out the same (or differ only in case) must not share a file.
            for (int n = 2; files.Contains(file, StringComparer.OrdinalIgnoreCase); n++) file = $"{name} ({n}).txt";
            files.Add(file);
        }
        for (int i = 0; i < results.Count; i++)
        {
            string path = Path.Combine(buildsDir, files[i]);
            File.WriteAllText(path, results[i].Text);
            results[i].Tree.VariantFiles = results.Count > 1 ? files.ToList() : new List<string>();
            results[i].Tree.Save(BuildTree.PathFor(path));
        }
        return files;
    }
}
