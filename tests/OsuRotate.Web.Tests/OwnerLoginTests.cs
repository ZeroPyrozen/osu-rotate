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

    private static void AssertSentToSignIn(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin", new Uri(new Uri("https://localhost"), response.Headers.Location!).AbsolutePath);
    }

    private static void AssertBackOnSignInWithAFailure(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin?failed=1", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_visitor_is_sent_to_the_sign_in_page_which_offers_osu()
    {
        var browser = _factory.Browser();
        var response = await browser.GetAsync(new Uri("/history", UriKind.Relative));
        AssertSentToSignIn(response);
        Assert.Contains("ReturnUrl=%2Fhistory", response.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var page = await browser.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<h1>Log in</h1>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/signin/osu?returnUrl=%2Fhistory\" data-enhance-nav=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("Log in with osu!", html, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor-error-ui", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sign_in_button_goes_to_osu_with_only_the_identify_scope()
    {
        var response = await _factory.Browser().GetAsync(BotFactory.SignInButton());

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

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/denied", callback.Headers.Location?.OriginalString);
        Assert.False(SetsSessionCookie(callback));

        var denied = await browser.GetAsync(new Uri("/denied", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("This dashboard is for its owner only.", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Still not logged in: the dashboard sends them to the sign-in page again.
        AssertSentToSignIn(await browser.GetAsync(new Uri("/", UriKind.Relative)));
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
        await browser.GetAsync(BotFactory.SignInButton());
        var callback = await browser.GetAsync(new Uri("/signin-osu?code=x&state=forged", UriKind.Relative));

        AssertBackOnSignInWithAFailure(callback);
        Assert.False(SetsSessionCookie(callback));
        Assert.DoesNotContain(_factory.Osu.Requests, r => r.Contains("/oauth/token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_callback_from_another_browser_is_refused()
    {
        // The state is real, but this browser never started the login, so it has no correlation cookie.
        var challenge = await _factory.Browser().GetAsync(BotFactory.SignInButton());
        var state = BotFactory.StateFrom(challenge);
        var callback = await _factory.Browser().GetAsync(new Uri($"/signin-osu?code=x&state={Uri.EscapeDataString(state)}", UriKind.Relative));

        AssertBackOnSignInWithAFailure(callback);
        Assert.False(SetsSessionCookie(callback));
    }

    [Fact]
    public async Task A_replayed_callback_does_not_log_in_again()
    {
        _factory.Osu.MeId = FakeOsu.OwnerId;
        var browser = _factory.Browser();
        var challenge = await browser.GetAsync(BotFactory.SignInButton());
        var callbackUri = new Uri($"/signin-osu?code=the-code&state={Uri.EscapeDataString(BotFactory.StateFrom(challenge))}", UriKind.Relative);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(callbackUri)).StatusCode);

        var replay = await _factory.Browser().GetAsync(callbackUri);
        AssertBackOnSignInWithAFailure(replay);
        Assert.False(SetsSessionCookie(replay));
    }

    [Fact]
    public async Task A_failed_token_exchange_is_refused()
    {
        _factory.Osu.FailToken = true;
        var (browser, callback) = await _factory.LogInAsync();

        AssertBackOnSignInWithAFailure(callback);
        Assert.False(SetsSessionCookie(callback));
        var page = await browser.GetStringAsync(callback.Headers.Location);
        Assert.Contains("The osu! login did not complete. Try again.", page, StringComparison.Ordinal);
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

        var signedOut = await browser.GetStringAsync(new Uri("/signed-out", UriKind.Relative));
        Assert.Contains("<h1>Signed out</h1>", signedOut, StringComparison.Ordinal);
        Assert.Contains("href=\"/signin\"", signedOut, StringComparison.Ordinal);

        AssertSentToSignIn(await browser.GetAsync(new Uri("/", UriKind.Relative)));
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
    public async Task The_health_check_and_the_account_pages_need_no_login()
    {
        var browser = _factory.Browser();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/signed-out", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/signin", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/denied", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task The_owner_on_the_sign_in_page_goes_straight_on()
    {
        var (browser, _) = await _factory.LogInAsync();
        var page = await browser.GetAsync(new Uri("/signin?ReturnUrl=%2Fhistory", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.EndsWith("/history", page.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/history", "/history")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example.com/", "/")]
    [InlineData("//evil.example.com/", "/")]
    [InlineData("/\\evil.example.com/", "/")]
    [InlineData("history", "/")]
    [InlineData("/x?next=https://evil.example.com", "/")]
    public void Only_a_path_on_this_site_is_kept_to_return_to(string? returnUrl, string expected) =>
        Assert.Equal(expected, Auth.OwnerAuthentication.LocalPath(returnUrl));

    [Fact]
    public async Task The_sign_in_button_never_returns_to_another_site()
    {
        _factory.Osu.MeId = FakeOsu.OwnerId;
        var browser = _factory.Browser();
        var challenge = await browser.GetAsync(new Uri("/signin/osu?returnUrl=https%3A%2F%2Fevil.example.com%2F", UriKind.Relative));
        var callback = await browser.GetAsync(new Uri($"/signin-osu?code=the-code&state={Uri.EscapeDataString(BotFactory.StateFrom(challenge))}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Pages_other_than_home_also_need_the_owner()
    {
        AssertSentToSignIn(await _factory.Browser().GetAsync(new Uri("/anything-else", UriKind.Relative)));
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
            var browser = factory.Browser();
            var response = await browser.GetAsync(new Uri("/signin", UriKind.Relative));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Dashboard__OwnerUserId", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Log in with osu!", html, StringComparison.Ordinal);

            // The button's address can't reach osu! either.
            var button = await browser.GetAsync(BotFactory.SignInButton());
            Assert.Equal("/signin", button.Headers.Location?.OriginalString);
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
