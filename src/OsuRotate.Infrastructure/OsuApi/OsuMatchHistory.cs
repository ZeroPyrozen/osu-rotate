using System.Globalization;
using System.Text.Json;
using OsuRotate.Application;

namespace OsuRotate.Infrastructure.OsuApi;

// IMatchHistory over GET /api/v2/matches/{id}. Pages are at most 100 events; "after" reads on from a
// known event, which is how the host-kick counterattack follows a room.
public sealed class OsuMatchHistory(OsuApiClient api) : IMatchHistory
{
    public const int PageSize = 100;

    public async ValueTask<MatchLookup> GetAsync(long matchId, long? afterEventId, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"/api/v2/matches/{matchId}?limit={PageSize}");
        if (afterEventId is { } after)
        {
            path += string.Create(CultureInfo.InvariantCulture, $"&after={after}");
        }

        var result = await api.GetAsync(path, cancellationToken).ConfigureAwait(false);
        using var body = result.Body;
        switch (result.Outcome)
        {
            case ApiOutcome.NotFound:
                return MatchLookup.NotFound;
            case ApiOutcome.Failed:
                return MatchLookup.Failed;
        }

        try
        {
            return MatchLookup.Found(Read(body!.RootElement));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return MatchLookup.Failed;
        }
    }

    private static MatchHistory Read(JsonElement root)
    {
        var match = root.GetProperty("match");
        var users = new Dictionary<int, string>();
        if (root.TryGetProperty("users", out var userList))
        {
            foreach (var user in userList.EnumerateArray())
            {
                users[user.GetProperty("id").GetInt32()] = user.GetProperty("username").GetString() ?? string.Empty;
            }
        }

        var events = root.TryGetProperty("events", out var eventList)
            ? eventList.EnumerateArray().Select(ReadEvent).OrderBy(e => e.Id).ToList()
            : [];

        return new MatchHistory(
            match.GetProperty("id").GetInt64(),
            match.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            Time(match.GetProperty("start_time"))!.Value,
            Time(match.TryGetProperty("end_time", out var end) ? end : default),
            root.TryGetProperty("current_game_id", out var current) && current.ValueKind == JsonValueKind.Number ? current.GetInt64() : null,
            root.TryGetProperty("latest_event_id", out var latest) && latest.ValueKind == JsonValueKind.Number ? latest.GetInt64() : events.LastOrDefault()?.Id ?? 0,
            events,
            users);
    }

    private static MatchEvent ReadEvent(JsonElement e) => new(
        e.GetProperty("id").GetInt64(),
        e.GetProperty("detail").GetProperty("type").GetString() ?? "other",
        Time(e.GetProperty("timestamp"))!.Value,
        e.TryGetProperty("user_id", out var user) && user.ValueKind == JsonValueKind.Number ? user.GetInt32() : null,
        e.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Object ? ReadGame(game) : null);

    private static MatchGame ReadGame(JsonElement game) => new(
        game.GetProperty("id").GetInt64(),
        Time(game.GetProperty("start_time"))!.Value,
        Time(game.TryGetProperty("end_time", out var end) ? end : default),
        game.TryGetProperty("beatmap_id", out var mapId) && mapId.ValueKind == JsonValueKind.Number ? mapId.GetInt32()
            : game.TryGetProperty("beatmap", out var map) && map.ValueKind == JsonValueKind.Object ? map.GetProperty("id").GetInt32() : 0,
        game.TryGetProperty("scores", out var scores)
            ? [.. scores.EnumerateArray().Select(s => new MatchGameScore(
                s.GetProperty("user_id").GetInt32(),
                s.GetProperty("score").GetInt64(),
                s.TryGetProperty("passed", out var passed) ? passed.ValueKind == JsonValueKind.True
                    : s.TryGetProperty("match", out var slot) && slot.TryGetProperty("pass", out var pass) && (pass.ValueKind == JsonValueKind.True || (pass.ValueKind == JsonValueKind.Number && pass.GetInt32() != 0))))]
            : []);

    private static DateTimeOffset? Time(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null;
}
