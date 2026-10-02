using System.IO;
using System.Text.Json;

namespace LastEpochHelper.Core;

public sealed class Settings
{
    public double Left { get; set; } = 40;
    public double Top { get; set; } = 200;
    public double Width { get; set; } = 380;
    public double Opacity { get; set; } = 0.88;
    public double FontSize { get; set; } = 14;
    public bool Locked { get; set; }
    /// <summary>Look for a new version on GitHub at start and a few times a day. Nothing installs by itself.</summary>
    public bool AutoCheckUpdates { get; set; } = true;
    /// <summary>Version that last ran here; a difference triggers the "What's new" window.</summary>
    public string LastRunVersion { get; set; } = "";
    /// <summary>Only the zone title and counters; for boss fights and small screens.</summary>
    public bool Compact { get; set; }
    /// <summary>One line across the screen instead of the box.</summary>
    public bool BarLayout { get; set; }
    public double BarWidth { get; set; } = 1100;
    public double? PlannerLeft { get; set; }
    public double? PlannerTop { get; set; }
    public string HotkeyPlanner { get; set; } = "Ctrl+Shift+G";
    public string HotkeyLookup { get; set; } = "Ctrl+Shift+E";
    /// <summary>Last Epoch account name, for reading the character's public profile. Filled in from the game log.</summary>
    public string AccountName { get; set; } = "";
    /// <summary>Hide while another application has focus.</summary>
    public bool AutoHide { get; set; } = true;
    public bool ShowTimer { get; set; } = true;
    public bool ShowBuild { get; set; } = true;
    /// <summary>0 = off, 1 = inside the overlay, 2 = large and centred.</summary>
    public int MapMode { get; set; } = 1;
    public bool AutoLearn { get; set; } = true;
    public bool AutoTick { get; set; } = true;
    public int Lookahead { get; set; } = 4;
    public int Lookbehind { get; set; } = 4;
    /// <summary>Override for the Player.log location; empty = default LocalLow path.</summary>
    public string LogPath { get; set; } = "";
    public string HotkeyNext { get; set; } = "Ctrl+Shift+Right";
    public string HotkeyPrev { get; set; } = "Ctrl+Shift+Left";
    public string HotkeyToggle { get; set; } = "Ctrl+Shift+H";
    public string HotkeyLock { get; set; } = "Ctrl+Shift+L";
    public string HotkeyCompact { get; set; } = "Ctrl+Shift+C";
    public string HotkeyMap { get; set; } = "Ctrl+Shift+M";
    public string HotkeyCapture { get; set; } = "Ctrl+Shift+S";
    public string HotkeyTree { get; set; } = "Ctrl+Shift+T";
    /// <summary>Open the tree view when the game's own passive / skill panel key is pressed.</summary>
    public bool FollowGameKeys { get; set; } = true;
    /// <summary>Switch to the skill tab whose name is on screen (read with Windows OCR).</summary>
    public bool FollowSkillOnScreen { get; set; } = true;
    /// <summary>
    /// Set once the game's passive / skill panel has been recognised on this machine. From then on
    /// the screen decides whether that tree view shows; before that, key presses do. Kept per
    /// panel, so trouble recognising one never makes the other close by itself.
    /// </summary>
    /// <summary>Take the point counts under the game's nodes as the character's real points.</summary>
    public bool ReadPointsFromScreen { get; set; } = true;
    public bool PanelSeenPassives { get; set; }
    public bool PanelSeenSkills { get; set; }
    /// <summary>Green rings and numbers marking the next points, per kind of tree.</summary>
    public bool ShowOrderPassives { get; set; } = true;
    public bool ShowOrderSkills { get; set; } = true;
    /// <summary>The "+N" tag: how many points the next step puts into a node, per kind of tree.</summary>
    public bool ShowAmountPassives { get; set; } = true;
    public bool ShowAmountSkills { get; set; } = true;
    /// <summary>List the imported build's per-level lines in the overlay box. The tree view shows the same thing as a picture.</summary>
    public bool ShowBuildLines { get; set; }
    public string GameKeyPassives { get; set; } = "P";
    public string GameKeySkills { get; set; } = "S";
    /// <summary>The game's map key: the map shows the true quest-reward counters, which are read from it.</summary>
    public string GameKeyMap { get; set; } = "M";
    public bool ReadCountersFromMap { get; set; } = true;
    /// <summary>Size of errors.log when the overlay last looked, to notice errors logged since.</summary>
    public long ErrorLogBytes { get; set; }
    // Null until the window has been placed once. (Not NaN: JSON cannot store it, and a settings
    // file that fails to save loses everything else in it too.)
    public double? TreeLeft { get; set; }
    public double? TreeTop { get; set; }
}

/// <summary>Pre-profile progress file (v0.1); only read to migrate it into the first profile.</summary>
public sealed class ProgressState
{
    public int Index { get; set; }
    public HashSet<string> Done { get; set; } = new();
}

