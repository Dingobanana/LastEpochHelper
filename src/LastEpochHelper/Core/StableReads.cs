namespace LastEpochHelper.Core;

/// <summary>
/// Text recognition wobbles: the same label can come out as "1/3" in one look and "7/3" or nothing
/// in the next. Taking each look at face value makes the drawn tree flicker between versions, so a
/// value is only passed on once two looks in a row agree on it.
/// </summary>
public sealed class StableReads
{
    private readonly Dictionary<string, Dictionary<int, int>> _previous = new();

    /// <param name="key">What was read (one tree).</param>
    /// <returns>The part of <paramref name="read"/> that the look before this one also saw.</returns>
    public Dictionary<int, int> Confirm(string key, IReadOnlyDictionary<int, int> read)
    {
        _previous.TryGetValue(key, out var before);
        _previous[key] = new Dictionary<int, int>(read);
        var agreed = new Dictionary<int, int>();
        if (before is null) return agreed;
        foreach (var (node, value) in read)
            if (before.TryGetValue(node, out int earlier) && earlier == value) agreed[node] = value;
        return agreed;
    }
}
