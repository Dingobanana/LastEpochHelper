namespace LastEpochHelper.Core;

/// <summary>
/// Notices when a panel key does not open that panel in the game - someone moving with WASD has
/// moved the game's skills key off S, and every step down would pop the build tree up and away
/// again. A press counts as unconfirmed when the screen never showed the panel before the next press
/// of the same key; after <see cref="Limit"/> of them in a row the key is not worth following.
/// </summary>
public sealed class PanelKeyCheck
{
    public const int Limit = 3;
    private readonly Dictionary<string, int> _unconfirmed = new();
    private string? _lastKind;
    private bool _lastConfirmed;

    /// <summary>A press of the key for <paramref name="kind"/>. True when it should be ignored from now on.</summary>
    public bool Pressed(string kind)
    {
        int count = _lastKind == kind && !_lastConfirmed ? _unconfirmed.GetValueOrDefault(kind) + 1 : 0;
        _unconfirmed[kind] = count;
        _lastKind = kind;
        _lastConfirmed = false;
        return count >= Limit;
    }

    /// <summary>The screen shows the panel of <paramref name="kind"/>: the last press of its key did open it.</summary>
    public void Seen(string kind)
    {
        if (_lastKind == kind) _lastConfirmed = true;
        _unconfirmed[kind] = 0;
    }
}
