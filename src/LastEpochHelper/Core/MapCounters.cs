using System.Text.RegularExpressions;

namespace LastEpochHelper.Core;

/// <summary>
/// Finds the game's own "quest rewards so far" counters on its map screen: "PASSIVE POINTS REWARDS
/// (6/15)" above "IDOL SLOT REWARDS (1/8)" in the bottom left corner. They are the truth the
/// overlay's own bookkeeping only approximates.
/// </summary>
public static class MapCounters
{
    private const double MaxApart = 400;

    /// <returns>Null unless both counters were found close to each other.</returns>
    public static (int Passive, int Idol)? Parse(IEnumerable<ScreenLine> lines, int passiveCap, int idolCap) =>
        Parse(lines, passiveCap, idolCap, out _);

    /// <param name="half">One of the two counters was there but not its partner: worth a closer look.</param>
    public static (int Passive, int Idol)? Parse(IEnumerable<ScreenLine> lines, int passiveCap, int idolCap, out bool half)
    {
        var passives = new List<(int Value, double X, double Y)>();
        var idols = new List<(int Value, double X, double Y)>();
        // Not preceded or followed by another digit: "13/15" must not also read as "3/15" or as "1/150".
        var passive = new Regex($@"(?<!\d)(\d{{1,2}})\s*/\s*{passiveCap}(?!\d)");
        var idol = new Regex($@"(?<!\d)(\d{{1,2}})\s*/\s*{idolCap}(?!\d)");

        foreach (var line in lines)
        {
            // The reader sometimes sees the letter O for a zero in these short counters.
            string text = line.Text.Replace('O', '0').Replace('o', '0');
            foreach (Match m in passive.Matches(text))
                if (int.Parse(m.Groups[1].Value) <= passiveCap) passives.Add((int.Parse(m.Groups[1].Value), line.X, line.Y));
            foreach (Match m in idol.Matches(text))
                if (int.Parse(m.Groups[1].Value) <= idolCap) idols.Add((int.Parse(m.Groups[1].Value), line.X, line.Y));
        }

        // A lone "2/8" could be a node label or anything; the pair, close together, is the counter.
        (int, int)? best = null;
        double bestDistance = MaxApart;
        foreach (var p in passives)
        foreach (var i in idols)
        {
            double distance = Math.Sqrt((p.X - i.X) * (p.X - i.X) + (p.Y - i.Y) * (p.Y - i.Y));
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = (p.Value, i.Value);
        }
        half = best is null && (passives.Count > 0 || idols.Count > 0);
        return best;
    }
}
