namespace OsuRotate.Domain;

// What !pick looks for: the regulation's limits (used even while the regulation is turned off) and
// the oldest ranked year. A record, so two equal queries can share one pool.
public sealed record MapPickQuery(
    GameMode Mode,
    double StarMin,
    double StarMax,
    int LengthMinSeconds,
    int LengthMaxSeconds,
    int OldestRankedYear)
{
    public static MapPickQuery For(MapRegulation regulation, MapPickSettings pick) => new(
        regulation.Mode,
        regulation.StarMin,
        regulation.StarMax,
        regulation.LengthMinSeconds,
        regulation.LengthMaxSeconds,
        pick.OldestRankedYear);

    // The same limits as a regulation, so a picked map is rated by the one set of rules.
    public bool Fits(BeatmapInfo map) => new MapRegulation
    {
        Mode = Mode,
        StarMin = StarMin,
        StarMax = StarMax,
        LengthMinSeconds = LengthMinSeconds,
        LengthMaxSeconds = LengthMaxSeconds,
    }.Rate(map).Rate == 0;
}

public enum MapPickOutcome
{
    Found,

    // The search worked and nothing fits the limits.
    NoneFit,

    // The osu! API could not answer.
    Failed,
}

public sealed record MapPickResult(MapPickOutcome Outcome, BeatmapInfo? Beatmap = null)
{
    public static MapPickResult Found(BeatmapInfo beatmap) => new(MapPickOutcome.Found, beatmap);
}
