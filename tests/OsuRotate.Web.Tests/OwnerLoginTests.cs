using System.Net;
using System.Text.RegularExpressions;

namespace OsuRotate.Web.Tests;

// Phase 5, issue 01: only the owner's osu! account gets a dashboard session (design figure 8).
public sealed partial class OwnerLoginTests : IDisposable
{
    private readonly BotFactory _factory = new();

    public void Dispose()
    {
        _factory.Dispose();
        _factory.DeleteFolder();
    }

    private static bool SetsSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(c => c.StartsWith("osu-rotate=", StringComparison.Ordinal) && !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task A_visitor_is_sent_to_osu_to_log_in_with_only_the_identify_scope()
    {
        var response = await _factory.Browser().GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https://osu.ppy.sh/oauth/authorize", location.GetLeftPart(UriPartial.Path));
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("identify", query["scope"]);
        Assert.Equal("7", query["client_id"]);
        Assert.Equal("https://localhost/signin-osu", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
    }

    [Fact]
    public async Task The_owner_gets_in_and_stays_in()
    {
        var (browser, callback) = await _factory.LogInAsync();

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location?.OriginalString);
        Assert.True(SetsSessionCookie(callback));

        var page = await browser.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Signed in as <strong>owner</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_session_cookie_is_http_only_and_lax()
    {
        var (_, callback) = await _factory.LogInAsync();
        var cookie = callback.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("osu-rotate=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Anyone_else_gets_403_and_no_session()
    {
        var (browser, callback) = await _factory.LogInAsync(userId: 1);

        Assert.Equal(HttpStatusCode.Forbidden, callback.StatusCode);
        Assert.False(SetsSessionCookie(callback));
        Assert.Equal("This dashboard is for its owner only.", await callback.Content.ReadAsStringAsync());

        // Still not logged in: the dashboard sends them to osu! again.
        var page = await browser.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }

    [Fact]
    public async Task The_osu_token_is_only_used_to_ask_who_logged_in()
    {
        await _factory.LogInAsync();

        Assert.Equal(["POST /oauth/token", "GET /api/v2/me"], _factory.Osu.Requests.Where(r => r.Contains("/oauth/", StringComparison.Ordinal) || r.Contains("/me", StringComparison.Ordinal)));
        var body = Assert.Single(_factory.Osu.TokenBodies);
        Assert.Contains("grant_type=authorization_code", body, StringComparison.Ordinal);
        Assert.Contains("code=the-code", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_session_keeps_who_logged_in_but_no_osu_token()
    {
        var (_, callback) = await _factory.LogInAsync();
        var cookie = callback.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("osu-rotate=", StringComparison.Ordinal));
        var value = Uri.UnescapeDataString(cookie.Split(';')[0]["osu-rotate=".Length..]);

        var options = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>(_factory.Services)
            .Get(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Unprotect(value)!;

        Assert.Equal(FakeOsu.OwnerId.ToString(System.Globalization.CultureInfo.InvariantCulture), ticket.Principal.FindFirst(Auth.OwnerAuthentication.OsuIdClaim)?.Value);
        Assert.Empty(Microsoft.AspNetCore.Authentication.AuthenticationTokenExtensions.GetTokens(ticket.Properties));
    }

    [Fact]
    public async Task A_forged_state_is_refused()
    {
        var browser = _factory.Browser();
        await browser.GetAsync(new Uri("/", UriKind.Relative));
        var callback = await browser.GetAsync(new Uri("/signin-osu?code=x&state=forged", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        Assert.False(SetsSessionCookie(callback));
        Assert.DoesNotContain(_factory.Osu.Requests, r => r.Contains("/oauth/token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_callback_from_another_browser_is_refused()
    {
        // The state is real, but this browser never started the login, so it has no correlation cookie.
        var challenge = await _factory.Browser().GetAsync(new Uri("/", UriKind.Relative));
        var state = BotFactory.StateFrom(challenge);
        var callback = await _factory.Browser().GetAsync(new Uri($"/signin-osu?code=x&state={Uri.EscapeDataString(state)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        Assert.False(SetsSessionCookie(callback));
    }

    [Fact]
    public async Task A_replayed_callback_does_not_log_in_again()
    {
        _factory.Osu.MeId = FakeOsu.OwnerId;
        var browser = _factory.Browser();
        var challenge = await browser.GetAsync(new Uri("/", UriKind.Relative));
        var callbackUri = new Uri($"/signin-osu?code=the-code&state={Uri.EscapeDataString(BotFactory.StateFrom(challenge))}", UriKind.Relative);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(callbackUri)).StatusCode);

        var replay = await _factory.Browser().GetAsync(callbackUri);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.False(SetsSessionCookie(replay));
    }

    [Fact]
    public async Task A_failed_token_exchange_is_refused()
    {
        _factory.Osu.FailToken = true;
        var (_, callback) = await _factory.LogInAsync();

        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        Assert.False(SetsSessionCookie(callback));
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var (browser, _) = await _factory.LogInAsync();
        var html = await browser.GetStringAsync(new Uri("/", UriKind.Relative));
        var token = AntiforgeryToken().Match(html).Groups[1].Value;

        var signOut = await browser.PostAsync(
            new Uri("/signout", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, signOut.StatusCode);
        Assert.Equal("/signed-out", signOut.Headers.Location?.OriginalString);

        var page = await browser.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("osu.ppy.sh", page.Headers.Location?.Host);
    }

    [Fact]
    public async Task Signing_out_needs_the_antiforgery_token()
    {
        var (browser, _) = await _factory.LogInAsync();
        var signOut = await browser.PostAsync(new Uri("/signout", UriKind.Relative), new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task The_health_check_and_signed_out_page_need_no_login()
    {
        var browser = _factory.Browser();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/signed-out", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Pages_other_than_home_also_need_the_owner()
    {
        var response = await _factory.Browser().GetAsync(new Uri("/anything-else", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("osu.ppy.sh", response.Headers.Location?.Host);
    }

    [Theory]
    [InlineData("Dashboard:OwnerUserId", "")]
    [InlineData("Osu:ClientId", "")]
    [InlineData("Osu:ClientSecret", "")]
    public async Task Without_the_owner_or_the_oauth_app_nobody_can_log_in(string key, string value)
    {
        using var factory = new BotFactory().With(key, value);
        try
        {
            var response = await factory.Browser().GetAsync(new Uri("/", UriKind.Relative));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("Dashboard__OwnerUserId", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            factory.DeleteFolder();
        }
    }

    // The same test guards every request, so a session from before an owner change stops working.
    [Theory]
    [InlineData("4242", true, 4242, true)]
    [InlineData("4243", true, 4242, false)]
    [InlineData(null, true, 4242, false)]
    [InlineData("4242", false, 4242, false)]
    [InlineData("0", true, 0, false)]
    public void Only_a_logged_in_user_with_the_owners_id_is_the_owner(string? osuId, bool authenticated, int owner, bool expected)
    {
        var claims = osuId is null ? [] : new[] { new System.Security.Claims.Claim(Auth.OwnerAuthentication.OsuIdClaim, osuId) };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, authenticated ? "test" : null);
        var login = new Auth.OwnerLoginOptions { OwnerUserId = owner, ClientId = "7", ClientSecret = "s" };

        Assert.Equal(expected, Auth.OwnerAuthentication.IsOwner(new System.Security.Claims.ClaimsPrincipal(identity), login));
        Assert.False(Auth.OwnerAuthentication.IsOwner(null, login));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
