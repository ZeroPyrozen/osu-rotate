namespace OsuRotate.Infrastructure.Bancho;

internal static class BanchoLogin
{
    // Seen live: with a wrong IRC password, Bancho sends MOTD lines (372) including this text, and
    // then closes the connection. It never sends 001 or 464. 464 is kept because standard IRC
    // servers, and osu-ahr's handling, use it.
    private const string AuthenticationRequired = "You are required to authenticate";

    public static bool IsRejection(IrcMessage message) =>
        message.Command == "464"
        || (message.Command is "372" or "NOTICE"
            && message.Parameters.Count > 0
            && message.Parameters[^1].Contains(AuthenticationRequired, StringComparison.OrdinalIgnoreCase));
}
