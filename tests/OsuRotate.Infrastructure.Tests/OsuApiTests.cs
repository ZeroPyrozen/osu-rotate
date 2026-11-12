using System.Net;
using Microsoft.Extensions.Time.Testing;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.OsuApi;

namespace OsuRotate.Infrastructure.Tests;

public abstract class OsuApiTests : IDisposable
{
    private const string Secret = "very-secret-value";
    private const string SetPath = "/api/v2/beatmapsets/lookup?beatmap_id=";

    private protected readonly FakeOsuApi _api = new();
    private protected readonly ListLogger<OsuApiClient> _log = new();
    private protected readonly FakeTimeProvider _fakeTime = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly HttpClient _http;
    private OsuApiClient? _client;

    protected OsuApiTests() => _http = new HttpClient(_api);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _client?.Dispose();
            _http.Dispose();
            _api.Dispose();
        }
    }

    // Real time by default, with retry waits short enough not to matter.
    private protected OsuApiClient Client(int perMinute = 30, int clientId = 7, string secret = Secret, TimeProvider? time = null) =>
        _client = new OsuApiClient(
            _http,
            new OsuApiOptions { ClientId = clientId, ClientSecret = secret, MaxRequestsPerMinute = perMinute, RetryDelay = TimeSpan.FromMilliseconds(1) },
            _log,
            time);

    private protected OsuBeatmapCatalog Catalog(TimeProvider? time = null) => new(Client(time: time), time);

    private protected void Serve(int beatmapId, string json) => _api.Routes[SetPath + beatmapId] = json;

    public class Beatmaps : OsuApiTests
    {
        [Fact]
        public async Task Finds_a_map_with_its_set()
        {
            Serve(2001, Fixtures.Set);
            var lookup = await Catalog().FindAsync(2001, GameMode.Osu, allowConvert: true, default);

            Assert.Equal(BeatmapLookupOutcome.Found, lookup.Outcome);
            Assert.Equal(new BeatmapInfo(2001, 1001, "Hachigatsu no Yoru", GameMode.Osu, 5.43, 125, "ranked"), lookup.Beatmap);
            Assert.Equal(["POST /oauth/token", $"GET {SetPath}2001"], _api.Requests);
        }

        [Fact]
        public async Task A_convert_is_found_with_its_own_star_rating_when_converts_are_allowed()
        {
            Serve(2001, Fixtures.Set);
            var lookup = await Catalog().FindAsync(2001, GameMode.Taiko, allowConvert: true, default);

            Assert.Equal(BeatmapLookupOutcome.Found, lookup.Outcome);
            Assert.Equal(GameMode.Taiko, lookup.Beatmap!.Mode);
            Assert.Equal(4.71, lookup.Beatmap.StarRating);
        }

        [Fact]
        public async Task A_convert_is_a_mode_mismatch_when_converts_are_not_allowed()
        {
            Serve(2001, Fixtures.Set);
            var lookup = await Catalog().FindAsync(2001, GameMode.Taiko, allowConvert: false, default);
            Assert.Equal(BeatmapLookupOutcome.ModeMismatch, lookup.Outcome);
        }

        [Fact]
        public async Task A_map_in_the_converts_list_is_a_convert_even_without_its_flag()
        {
            Serve(2001, Fixtures.Set.Replace(", \"convert\": true", string.Empty, StringComparison.Ordinal));
            var lookup = await Catalog().FindAsync(2001, GameMode.Taiko, allowConvert: false, default);
            Assert.Equal(BeatmapLookupOutcome.ModeMismatch, lookup.Outcome);
        }

        [Fact]
        public async Task A_map_made_for_the_mode_needs_no_converts()
        {
            Serve(2003, Fixtures.Set);
            var lookup = await Catalog().FindAsync(2003, GameMode.Taiko, allowConvert: false, default);
            Assert.Equal(BeatmapLookupOutcome.Found, lookup.Outcome);
            Assert.Equal(4.0, lookup.Beatmap!.StarRating);
        }

        [Theory]
        [InlineData(2002, GameMode.Taiko)]
        [InlineData(2001, GameMode.Mania)]
        [InlineData(2003, GameMode.Osu)]
        public async Task A_mode_the_map_does_not_have_is_a_mismatch(int beatmapId, GameMode mode)
        {
            Serve(beatmapId, Fixtures.Set);
            Assert.Equal(BeatmapLookupOutcome.ModeMismatch, (await Catalog().FindAsync(beatmapId, mode, allowConvert: true, default)).Outcome);
        }

        [Fact]
        public async Task A_removed_map_is_not_found()
        {
            Assert.Equal(BeatmapLookupOutcome.NotFound, (await Catalog().FindAsync(9999, GameMode.Osu, true, default)).Outcome);
        }

        [Theory]
        [InlineData(true, null)]
        [InlineData(false, "Removed by request of the artist.")]
        public async Task A_map_without_a_download_is_not_available(bool disabled, string? reason)
        {
            Serve(3001, Fixtures.Unavailable(disabled, reason));
            var catalog = Catalog();
            Assert.Equal(BeatmapLookupOutcome.NotAvailable, (await catalog.FindAsync(3001, GameMode.Osu, true, default)).Outcome);

            // Remembered, like a found map.
            Assert.Equal(BeatmapLookupOutcome.NotAvailable, (await catalog.FindAsync(3001, GameMode.Osu, true, default)).Outcome);
            Assert.Equal(1, _api.ApiRequests);
        }

        [Fact]
        public async Task One_request_caches_the_whole_set_for_a_day()
        {
            Serve(2001, Fixtures.Set);
            var catalog = Catalog(_fakeTime);
            await catalog.FindAsync(2001, GameMode.Osu, true, default);
            Assert.Equal(BeatmapLookupOutcome.Found, (await catalog.FindAsync(2002, GameMode.Osu, true, default)).Outcome);
            Assert.Equal(BeatmapLookupOutcome.Found, (await catalog.FindAsync(2001, GameMode.Catch, true, default)).Outcome);
            Assert.Equal(BeatmapLookupOutcome.ModeMismatch, (await catalog.FindAsync(2001, GameMode.Taiko, false, default)).Outcome);
            Assert.Equal(1, _api.ApiRequests);
            Assert.Equal(5, catalog.CachedMaps);

            _fakeTime.Advance(OsuBeatmapCatalog.CacheFor - TimeSpan.FromSeconds(1));
            await catalog.FindAsync(2001, GameMode.Osu, true, default);
            Assert.Equal(1, _api.ApiRequests);

            _fakeTime.Advance(TimeSpan.FromSeconds(1));
            await catalog.FindAsync(2001, GameMode.Osu, true, default);
            Assert.Equal(2, _api.ApiRequests);
        }

        [Fact]
        public async Task An_unavailable_map_is_asked_about_again_after_a_day()
        {
            Serve(3001, Fixtures.Unavailable(true, null));
            var catalog = Catalog(_fakeTime);
            await catalog.FindAsync(3001, GameMode.Osu, true, default);
            _fakeTime.Advance(OsuBeatmapCatalog.CacheFor);
            Serve(3001, Fixtures.Set.Replace("\"id\": 2002", "\"id\": 3001", StringComparison.Ordinal));
            Assert.Equal(BeatmapLookupOutcome.Found, (await catalog.FindAsync(3001, GameMode.Osu, true, default)).Outcome);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("""{"id": 1, "title": "x", "beatmaps": [ { "id": 5 } ]}""")]
        public async Task A_malformed_answer_fails_quietly(string body)
        {
            Serve(5, body);
            Assert.Equal(BeatmapLookupOutcome.Failed, (await Catalog().FindAsync(5, GameMode.Osu, true, default)).Outcome);
        }

        [Fact]
        public async Task Failed_lookups_are_not_cached()
        {
            _api.Script.Enqueue((HttpStatusCode.InternalServerError, "", null));
            _api.Script.Enqueue((HttpStatusCode.InternalServerError, "", null));
            _api.Script.Enqueue((HttpStatusCode.InternalServerError, "", null));
            Serve(2001, Fixtures.Set);
            var catalog = Catalog();
            Assert.Equal(BeatmapLookupOutcome.Failed, (await catalog.FindAsync(2001, GameMode.Osu, true, default)).Outcome);
            Assert.Equal(BeatmapLookupOutcome.Found, (await catalog.FindAsync(2001, GameMode.Osu, true, default)).Outcome);
        }
    }

    public class Requests : OsuApiTests
    {
        [Fact]
        public async Task Server_errors_are_retried()
        {
            Serve(2001, Fixtures.Set);
            _api.Script.Enqueue((HttpStatusCode.BadGateway, "", null));
            _api.Script.Enqueue((HttpStatusCode.ServiceUnavailable, "", null));
            Assert.Equal(BeatmapLookupOutcome.Found, (await Catalog().FindAsync(2001, GameMode.Osu, true, default)).Outcome);
            Assert.Equal(3, _api.ApiRequests);
        }

        [Fact]
        public async Task Retries_give_up_after_the_limit()
        {
            for (var i = 0; i < 5; i++)
            {
                _api.Script.Enqueue((HttpStatusCode.InternalServerError, "", null));
            }

            Assert.Equal(BeatmapLookupOutcome.Failed, (await Catalog().FindAsync(2001, GameMode.Osu, true, default)).Outcome);
            Assert.Equal(3, _api.ApiRequests);
        }

        [Fact]
        public async Task A_rate_limited_answer_waits_as_told_and_tries_again()
        {
            Serve(2001, Fixtures.Set);
            _api.Script.Enqueue((HttpStatusCode.TooManyRequests, "", 2));
            var lookup = Catalog(_fakeTime).FindAsync(2001, GameMode.Osu, true, default).AsTask();
            await WaitForAsync(() => _api.ApiRequests == 1);
            _fakeTime.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50);
            Assert.False(lookup.IsCompleted);

            _fakeTime.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(BeatmapLookupOutcome.Found, (await lookup.WaitAsync(TimeSpan.FromSeconds(10))).Outcome);
            Assert.Equal(2, _api.ApiRequests);
        }

        [Fact]
        public async Task A_rate_limit_longer_than_a_minute_is_not_waited_out()
        {
            _api.Script.Enqueue((HttpStatusCode.TooManyRequests, "", 600));
            var lookup = await Catalog(_fakeTime).FindAsync(2001, GameMode.Osu, true, default).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(BeatmapLookupOutcome.Failed, lookup.Outcome);
            Assert.Equal(1, _api.ApiRequests);
        }

        [Fact]
        public async Task A_network_failure_is_retried_then_fails_quietly()
        {
            _api.ThrowNetworkError = true;
            Assert.Equal(BeatmapLookupOutcome.Failed, (await Catalog().FindAsync(2001, GameMode.Osu, true, default)).Outcome);
            Assert.Equal(3, _api.Requests.Count);
        }

        [Fact]
        public async Task Other_client_errors_fail_without_retrying()
        {
            _api.Script.Enqueue((HttpStatusCode.Forbidden, "", null));
            Assert.Equal(BeatmapLookupOutcome.Failed, (await Catalog().FindAsync(2001, GameMode.Osu, true, default)).Outcome);
            Assert.Equal(1, _api.ApiRequests);
        }

        [Fact]
        public async Task The_token_is_reused_and_renewed_after_a_401()
        {
            Serve(2001, Fixtures.Set);
            Serve(2003, Fixtures.Set);
            var client = Client(time: _fakeTime);
            await new OsuBeatmapCatalog(client, _fakeTime).FindAsync(2001, GameMode.Osu, true, default);
            await new OsuBeatmapCatalog(client, _fakeTime).FindAsync(2003, GameMode.Taiko, true, default);
            Assert.Equal(1, _api.TokensIssued);
            Assert.Equal("token-1", _api.LastToken);

            // A fresh catalog has nothing cached, so this goes to the API, which now refuses the token.
            _api.Script.Enqueue((HttpStatusCode.Unauthorized, "", null));
            Assert.Equal(BeatmapLookupOutcome.Found, (await new OsuBeatmapCatalog(client, _fakeTime).FindAsync(2001, GameMode.Osu, true, default)).Outcome);
            Assert.Equal(2, _api.TokensIssued);
            Assert.Equal("token-2", _api.LastToken);
        }

        [Fact]
        public async Task The_token_is_renewed_before_it_expires()
        {
            Serve(2001, Fixtures.Set);
            var catalog = Catalog(_fakeTime);
            await catalog.FindAsync(2001, GameMode.Osu, true, default);
            _fakeTime.Advance(OsuBeatmapCatalog.CacheFor - TimeSpan.FromMinutes(1));
            await catalog.FindAsync(2002, GameMode.Taiko, true, default);
            Assert.Equal(2, _api.TokensIssued);
        }

        [Fact]
        public async Task A_refused_token_fails_quietly_and_is_logged_without_the_secret()
        {
            _api.TokenStatus = HttpStatusCode.Unauthorized;
            Assert.Equal(BeatmapLookupOutcome.Failed, (await Catalog().FindAsync(2001, GameMode.Osu, true, default)).Outcome);
            Assert.Equal(0, _api.ApiRequests);
            Assert.Contains(_log.Messages, m => m.Contains("refused a token", StringComparison.Ordinal));
            Assert.DoesNotContain(_log.Messages, m => m.Contains(Secret, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(0, Secret)]
        [InlineData(7, "")]
        public async Task Without_credentials_nothing_is_sent(int clientId, string secret)
        {
            var catalog = new OsuBeatmapCatalog(Client(clientId: clientId, secret: secret));
            Assert.Equal(BeatmapLookupOutcome.Failed, (await catalog.FindAsync(2001, GameMode.Osu, true, default)).Outcome);
            Assert.Empty(_api.Requests);
        }

        [Fact]
        public async Task Requests_are_spaced_to_stay_under_the_limit()
        {
            Serve(2001, Fixtures.Set);
            Serve(2003, Fixtures.Set);
            var client = Client(perMinute: 2, time: _fakeTime);

            // The token request and one lookup use up the minute.
            await new OsuBeatmapCatalog(client, _fakeTime).FindAsync(2001, GameMode.Osu, true, default);
            var second = new OsuBeatmapCatalog(client, _fakeTime).FindAsync(2003, GameMode.Taiko, true, default).AsTask();
            await Task.Delay(50);
            Assert.False(second.IsCompleted);

            _fakeTime.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(BeatmapLookupOutcome.Found, (await second.WaitAsync(TimeSpan.FromSeconds(10))).Outcome);
        }

        [Fact]
        public async Task Cancelling_the_caller_cancels_the_request()
        {
            using var cancel = new CancellationTokenSource();
            await cancel.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Catalog().FindAsync(2001, GameMode.Osu, true, cancel.Token));
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!condition())
            {
                await Task.Delay(5, timeout.Token);
            }
        }
    }

    public class Matches : OsuApiTests
    {
        private const string MatchPath = "/api/v2/matches/53084403?limit=100";

        [Fact]
        public async Task Reads_events_users_games_and_scores()
        {
            _api.Routes[MatchPath] = Fixtures.Match;
            var lookup = await new OsuMatchHistory(Client()).GetAsync(53084403, null, default);

            Assert.Equal(MatchLookupOutcome.Found, lookup.Outcome);
            var history = lookup.History!;
            Assert.Equal(53084403, history.MatchId);
            Assert.Equal("5* host rotate", history.Name);
            Assert.Null(history.EndTime);
            Assert.Equal(16, history.LatestEventId);
            Assert.Equal([11L, 12, 13, 14, 15, 16], history.Events.Select(e => e.Id));
            Assert.Equal(["match-created", "player-joined", "host-changed", "other", "player-kicked", "other"], history.Events.Select(e => e.Type));
            Assert.Equal("kicked guy", history.UsernameOf(history.Events[4].UserId));
            Assert.Null(history.UsernameOf(null));

            var played = history.Events[3].Game!;
            Assert.Equal(2001, played.BeatmapId);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 10, 4, 0, TimeSpan.Zero), played.EndTime);
            Assert.Equal([new MatchGameScore(200, 812345, true), new MatchGameScore(300, 0, false)], played.Scores);
            Assert.Equal(2002, history.Events[5].Game!.BeatmapId);
        }

        [Fact]
        public async Task A_game_with_no_end_time_is_running()
        {
            _api.Routes[MatchPath] = Fixtures.Match;
            var history = (await new OsuMatchHistory(Client()).GetAsync(53084403, null, default)).History!;
            Assert.Equal(902, history.RunningGame?.Id);
        }

        [Fact]
        public async Task A_current_game_that_has_ended_is_not_running()
        {
            // Seen live: current_game_id stays set for a moment after the abort.
            _api.Routes[MatchPath] = Fixtures.Match.Replace("\"current_game_id\": 902", "\"current_game_id\": 901", StringComparison.Ordinal);
            var history = (await new OsuMatchHistory(Client()).GetAsync(53084403, null, default)).History!;
            Assert.Null(history.RunningGame);
        }

        [Fact]
        public async Task No_current_game_is_not_running()
        {
            _api.Routes[MatchPath] = Fixtures.Match.Replace("\"current_game_id\": 902", "\"current_game_id\": null", StringComparison.Ordinal);
            var history = (await new OsuMatchHistory(Client()).GetAsync(53084403, null, default)).History!;
            Assert.Null(history.CurrentGameId);
            Assert.Null(history.RunningGame);
        }

        [Fact]
        public async Task Reads_on_after_a_known_event()
        {
            _api.Routes[MatchPath + "&after=14"] = Fixtures.Match;
            var lookup = await new OsuMatchHistory(Client()).GetAsync(53084403, 14, default);
            Assert.Equal(MatchLookupOutcome.Found, lookup.Outcome);
            Assert.Contains("GET " + MatchPath + "&after=14", _api.Requests);
        }

        [Fact]
        public async Task A_closed_room_that_is_gone_is_not_found()
        {
            Assert.Equal(MatchLookupOutcome.NotFound, (await new OsuMatchHistory(Client()).GetAsync(1, null, default)).Outcome);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("""{"match": {"id": 1}}""")]
        public async Task A_malformed_answer_fails_quietly(string body)
        {
            _api.Routes[MatchPath] = body;
            Assert.Equal(MatchLookupOutcome.Failed, (await new OsuMatchHistory(Client()).GetAsync(53084403, null, default)).Outcome);
        }

        [Fact]
        public async Task A_server_error_fails_quietly()
        {
            for (var i = 0; i < 3; i++)
            {
                _api.Script.Enqueue((HttpStatusCode.InternalServerError, "", null));
            }

            Assert.Equal(MatchLookupOutcome.Failed, (await new OsuMatchHistory(Client()).GetAsync(53084403, null, default)).Outcome);
        }
    }
}
