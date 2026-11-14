namespace OsuRotate.Domain;

// The owner's settings for !pick (.scratch/map-pick). Kept apart from MapRegulation on purpose: they
// shape what the bot suggests and never what a host may pick by hand. Changed only from the dashboard.
public sealed record MapPickSettings
{
    // The first year osu! ranked maps, and a far bound that only guards against typing errors.
    public const int FirstRankedYear = 2007;
    public const int LastAllowedYear = 2100;

    public bool Enabled { get; init; }

    // Only maps ranked in or after this year are picked.
    public int OldestRankedYear { get; init; } = 2012;
}
