using System.Net;
using System.Text;
using OsuRotate.BanchoProbe;

namespace OsuRotate.Bancho.Tests;

public sealed class SearchProbeTests
{
    private const string ApiToken = "secret-api-token";
    private const string ClientSecret = "secret-oauth-value";

    private sealed class FakeOsu(HttpStatusCode searchStatus, string searchBody) : HttpMessageHandler
    {
        public List<string> Searches { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/oauth/token")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""{"access_token":"{{ApiToken}}"}"""));
            }

            Assert.Equal($"Bearer {ApiToken}", request.Headers.Authorization?.ToString());
            Searches.Add(request.RequestUri.PathAndQuery);
            var body = request.RequestUri.Query.Contains("cursor_string", StringComparison.Ordinal)
                ? """{"beatmapsets":[],"total":2}"""
                : searchBody;
            return Task.FromResult(Json(searchStatus, body));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private const string TwoSets = """
        {"beatmapsets":[
          {"id":1,"status":"ranked","ranked_date":"2015-03-01T00:00:00Z","beatmaps":[
            {"id":11,"mode":"osu","status":"ranked","difficulty_rating":4.5,"total_length":200},
            {"id":12,"mode":"osu","status":"ranked","difficulty_rating":6.1,"total_length":200}]},
          {"id":2,"status":"ranked","ranked_date":"2010-05-01T00:00:00Z","beatmaps":[
            {"id":21,"mode":"taiko","status":"ranked","difficulty_rating":5.0,"total_length":400}]}],
         "cursor_string":"abc","total":2}
        """;

    [Fact]
    public async Task Reports_what_the_search_returned_and_which_filters_held()
    {
        var osu = new FakeOsu(HttpStatusCode.OK, TwoSets);
        using var http = new HttpClient(osu);

        var report = await new SearchProbe(http, "https://osu.test").RunAsync("1", ClientSecret, CancellationToken.None);

        Assert.Contains("Token (client_credentials, scope public): issued", report, StringComparison.Ordinal);
        Assert.Contains("Difficulties returned: 3, of which fit every limit: 1", report, StringComparison.Ordinal);
        Assert.Contains("Difficulties not in osu! mode: 1", report, StringComparison.Ordinal);
        Assert.Contains("Difficulties outside 4-5.5 stars: 1", report, StringComparison.Ordinal);
        Assert.Contains("Difficulties longer than 300 s: 1", report, StringComparison.Ordinal);
        Assert.Contains("Sets ranked before 2012: 1", report, StringComparison.Ordinal);
        Assert.Contains("Next cursor: present", report, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiToken, report, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, report, StringComparison.Ordinal);

        // First page, the cursor's second page, and two other sort orders.
        Assert.Equal(4, osu.Searches.Count);
        Assert.Contains(osu.Searches, s => s.Contains("cursor_string=abc", StringComparison.Ordinal));
        Assert.Contains(osu.Searches, s => s.Contains("sort=plays_desc", StringComparison.Ordinal));
        Assert.All(osu.Searches, s => Assert.Contains("m=0&s=ranked", s, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refused_search_is_reported_with_its_status_and_body()
    {
        using var http = new HttpClient(new FakeOsu(HttpStatusCode.Unauthorized, """{"authentication":"basic"}"""));

        var report = await new SearchProbe(http, "https://osu.test").RunAsync("1", ClientSecret, CancellationToken.None);

        Assert.Contains("HTTP 401", report, StringComparison.Ordinal);
        Assert.Contains("authentication", report, StringComparison.Ordinal);
    }
}
