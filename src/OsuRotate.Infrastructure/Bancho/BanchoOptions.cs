namespace OsuRotate.Infrastructure.Bancho;

// A class rather than a record, so the password cannot be printed by a generated ToString.
public sealed class BanchoOptions
{
    public string Host { get; init; } = "irc.ppy.sh";
    public int Port { get; init; } = 6667;
    public required string Nick { get; init; }
    public required string Password { get; init; }

    // A personal account may send 10 messages every 5 seconds. 8 leaves room for your own in-game chat.
    public int MessagesPerWindow { get; init; } = 8;
    public TimeSpan RateWindow { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan InfoMessagesStaleAfter { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxQueuedInfoMessages { get; init; } = 64;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan IdlePingAfter { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan DeadAfter { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ReconnectBaseDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan ReconnectMaxDelay { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan RefereeListWindow { get; init; } = TimeSpan.FromSeconds(1);
}
