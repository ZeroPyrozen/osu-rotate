using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OsuRotate.BanchoProbe;

// Checks whether osu!'s beatmap search answers an app-only token (client credentials, scope public),
// and which of its filters it honours, for the map pick feature (.scratch/map-pick, issue 01).
// Read-only: it only searches. The token is never written to the report.
internal sealed class SearchProbe(HttpClient http, string baseUrl = "https://osu.ppy.sh")
{
    // The filters the feature would send: a typical room's limits, ranked since 2012, osu! mode.
    internal const double StarMin = 4;
    internal const double StarMax = 5.5;
    internal const int LengthMax = 300;
    internal const int OldestYear = 2012;
    internal static readonly string Query = string.Create(CultureInfo.InvariantCulture, $"stars>={StarMin} stars<={StarMax} length<={LengthMax} ranked>={OldestYear}");

    public async Task<string> RunAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"# Beatmap search probe, {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
        report.AppendLine();

        var (token, tokenDetail) = await TokenAsync(clientId, clientSecret, ct);
        report.AppendLine(CultureInfo.InvariantCulture, $"Token (client_credentials, scope public): {tokenDetail}");
        if (token is null)
        {
            return report.ToString();
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"Query: `q={Query}`, `m=0`, `s=ranked`");
        report.AppendLine();

        var first = await SearchAsync(token, "First page, default sort", sort: null, cursor: null, report, ct);
        if (first?.Cursor is { } cursor)
        {
            await SearchAsync(token, "Second page, default sort (cursor_string)", sort: null, cursor, report, ct);
        }

        await SearchAsync(token, "First page, sort=plays_desc", "plays_desc", cursor: null, report, ct);
        await SearchAsync(token, "First page, sort=difficulty_asc", "difficulty_asc", cursor: null, report, ct);
        return report.ToString();
    }

    private async Task<(string? Token, string Detail)> TokenAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            new Uri(new Uri(baseUrl), "/oauth/token"),
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "client_credentials",
                ["scope"] = "public",
            },
            ct);
        if (!response.IsSuccessStatusCode)
        {
            return (null, $"refused, HTTP {(int)response.StatusCode}. Check the client id and secret.");
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return (body.RootElement.GetProperty("access_token").GetString(), "issued");
    }

    private sealed record Page(string? Cursor);

    private async Task<Page?> SearchAsync(string token, string title, string? sort, string? cursor, StringBuilder report, CancellationToken ct)
    {
        var url = $"{baseUrl}/api/v2/beatmapsets/search?q={Uri.EscapeDataString(Query)}&m=0&s=ranked";
        if (sort is not null)
        {
            url += $"&sort={sort}";
        }

        if (cursor is not null)
        {
            url += $"&cursor_string={Uri.EscapeDataString(cursor)}";
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"## {title}");
        report.AppendLine();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        report.AppendLine(CultureInfo.InvariantCulture, $"- HTTP {(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- Body: `{Shorten(text, 300)}`");
            report.AppendLine();
            return null;
        }

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var next = root.TryGetProperty("cursor_string", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        var sets = root.TryGetProperty("beatmapsets", out var s) && s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().ToList() : [];
        report.AppendLine(CultureInfo.InvariantCulture, $"- Total: {(root.TryGetProperty("total", out var total) ? total.ToString() : "absent")}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- Next cursor: {(next is null ? "none" : "present")}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- Sets on this page: {sets.Count}");

        var checks = new FilterChecks();
        var sample = new List<string>();
        foreach (var set in sets)
        {
            checks.AddSet(set);
            if (sample.Count < 5)
            {
                sample.Add(Describe(set));
            }
        }

        foreach (var line in checks.Lines())
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
        }

        if (sample.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("First sets:");
            report.AppendLine();
            foreach (var line in sample)
            {
                report.AppendLine(line);
            }
        }

        report.AppendLine();
        return new Page(next);
    }

    // Whether the results actually respect each filter, per difficulty and per set.
    private sealed class FilterChecks
    {
        private int _difficulties;
        private int _fitting;
        private int _wrongMode;
        private int _starsOut;
        private int _lengthOut;
        private int _setsTooOld;
        private int _setsWithoutDate;
        private int _setsWithoutDifficulties;
        private readonly SortedSet<string> _setStatuses = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _difficultyStatuses = new(StringComparer.Ordinal);

        public void AddSet(JsonElement set)
        {
            _setStatuses.Add(Text(set, "status") ?? "absent");
            if (DateTimeOffset.TryParse(Text(set, "ranked_date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ranked))
            {
                _setsTooOld += ranked.Year < OldestYear ? 1 : 0;
            }
            else
            {
                _setsWithoutDate++;
            }

            if (!set.TryGetProperty("beatmaps", out var maps) || maps.ValueKind != JsonValueKind.Array)
            {
                _setsWithoutDifficulties++;
                return;
            }

            foreach (var map in maps.EnumerateArray())
            {
                _difficulties++;
                _difficultyStatuses.Add(Text(map, "status") ?? "absent");
                var mode = Text(map, "mode");
                var stars = Number(map, "difficulty_rating");
                var length = Number(map, "total_length");
                var wrongMode = mode != "osu";
                var starsOut = stars is not { } st || st < StarMin || st > StarMax;
                var lengthOut = length is not { } len || len > LengthMax;
                _wrongMode += wrongMode ? 1 : 0;
                _starsOut += starsOut ? 1 : 0;
                _lengthOut += lengthOut ? 1 : 0;
                _fitting += !wrongMode && !starsOut && !lengthOut ? 1 : 0;
            }
        }

        public IEnumerable<string> Lines()
        {
            yield return $"Difficulties returned: {_difficulties}, of which fit every limit: {_fitting}";
            yield return $"Difficulties not in osu! mode: {_wrongMode}";
            yield return $"Difficulties outside {StarMin}-{StarMax} stars: {_starsOut}";
            yield return $"Difficulties longer than {LengthMax} s: {_lengthOut}";
            yield return $"Sets ranked before {OldestYear}: {_setsTooOld} (no ranked_date: {_setsWithoutDate})";
            yield return $"Sets without a beatmaps list: {_setsWithoutDifficulties}";
            yield return $"Set statuses: {string.Join(", ", _setStatuses)}; difficulty statuses: {string.Join(", ", _difficultyStatuses)}";
        }
    }

    private static string Describe(JsonElement set)
    {
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"- set {Text(set, "id")} ({Text(set, "status")}, ranked {Text(set, "ranked_date")}): ");
        if (set.TryGetProperty("beatmaps", out var maps) && maps.ValueKind == JsonValueKind.Array)
        {
            line.AppendJoin("; ", maps.EnumerateArray().Select(m =>
                string.Create(CultureInfo.InvariantCulture, $"{Text(m, "id")} {Text(m, "mode")} {Number(m, "difficulty_rating"):0.00}* {Number(m, "total_length")}s")));
        }

        return line.ToString();
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static string Shorten(string text, int max) =>
        (text.Length <= max ? text : text[..max] + "...").ReplaceLineEndings(" ").Replace('`', '\'');
}
