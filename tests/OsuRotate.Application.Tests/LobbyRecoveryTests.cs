using Microsoft.Extensions.Time.Testing;
using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Application.Tests;

public class LobbyRecoveryTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private static readonly LobbySettings Configured = new() { OwnerName = "creator", BotVersion = "2.0.0" };

    private static readonly MatchGame Running = new(902, new DateTimeOffset(2025, 12, 31, 23, 58, 0, TimeSpan.Zero), null, 4242, []);
    private static readonly MatchGame Ended = Running with { EndTime = new DateTimeOffset(2025, 12, 31, 23, 59, 0, TimeSpan.Zero) };

    private static async Task<InMemoryLobbyStore> StoreWith(SavedLobby lobby)
    {
        var store = new InMemoryLobbyStore();
        await store.SaveAsync(lobby, default);
        return store;
    }

    private static MatchLookup History(MatchGame? game, long? current) => MatchLookup.Found(new MatchHistory(
        7, "room", DateTimeOffset.UnixEpoch, null, current, 1,
        game is null ? [] : [new MatchEvent(1, "other", game.StartTime, null, game)],
        new Dictionary<int, string>()));

    private static readonly SavedLobby Saved = new(
        new SavedSession(7, "my room", ["p2", "p3", "p1"], "p1"),
        ["denied"],
        new LobbySettings { OwnerName = "someone else", BotVersion = "old", AfkKickThreshold = 9 });

    [Fact]
    public async Task Nothing_saved_starts_idle_with_the_configured_settings()
    {
        var recovered = await LobbyRecovery.RecoverAsync(new InMemoryLobbyStore(), new FixedHistory(MatchLookup.Failed), Configured, Time, default);

        Assert.Equal(LobbyStatus.Idle, recovered.Lifecycle.Status);
        Assert.Null(recovered.Session);
        Assert.Equal(Configured, recovered.Engine.Settings);
        Assert.False(recovered.Engine.IsRestorePending);
    }

    [Fact]
    public async Task Saved_rules_come_back_with_the_owner_and_version_from_the_host()
    {
        var recovered = await LobbyRecovery.RecoverAsync(await StoreWith(Saved), new FixedHistory(MatchLookup.Failed), Configured, Time, default);

        Assert.Equal(9, recovered.Engine.Settings.AfkKickThreshold);
        Assert.Equal("creator", recovered.Engine.Settings.OwnerName);
        Assert.Equal("2.0.0", recovered.Engine.Settings.BotVersion);
        Assert.Equal(["denied"], recovered.Engine.DenyList.Keys);
    }

    [Fact]
    public async Task A_saved_room_is_rejoined_with_the_queue_waiting_to_be_restored()
    {
        var history = new FixedHistory(History(null, null));
        var recovered = await LobbyRecovery.RecoverAsync(await StoreWith(Saved), history, Configured, Time, default);

        Assert.Equal(LobbyStatus.Connecting, recovered.Lifecycle.Status);
        Assert.Equal(7, recovered.Lifecycle.RoomId);
        Assert.Equal("my room", recovered.Lifecycle.Title);
        Assert.True(recovered.Engine.IsRestorePending);
        Assert.False(recovered.MatchRunning);
        Assert.Equal([7L], history.Asked);
    }

    [Fact]
    public async Task A_running_game_is_resumed()
    {
        var recovered = await LobbyRecovery.RecoverAsync(await StoreWith(Saved), new FixedHistory(History(Running, 902)), Configured, Time, default);
        Assert.True(recovered.MatchRunning);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 902L)]
    [InlineData(false, 901L)]
    public async Task A_game_that_ended_or_is_not_current_is_not_resumed(bool ended, long? current)
    {
        var game = ended ? Ended : Running;
        var recovered = await LobbyRecovery.RecoverAsync(await StoreWith(Saved), new FixedHistory(History(game, current ?? 902)), Configured, Time, default);
        Assert.Equal(!ended && current == 902, recovered.MatchRunning);
    }

    [Theory]
    [InlineData(MatchLookupOutcome.Failed)]
    [InlineData(MatchLookupOutcome.NotFound)]
    public async Task No_answer_from_the_api_means_no_match(MatchLookupOutcome outcome)
    {
        var recovered = await LobbyRecovery.RecoverAsync(await StoreWith(Saved), new FixedHistory(new MatchLookup(outcome, null)), Configured, Time, default);
        Assert.False(recovered.MatchRunning);
        Assert.True(recovered.Engine.IsRestorePending);
    }

    [Fact]
    public async Task A_throwing_history_means_no_match()
    {
        var recovered = await LobbyRecovery.RecoverAsync(await StoreWith(Saved), new FixedHistory(null), Configured, Time, default);
        Assert.False(recovered.MatchRunning);
    }

    [Fact]
    public async Task Without_an_api_key_recovery_still_rejoins()
    {
        var recovered = await LobbyRecovery.RecoverAsync(await StoreWith(Saved), NoMatchHistory.Instance, Configured, Time, default);
        Assert.Equal(7, recovered.Lifecycle.RoomId);
        Assert.False(recovered.MatchRunning);
    }

    private sealed class FixedHistory(MatchLookup? answer) : IMatchHistory
    {
        public List<long> Asked { get; } = [];

        public ValueTask<MatchLookup> GetAsync(long matchId, long? afterEventId, CancellationToken cancellationToken)
        {
            Asked.Add(matchId);
            return answer is null ? throw new InvalidOperationException("history broke") : ValueTask.FromResult(answer);
        }
    }
}
