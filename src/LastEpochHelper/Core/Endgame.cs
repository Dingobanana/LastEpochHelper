using System.IO;
using System.Text.Json;

namespace LastEpochHelper.Core;

public sealed class Blessing
{
    public string Name { get; set; } = "";
    public string Effect { get; set; } = "";
    /// <summary>The empowered ("Grand") roll range of the same stat.</summary>
    public string Grand { get; set; } = "";
    public bool Recommended { get; set; }
}

public sealed class Timeline
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string Boss { get; set; } = "";
    /// <summary>Knowledge of Orobyss awarded for the first completion.</summary>
    public int Knowledge { get; set; }
    public string Echoes { get; set; } = "";
    public string Harbinger { get; set; } = "";
    public string Rewards { get; set; } = "";
    public List<Blessing> Blessings { get; set; } = new();
}

public sealed class Dungeon
{
    public string Name { get; set; } = "";
    public string Entrance { get; set; } = "";
    public int Level { get; set; }
    public string Boss { get; set; } = "";
    public string Mechanic { get; set; } = "";
    public string Reward { get; set; } = "";
    public string FirstClear { get; set; } = "";
    public string Skip { get; set; } = "";
    public string Keys { get; set; } = "";
}

/// <summary>Static facts about the Monolith timelines and the dungeons (Data/endgame.json).</summary>
public sealed class EndgameData
{
    public int KnowledgeNeeded { get; set; } = 5;
    public List<Timeline> Timelines { get; set; } = new();
    public List<Dungeon> Dungeons { get; set; } = new();

    public static EndgameData Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<EndgameData>(File.ReadAllText(path), Guide.JsonOptions) ?? new EndgameData();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new EndgameData();
    }

    /// <summary>Which timeline a blessing belongs to; planner builds name the empowered version ("Grand ...").</summary>
    public Timeline? TimelineOf(string blessing)
    {
        string name = blessing.StartsWith("Grand ", StringComparison.OrdinalIgnoreCase) ? blessing[6..] : blessing;
        return Timelines.FirstOrDefault(t => t.Blessings.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>What a character has done in one timeline. Kept by hand: the game log does not report it.</summary>
public sealed class TimelineProgress
{
    public bool Normal { get; set; }
    public bool Empowered { get; set; }
    public string Blessing { get; set; } = "";
    public int Corruption { get; set; }
}

public sealed class DungeonProgress
{
    public int Keys { get; set; }
    public bool FirstClear { get; set; }
}
