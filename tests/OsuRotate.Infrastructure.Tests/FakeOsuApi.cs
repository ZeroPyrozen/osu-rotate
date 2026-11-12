using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace OsuRotate.Infrastructure.Tests;

// Answers like osu.ppy.sh: /oauth/token hands out tokens, everything else comes from Routes, or from
// Script when a test wants a sequence of answers for one path.
internal sealed class FakeOsuApi : HttpMessageHandler
{
    private int _tokens;

    public ConcurrentQueue<string> Requests { get; } = new();

    public ConcurrentDictionary<string, string> Routes { get; } = new();

    // Answers used in order before Routes, as (status, body, retryAfterSeconds).
    public ConcurrentQueue<(HttpStatusCode Status, string Body, int? RetryAfter)> Script { get; } = new();

    public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

    public bool ThrowNetworkError { get; set; }

    public int TokensIssued => _tokens;

    public string? LastToken { get; private set; }

    public int ApiRequests => Requests.Count(r => !r.StartsWith("POST /oauth/token", StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;
        Requests.Enqueue($"{request.Method} {path}");
        if (ThrowNetworkError)
        {
            throw new HttpRequestException("network down");
        }

        if (path == "/oauth/token")
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (TokenStatus != HttpStatusCode.OK || !body.Contains("\"grant_type\":\"client_credentials\"", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(TokenStatus == HttpStatusCode.OK ? HttpStatusCode.BadRequest : TokenStatus);
            }

            var token = $"token-{Interlocked.Increment(ref _tokens)}";
            return Json(HttpStatusCode.OK, $$"""{"token_type":"Bearer","expires_in":86400,"access_token":"{{token}}"}""");
        }

        LastToken = request.Headers.Authorization?.Parameter;
        if (Script.TryDequeue(out var scripted))
        {
            var response = Json(scripted.Status, scripted.Body);
            if (scripted.RetryAfter is { } seconds)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            return response;
        }

        if (request.Headers.Authorization?.Parameter is not { } bearer || !bearer.StartsWith("token-", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        return Routes.TryGetValue(path, out var json) ? Json(HttpStatusCode.OK, json) : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
}

// Shaped like GET /api/v2/beatmapsets/lookup and GET /api/v2/matches/{id} in the osu! API v2
// documentation and osu-ahr's Beatmapsets.ts and HistoryTypes.ts. Trimmed to the fields that matter.
internal static class Fixtures
{
    public const string Set = """
        {
          "id": 1001, "title": "Hachigatsu no Yoru", "artist": "Silent Siren", "status": "ranked",
          "availability": { "download_disabled": false, "more_information": null },
          "beatmaps": [
            { "id": 2001, "mode": "osu", "mode_int": 0, "difficulty_rating": 5.43, "total_length": 125, "status": "ranked", "convert": false },
            { "id": 2002, "mode": "osu", "mode_int": 0, "difficulty_rating": 3.1, "total_length": 125, "status": "ranked", "convert": false },
            { "id": 2003, "mode": "taiko", "mode_int": 1, "difficulty_rating": 4.0, "total_length": 125, "status": "ranked", "convert": false }
          ],
          "converts": [
            { "id": 2001, "mode": "taiko", "mode_int": 1, "difficulty_rating": 4.71, "total_length": 125, "status": "ranked", "convert": true },
            { "id": 2001, "mode": "fruits", "mode_int": 2, "difficulty_rating": 4.9, "total_length": 125, "status": "ranked", "convert": true }
          ]
        }
        """;

    public static string Unavailable(bool downloadDisabled, string? moreInformation) => $$"""
        {
          "id": 1002, "title": "Taken down", "status": "graveyard",
          "availability": { "download_disabled": {{(downloadDisabled ? "true" : "false")}}, "more_information": {{(moreInformation is null ? "null" : $"\"{moreInformation}\"")}} },
          "beatmaps": [ { "id": 3001, "mode_int": 0, "difficulty_rating": 2.0, "total_length": 60, "status": "graveyard" } ]
        }
        """;

    public const string Match = """
        {
          "match": { "id": 53084403, "start_time": "2026-01-01T10:00:00+00:00", "end_time": null, "name": "5* host rotate" },
          "events": [
            { "id": 11, "detail": { "type": "match-created" }, "timestamp": "2026-01-01T10:00:00+00:00", "user_id": 100 },
            { "id": 12, "detail": { "type": "player-joined" }, "timestamp": "2026-01-01T10:01:00+00:00", "user_id": 200 },
            { "id": 13, "detail": { "type": "host-changed" }, "timestamp": "2026-01-01T10:01:05+00:00", "user_id": 200 },
            { "id": 14, "detail": { "type": "other" }, "timestamp": "2026-01-01T10:02:00+00:00", "user_id": null,
              "game": { "id": 901, "start_time": "2026-01-01T10:02:00+00:00", "end_time": "2026-01-01T10:04:00+00:00", "beatmap_id": 2001,
                "scores": [
                  { "user_id": 200, "score": 812345, "match": { "slot": 0, "team": "none", "pass": 1 } },
                  { "user_id": 300, "score": 0, "match": { "slot": 1, "team": "none", "pass": 0 } }
                ] } },
            { "id": 15, "detail": { "type": "player-kicked" }, "timestamp": "2026-01-01T10:05:00+00:00", "user_id": 300 },
            { "id": 16, "detail": { "type": "other" }, "timestamp": "2026-01-01T10:06:00+00:00", "user_id": null,
              "game": { "id": 902, "start_time": "2026-01-01T10:06:00+00:00", "end_time": null, "beatmap": { "id": 2002 }, "scores": [] } }
          ],
          "users": [ { "id": 100, "username": "creator" }, { "id": 200, "username": "some one" }, { "id": 300, "username": "kicked guy" } ],
          "first_event_id": 11,
          "latest_event_id": 16,
          "current_game_id": 902
        }
        """;
}
