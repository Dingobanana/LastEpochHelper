using System.Text.RegularExpressions;

namespace LastEpochHelper.Core;

public abstract record LogEvent;
public sealed record SceneLoadEvent(string Scene) : LogEvent;
/// <summary>
/// A scene loaded on top of the current one. The Monolith loads its echoes this way ("R2Q30" is a
/// timeline's boss echo); so are UI and sub-scenes, which is why these do not move the campaign guide.
/// </summary>
public sealed record EchoLoadEvent(string Scene) : LogEvent;
/// <summary>Sent by the server on zone entry, level-up and tree changes.</summary>
public sealed record CharacterLevelEvent(int Level, int ClassId = -1, int Mastery = 0) : LogEvent;
public sealed record CharacterSelectEvent : LogEvent;
public sealed record CharacterCreatedEvent(string Name, int ClassId) : LogEvent;
/// <summary>
/// The game logs a warning naming the quest trigger for some (not all) quest steps, e.g.
/// "Storerooms Sidequest Turn In Speak with Heoborean Soldier".
/// </summary>
public sealed record QuestTriggerEvent(string Name) : LogEvent;
public sealed record PlayerDiedEvent : LogEvent;
/// <summary>The account name, as the chat service reports it on login.</summary>
public sealed record AccountEvent(string Name) : LogEvent;

/// <summary>Turns Player.log lines into events. Lines look like "&lt;timestamp&gt;\tLog\t&lt;message&gt;".</summary>
public static partial class LogParser
{
    // Zones load with mode "Single"; additive loads are UI and sub-scenes.
    [GeneratedRegex(@"\tScene (\S+) load started: load mode: Single")]
    private static partial Regex SceneLoad();

    [GeneratedRegex(@"\tScene (\S+) load started: load mode: Additive")]
    private static partial Regex AdditiveLoad();

    [GeneratedRegex(@"\| characterLevel \| (\d+) \| classID \| (\d+) \| chosenMastery \| (\d+)")]
    private static partial Regex CharacterLevel();

    [GeneratedRegex(@"\tCharacter Created: (.+?) [0-9A-F]{8,} isOnline: \w+, cycle: [^,]*, class: (\d+)")]
    private static partial Regex CharacterCreated();

    [GeneratedRegex(@"update the condition handler in (.+?)\s*$")]
    private static partial Regex QuestTrigger();

    [GeneratedRegex(@"\tConnected to chat as '([^']+)'")]
    private static partial Regex ChatAccount();

    public static LogEvent? Parse(string line)
    {
        // Cheap pre-filter: the log is noisy and almost no line is interesting. Real entries start
        // with a timestamp; stack traces and echoed copies of a message do not.
        if (line.Length < 20 || !char.IsDigit(line[0])) return null;

        var m = SceneLoad().Match(line);
        if (m.Success) return new SceneLoadEvent(m.Groups[1].Value);
        m = AdditiveLoad().Match(line);
        if (m.Success) return new EchoLoadEvent(m.Groups[1].Value);

        m = CharacterLevel().Match(line);
        if (m.Success)
            return new CharacterLevelEvent(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));

        if (line.Contains("condition handler in ", StringComparison.Ordinal))
        {
            m = QuestTrigger().Match(line);
            if (m.Success) return new QuestTriggerEvent(m.Groups[1].Value);
        }

        if (line.Contains("Application state changed to CharacterSelect", StringComparison.Ordinal))
            return new CharacterSelectEvent();

        if (line.Contains("Character Created: ", StringComparison.Ordinal))
        {
            m = CharacterCreated().Match(line);
            if (m.Success) return new CharacterCreatedEvent(m.Groups[1].Value, int.Parse(m.Groups[2].Value));
        }

        if (line.Contains("Player died: -IsLocalPlayer: True", StringComparison.Ordinal))
            return new PlayerDiedEvent();

        if (line.Contains("Connected to chat as ", StringComparison.Ordinal))
        {
            m = ChatAccount().Match(line);
            if (m.Success) return new AccountEvent(m.Groups[1].Value);
        }

        return null;
    }
}