/// <summary>Everything remembered about one character.</summary>
public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Character";
    /// <summary>-1 until the game has told us (0 Primalist, 1 Mage, 2 Sentinel, 3 Acolyte, 4 Rogue).</summary>
    public int ClassId { get; set; } = -1;
    public int Mastery { get; set; }
    public int Level { get; set; }
    public string RouteId { get; set; } = "full";
    public int Index { get; set; }
    /// <summary>Ticked tasks as "stepIndex:taskIndex".</summary>
    public HashSet<string> Done { get; set; } = new();
    /// <summary>Reward tasks the player decided not to do.</summary>
    public HashSet<string> Skipped { get; set; } = new();
    /// <summary>In-game seconds, and the value it had on first arrival at each step.</summary>
    public double PlaySeconds { get; set; }
    public Dictionary<int, double> Splits { get; set; } = new();
    public int Deaths { get; set; }
    /// <summary>File name of the build plan in the builds folder; empty = none.</summary>
    public string BuildPlan { get; set; } = "";
    public HashSet<string> PlanDone { get; set; } = new();
    /// <summary>Correction to the computed passive point count, set with the tree view's + / - buttons.</summary>
    public int PassiveOffset { get; set; }
    /// <summary>Points spent per skill, counted by hand in the tree view (the log does not report them).</summary>
    public Dictionary<string, int> SkillPoints { get; set; } = new();
    public string TreeTab { get; set; } = "";
    public string PlannerTab { get; set; } = "";
    /// <summary>Gear slots ticked in the planner, as "stage|slot".</summary>
    public HashSet<string> GearDone { get; set; } = new();
    /// <summary>Loot filter file name -> level to start using it at (0 = unused).</summary>
    public Dictionary<string, int> FilterStages { get; set; } = new();
    public Dictionary<string, TimelineProgress> Timelines { get; set; } = new();
    public Dictionary<string, DungeonProgress> Dungeons { get; set; } = new();
    /// <summary>Tree points as last read from the character's public profile; null = follow the plan instead.</summary>
    public ActualTrees? Actual { get; set; }
    public List<DeathEntry> DeathLog { get; set; } = new();
    /// <summary>Trees where the slider's plan view was chosen over the points read from the game.</summary>
    public HashSet<string> PlanViewTrees { get; set; } = new();
    /// <summary>
    /// Quest passive points / idol slots as last read from the game's map. Rewards the overlay counts
    /// from the step the player stood on then (<see cref="MapIndex"/>) onwards, beyond what it counted
    /// there at the time, are added on top until the next read.
    /// </summary>
    public int? MapPassives { get; set; }
    public int? MapIdols { get; set; }
    public int MapIndex { get; set; }
    public int MapBasePassives { get; set; }
    public int MapBaseIdols { get; set; }
    public string LastSkillTab { get; set; } = "";
    public DateTime LastPlayed { get; set; }

    private static readonly string[] ClassNames = { "Primalist", "Mage", "Sentinel", "Acolyte", "Rogue" };
    private static readonly string[][] MasteryNames =
    {
        new[] { "Beastmaster", "Shaman", "Druid" },
        new[] { "Sorcerer", "Spellblade", "Runemaster" },
        new[] { "Void Knight", "Forge Guard", "Paladin" },
        new[] { "Necromancer", "Lich", "Warlock" },
        new[] { "Bladedancer", "Marksman", "Falconer" },
    };

    public static string ClassName(int classId, int mastery = 0)
    {
        if (classId < 0 || classId >= ClassNames.Length) return "Unknown";
        return mastery >= 1 && mastery <= 3 ? MasteryNames[classId][mastery - 1] : ClassNames[classId];
    }

    public void ResetProgress()
    {
        MapPassives = MapIdols = null;
        Index = 0;
        Done.Clear();
        Skipped.Clear();
        Splits.Clear();
        PlaySeconds = 0;
        Deaths = 0;
    }
}

/// <summary>One death, for the journal.</summary>
public sealed class DeathEntry
{
    public DateTime When { get; set; }
    public string Zone { get; set; } = "";
    public int Level { get; set; }
    public int ZoneLevel { get; set; }
    public double PlaySeconds { get; set; }
    public string Cause { get; set; } = "";

    public static readonly string[] Causes = { "", "Boss mechanic", "One-shot", "Damage over time", "Swarmed", "Under-levelled", "Lag / disconnect" };
}

public sealed class ProfileStore
{
    public string ActiveId { get; set; } = "";
    public List<Profile> Profiles { get; set; } = new();
}

/// <summary>JSON files under %APPDATA%\LastEpochHelper, so progress survives rebuilds and updates.</summary>
public sealed class Storage
{
    private static readonly JsonSerializerOptions Options = new(Guide.JsonOptions)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        // Older settings files may hold "NaN"; read it rather than throwing the whole file away.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string Dir { get; }

    public Storage(string? dir = null)
    {
        Dir = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LastEpochHelper");
        Directory.CreateDirectory(Dir);
    }

    public string PathOf(string name) => Path.Combine(Dir, name);

    /// <summary>A sub-folder for user files (build plans, zone maps), created on first use.</summary>
    public string Folder(string name)
    {
        string path = PathOf(name);
        Directory.CreateDirectory(path);
        return path;
    }

    public bool Exists(string name) => File.Exists(PathOf(name));

    public T Load<T>(string name) where T : new()
    {
        try
        {
            var path = PathOf(name);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new T();
    }

    public void Save<T>(string name, T value)
    {
        try
        {
            var path = PathOf(name);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { } // a value JSON cannot express; never worth crashing the caller for
        catch (NotSupportedException) { }
    }

    public void AppendLine(string name, string line)
    {
        try { File.AppendAllText(PathOf(name), line + Environment.NewLine); }
        catch (IOException) { }
    }
}
