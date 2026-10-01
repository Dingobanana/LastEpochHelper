using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LastEpochHelper.Core;

/// <summary>One line in a step. <see cref="Type"/> is one of: main, side, go, boss, res, tip, skip.</summary>
public sealed class GuideTask
{
    public string Type { get; set; } = "main";
    public string Text { get; set; } = "";
    /// <summary>Name of the quest this line belongs to, when it is a quest objective.</summary>
    public string? Quest { get; set; }
    /// <summary>Passive points granted when this task is completed.</summary>
    public int Passive { get; set; }
    /// <summary>Idol slots granted when this task is completed.</summary>
    public int Idol { get; set; }

    [JsonIgnore] public bool HasReward => Passive > 0 || Idol > 0;
    [JsonIgnore] public bool IsMain => Type.Equals("main", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One visit to a zone. The same zone may appear in several steps (e.g. returning to a town).</summary>
public sealed class GuideStep
{
    public string Zone { get; set; } = "";
    /// <summary>
    /// Key used for scene mapping. Defaults to <see cref="Zone"/>; set it when two different
    /// scenes share a display name (e.g. "The Burning Forest" in chapter 1 and chapter 7).
    /// </summary>
    public string? SceneKey { get; set; }
    public bool Waypoint { get; set; }
    /// <summary>Area level of the zone (0 = scales / unknown).</summary>
    public int Level { get; set; }
    public List<GuideTask> Tasks { get; set; } = new();

    [JsonIgnore] public string Key => SceneKey ?? Zone;
}

public sealed class GuideChapter
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Era { get; set; } = "";
    public List<GuideStep> Steps { get; set; } = new();
}

/// <summary>One way through the game: the full campaign, or a variant that skips parts via dungeons.</summary>
public sealed class GuideRoute
{
    public string Id { get; set; } = "full";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<GuideChapter> Chapters { get; set; } = new();

    /// <summary>All steps in play order, with the chapter each belongs to.</summary>
    [JsonIgnore] public IReadOnlyList<(GuideChapter Chapter, GuideStep Step, int IndexInChapter)> Flat => _flat ??= Flatten();
    private List<(GuideChapter, GuideStep, int)>? _flat;

    private List<(GuideChapter, GuideStep, int)> Flatten()
    {
        var list = new List<(GuideChapter, GuideStep, int)>();
        foreach (var c in Chapters)
            for (int i = 0; i < c.Steps.Count; i++)
                list.Add((c, c.Steps[i], i));
        return list;
    }

    /// <summary>Index of the first step of the chapter at the given position in <see cref="Chapters"/>.</summary>
    public int FirstStepOf(int chapterPosition) => Chapters.Take(chapterPosition).Sum(c => c.Steps.Count);
}

public sealed class Guide
{
    public string GameVersion { get; set; } = "";
    /// <summary>Max passive points / idol slots obtainable from quests.</summary>
    public int PassiveCap { get; set; }
    public int IdolCap { get; set; }
    public List<GuideRoute> Routes { get; set; } = new();

    public GuideRoute Route(string? id) =>
        Routes.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Routes[0];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public static Guide Load(string path) => Parse(File.ReadAllText(path));

    public static Guide Parse(string json)
    {
        var guide = JsonSerializer.Deserialize<Guide>(json, JsonOptions) ?? throw new InvalidDataException("Guide is empty.");
        if (guide.Routes.Count == 0 || guide.Routes.Any(r => r.Flat.Count == 0))
            throw new InvalidDataException("Guide contains a route without steps.");
        return guide;
    }
}
