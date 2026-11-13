using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;

namespace OsuRotate.Bancho.Tests;

// The messages below are real BanchoBot output collected by osu-ahr's test suite.
public class BanchoParserTests
{
    public static TheoryData<string, LobbyEvent> Messages => new()
    {
        // Players
        // Not from osu-ahr: the reply to !mp make when the account already has its four rooms open.
        {
            "You cannot create any more tournament matches. Please close any previous tournament matches you have open.",
            new RoomCreateRefused("You cannot create any more tournament matches. Please close any previous tournament matches you have open.")
        },
        { "Swgciai joined in slot 4.", new PlayerJoined("Swgciai", 4, Team.None) },
        { "Foet_Mnagyo joined in slot 1.", new PlayerJoined("Foet_Mnagyo", 1, Team.None) },
        { "- Cylcl joined in slot 5.", new PlayerJoined("- Cylcl", 5, Team.None) },
        { "Cartist joined in slot 7 for team blue.", new PlayerJoined("Cartist", 7, Team.Blue) },
        { "hmelevsky joined in slot 8 for team red.", new PlayerJoined("hmelevsky", 8, Team.Red) },
        { "Xiux joined in slot 2 for team red.", new PlayerJoined("Xiux", 2, Team.Red) },
        { "Swgciai left the game.", new PlayerLeft("Swgciai") },
        { "Foet_Mnagyo left the game.", new PlayerLeft("Foet_Mnagyo") },
        { "- Cylcl left the game.", new PlayerLeft("- Cylcl") },
        { "azi03 moved to slot 6", new PlayerMoved("azi03", 6) },
        { "a6387534 changed to Red", new PlayerTeamChanged("a6387534", Team.Red) },
        { "milisaurus changed to Blue", new PlayerTeamChanged("milisaurus", Team.Blue) },
        { "Kicked damn from the match", new PlayerKicked("damn") },
        { "Kicked damn from the match.", new PlayerKicked("damn") },
        { "Invited someone to the room", new PlayerInvited("someone") },
        { "Natu rolls 13 point(s)", new PlayerRolled("Natu", 13) },
        { "gurdil203 rolls 1 point(s)", new PlayerRolled("gurdil203", 1) },
        { "DAE rolls rolls 46 point(s)", new PlayerRolled("DAE rolls", 46) },

        // Host
        { "Swgciai became the host.", new HostChanged("Swgciai") },
        { "Foet_Mnagyo became the host.", new HostChanged("Foet_Mnagyo") },
        { "- Cylcl became the host.", new HostChanged("- Cylcl") },
        { "Changed match host to Brena_Pia", new HostTransferAcknowledged("Brena_Pia") },
        { "Cleared match host", new HostCleared() },
        { "User not found", new UserNotFound() },
        { "No user specified", new NoUserSpecified() },

        // Beatmaps, including titles with brackets, quotes and parentheses
        { "Host is changing map...", new BeatmapChanging() },
        { "Beatmap changed to: Noah - Celestial stinger [apl's EXHAUST] (https://osu.ppy.sh/b/1454083)", new BeatmapChanged(1454083, "Noah - Celestial stinger [apl's EXHAUST]", false) },
        { "Beatmap changed to: Paul Bazooka - DrunkenSteiN [bor's Insane] (https://osu.ppy.sh/b/1913126)", new BeatmapChanged(1913126, "Paul Bazooka - DrunkenSteiN [bor's Insane]", false) },
        { "Beatmap changed to: supercell - Hoshi ga Matataku Konna Yoru ni [Sharlo's Insane] (https://osu.ppy.sh/b/670743)", new BeatmapChanged(670743, "supercell - Hoshi ga Matataku Konna Yoru ni [Sharlo's Insane]", false) },
        { "Beatmap changed to: Lil Boom - \"Already Dead \" instrumental (Omae Wa Mou) (https://osu.ppy.sh/b/2122318)", new BeatmapChanged(2122318, "Lil Boom - \"Already Dead \" instrumental (Omae Wa Mou)", false) },
        { "Changed beatmap to https://osu.ppy.sh/b/2145701 Camellia feat. Nanahira - NANI THE FUCK!!", new BeatmapChanged(2145701, "Camellia feat. Nanahira - NANI THE FUCK!!", true) },
        { "Invalid map ID provided", new InvalidBeatmapId() },

        // Match
        { "All players are ready", new AllPlayersReady() },
        { "The match has started!", new MatchStarted() },
        { "Started the match", new MatchStartAcknowledged() },
        { "The match has already been started", new MatchAlreadyStarted() },
        { "Swgciai finished playing (Score: 18048202, PASSED).", new PlayerFinished("Swgciai", 18048202, true) },
        { "Foet_Mnagyo finished playing (Score: 290043, FAILED).", new PlayerFinished("Foet_Mnagyo", 290043, false) },
        { "- Cylcl finished playing (Score: 2095838, PASSED).", new PlayerFinished("- Cylcl", 2095838, true) },
        { "The match has finished!", new MatchFinished() },
        { "Aborted the match", new MatchAborted() },
        { "The match is not in progress", new MatchNotInProgress() },

        // Countdowns
        { "Match starts in 10 seconds", new CountdownTick(10) },
        { "Match starts in 2 minutes and 40 seconds", new CountdownTick(160) },
        { "Match starts in 2 minutes", new CountdownTick(120) },
        { "Match starts in 1 minute and 1 second", new CountdownTick(61) },
        { "Queued the match to start in 30 seconds", new CountdownStarted(30) },
        { "Queued the match to start in 1 second", new CountdownStarted(1) },
        { "Queued the match to start in 5 minutes and 40 seconds", new CountdownStarted(340) },
        { "Good luck, have fun!", new CountdownFinished() },
        { "Countdown aborted", new CountdownAborted() },

        // Room
        { "Created the tournament match https://osu.ppy.sh/mp/52612489 irctestroom", new RoomCreated(52612489, "irctestroom") },
        { "Created the tournament match https://osu.ppy.sh/mp/52849259 irctest_room^^", new RoomCreated(52849259, "irctest_room^^") },
        { "Created the tournament match https://osu.ppy.sh/mp/52849326 irc test room 1", new RoomCreated(52849326, "irc test room 1") },
        { "Closed the match", new RoomCloseAcknowledged() },
        { "Changed the match password", new PasswordChanged() },
        { "Removed the match password", new PasswordRemoved() },
        { "Changed match to size 8", new RoomSizeChanged(8) },
        { "Changed match to size 16", new RoomSizeChanged(16) },
        { "Changed match settings to 8 slots, HeadToHead, Score", new RoomSettingsChanged(8, "HeadToHead", "Score") },
        { "Changed match settings to TeamVs, ScoreV2", new RoomSettingsChanged(null, "TeamVs", "ScoreV2") },
        { "Invalid or no settings provided", new InvalidSettings() },
        { "Invalid or no size provided", new InvalidSize() },
        { "Locked the match", new RoomLocked() },
        { "Unlocked the match", new RoomUnlocked() },

        // Referees
        { "Added damn to the match referees", new RefereeAdded("damn") },
        { "Removed damn from the match referees", new RefereeRemoved("damn") },
        { "Match referees:", new RefereeListStarted() },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void Parses_BanchoBot_message(string message, LobbyEvent expected)
    {
        Assert.Equal(expected, BanchoParser.Parse(message));
    }

    [Theory]
    [InlineData("hello!")]
    [InlineData("")]
    [InlineData("joined in slot 4.")]
    public void Returns_null_for_unknown_text(string message)
    {
        Assert.Null(BanchoParser.Parse(message));
    }
}
