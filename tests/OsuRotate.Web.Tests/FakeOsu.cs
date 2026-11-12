using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace OsuRotate.Web.Tests;

// osu.ppy.sh for the login's back channel: the token exchange and /api/v2/me. MeId is who the next
// login turns out to be. Anything else (the bot's API calls) gets 404, so tests never reach osu!.
public sealed class FakeOsu : HttpMessageHandler
{
    public const int OwnerId = 4242;

    public int MeId { get; set; } = OwnerId;

    public string MeName { get; set; } = "owner";

    public bool FailToken { get; set; }

    public ConcurrentQueue<string> Requests { get; } = new();

    public ConcurrentQueue<string> TokenBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Enqueue($"{request.Method} {path}");
        if (path == "/oauth/token")
        {
            TokenBodies.Enqueue(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return FailToken
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                : Json("""{"access_token":"user-token","token_type":"Bearer","expires_in":86400}""");
        }

        if (path == "/api/v2/me" && request.Headers.Authorization?.Parameter == "user-token")
        {
            return Json($$"""{"id":{{MeId}},"username":"{{MeName}}"}""");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
