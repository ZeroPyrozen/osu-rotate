namespace OsuRotate.BanchoProbe;

// A class rather than a record, so secrets cannot be printed by a generated ToString.
internal sealed class ProbeOptions
{
    public string Host { get; init; } = "irc.ppy.sh";
    public int Port { get; init; } = 6667;
    public required string Nick { get; init; }
    public required string Password { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }

    public bool KeepRoom { get; init; }
    public bool SkipMatch { get; init; }
    public long? CheckRoom { get; init; }

    public TimeSpan ServerReplyWait { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan JoinWait { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ChatWait { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan MatchWait { get; init; } = TimeSpan.FromMinutes(4);
    public TimeSpan CoexistWait { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan AbortAfter { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan AfterMotdWait { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan IdleDuration { get; init; } = TimeSpan.FromMinutes(6);

    // The bot's reconnect limit: an idle lobby must produce traffic more often than this.
    public TimeSpan DeadAfter { get; init; } = TimeSpan.FromMinutes(5);

    // About 7 messages per 5 seconds, inside the personal account limit of 10.
    public TimeSpan SendSpacing { get; init; } = TimeSpan.FromMilliseconds(700);
}
