namespace OsuRotate.Domain;

public static class PlayerNames
{
    // BanchoBot writes names with spaces, while IRC nicks and !mp arguments use underscores.
    public static string ToIrcName(string username) => username.Replace(' ', '_');
}
