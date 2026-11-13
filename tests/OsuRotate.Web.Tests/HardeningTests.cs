using Microsoft.AspNetCore.Builder;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OsuRotate.Web.Hosting;

namespace OsuRotate.Web.Tests;

// Phase 5, issue 08: the host as it runs on the Pi, behind a proxy on the same machine
// (tailscale serve, ADR 0001).
public sealed partial class HardeningTests : IDisposable
{
    private const string PublicHost = "rotate.example.com";

    private readonly BotFactory _factory = new BotFactory().InProduction().With("Dashboard:Host", PublicHost);

    public void Dispose()
    {
        _factory.Dispose();
        _factory.DeleteFolder();
    }

    private HttpClient Client(string baseAddress) =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri(baseAddress) });

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? forwardedProto = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
            request.Headers.Add("X-Forwarded-For", "198.51.100.7");
        }

        return await client.SendAsync(request);
    }

    private const string SignInButton = "/signin/osu?returnUrl=%2F";

    private static string RedirectUri(HttpResponseMessage challenge) =>
        System.Web.HttpUtility.ParseQueryString(challenge.Headers.Location!.Query)["redirect_uri"]!;

    [Fact]
    public async Task Https_from_the_proxy_on_loopback_is_trusted()
    {
        _factory.RemoteAddress = IPAddress.Loopback;
        var challenge = await GetAsync(Client($"http://{PublicHost}"), SignInButton, forwardedProto: "https");

        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.Equal($"https://{PublicHost}/signin-osu", RedirectUri(challenge));
        Assert.True(challenge.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Ipv6_loopback_is_trusted_too()
    {
        _factory.RemoteAddress = IPAddress.IPv6Loopback;
        var challenge = await GetAsync(Client($"http://{PublicHost}"), SignInButton, forwardedProto: "https");

        Assert.Equal($"https://{PublicHost}/signin-osu", RedirectUri(challenge));
    }

    [Fact]
    public async Task Forwarded_headers_from_anywhere_else_are_ignored()
    {
        _factory.RemoteAddress = IPAddress.Parse("203.0.113.5");
        var challenge = await GetAsync(Client($"http://{PublicHost}"), SignInButton, forwardedProto: "https");

        Assert.Equal($"http://{PublicHost}/signin-osu", RedirectUri(challenge));
        Assert.False(challenge.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public void Only_the_visitor_address_and_scheme_are_forwarded()
    {
        var options = _factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Empty(options.KnownProxies);
        Assert.Equal(["127.0.0.0/8", "::1/128"], options.KnownIPNetworks.Select(n => n.ToString()));
    }

    [Theory]
    [InlineData(PublicHost, HttpStatusCode.OK)]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("127.0.0.1:5180", HttpStatusCode.OK)]
    [InlineData("[::1]:5180", HttpStatusCode.OK)]
    [InlineData("evil.example.com", HttpStatusCode.BadRequest)]
    [InlineData("sub.rotate.example.com", HttpStatusCode.BadRequest)]
    public async Task Only_the_dashboard_host_and_loopback_are_answered(string host, HttpStatusCode expected)
    {
        var response = await GetAsync(Client($"http://{host}"), "/healthz");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_dashboard_host_only_loopback_is_answered()
    {
        using var factory = new BotFactory().InProduction();
        try
        {
            var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{PublicHost}") });
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode);
        }
        finally
        {
            factory.DeleteFolder();
        }
    }

    [Fact]
    public async Task Cookies_are_https_only_even_on_a_plain_http_request()
    {
        var challenge = await GetAsync(Client("http://localhost"), SignInButton);
        var correlation = Assert.Single(challenge.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Correlation", StringComparison.Ordinal));
        Assert.Contains("secure", correlation, StringComparison.OrdinalIgnoreCase);

        var login = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal(CookieSecurePolicy.Always, login.Cookie.SecurePolicy);
        Assert.True(login.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, login.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, _factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.SecurePolicy);
    }

    [Fact]
    public async Task The_owner_gets_secure_cookies()
    {
        var (_, callback) = await _factory.LogInAsync();
        var session = Assert.Single(callback.Headers.GetValues("Set-Cookie"), c => c.StartsWith("osu-rotate=", StringComparison.Ordinal));
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);

        // A persistent cookie, so the login survives closing the browser, for 7 days.
        var expires = System.Text.RegularExpressions.Regex.Match(session, "expires=([^;]+)", RegexOptions.IgnoreCase);
        Assert.True(expires.Success, session);
        var left = DateTimeOffset.Parse(expires.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - DateTimeOffset.UtcNow;
        Assert.InRange(left, TimeSpan.FromDays(7) - TimeSpan.FromMinutes(5), TimeSpan.FromDays(7) + TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/")]
    [InlineData("/signed-out")]
    [InlineData("/no-such-page")]
    public async Task Every_response_carries_the_security_headers(string path)
    {
        var response = await GetAsync(Client("https://localhost"), path);
        Assert.Equal(Hardening.ContentSecurityPolicy, string.Join(",", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", string.Join(",", response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", string.Join(",", response.Headers.GetValues("Referrer-Policy")));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public void The_policy_allows_nothing_inline_and_no_framing()
    {
        Assert.DoesNotContain("unsafe", Hardening.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", Hardening.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", Hardening.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("form-action 'self'", Hardening.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.False(_factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/history")]
    public async Task Pages_have_no_inline_script_or_style_for_the_policy_to_block(string path)
    {
        // A match, so the stats draw their per-day bars.
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        await _factory.Services.GetRequiredService<OsuRotate.Infrastructure.Persistence.SqliteLobbyStore>().SaveAsync(
            7, new OsuRotate.Domain.MatchRecord(1, "alice", null, start, start.AddMinutes(3), OsuRotate.Domain.MatchOutcome.Finished, []), default);
        var (browser, _) = await _factory.LogInAsync();
        var html = await browser.GetStringAsync(new Uri(path, UriKind.Relative));

        Assert.Contains("<script", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"icon\" href=\"data:,\"", html, StringComparison.Ordinal);
        if (path == "/history")
        {
            Assert.Contains("<svg class=\"chart\"", html, StringComparison.Ordinal);
        }
        Assert.All(ScriptTag().Matches(html), tag => Assert.Contains(" src=", tag.Value, StringComparison.Ordinal));
        Assert.DoesNotMatch(StyleAttribute(), html);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
    }

    // Before the circuit connects, an interactive form is plain HTML: submitting it is a GET of the
    // page, which changes nothing. The only form that posts is signing out, and it carries a token.
    [Fact]
    public async Task Rendered_forms_post_only_with_a_token()
    {
        var (browser, _) = await _factory.LogInAsync();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            while (_factory.Runtime.Actor is null)
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        // The lobby page and the rules page, each with its forms and the sign-out form.
        var html = await browser.GetStringAsync(new Uri("/", UriKind.Relative)) + await browser.GetStringAsync(new Uri("/rules", UriKind.Relative));
        var forms = Form().Matches(html).ToList();
        Assert.True(forms.Count >= 6, $"Only {forms.Count} forms rendered.");

        var posting = forms.Where(f => f.Groups["attributes"].Value.Contains("method=", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, posting.Count);
        Assert.All(posting, form =>
        {
            Assert.Contains("method=\"post\" action=\"signout\"", form.Groups["attributes"].Value, StringComparison.Ordinal);
            Assert.Contains("name=\"__RequestVerificationToken\"", form.Value, StringComparison.Ordinal);
        });
    }

    // The test host has no Bancho login, like a Pi whose environment file was never filled in.
    [Fact]
    public async Task A_bot_with_no_bancho_login_says_so_on_the_dashboard()
    {
        var (browser, _) = await _factory.LogInAsync();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            while (_factory.Runtime.Actor is null)
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        Assert.Equal(OsuRotate.Web.Bot.LobbyHostService.NotConfigured, _factory.Runtime.Problem);
        Assert.Contains("The bot has no Bancho login", await browser.GetStringAsync(new Uri("/", UriKind.Relative)), StringComparison.Ordinal);
    }

    // The interactive panels only render once the circuit is up, so this reads their source: a form
    // either submits through the circuit (@onsubmit, no HTTP request) or posts with a token.
    [Fact]
    public void Every_form_posts_with_a_token_or_runs_in_the_circuit()
    {
        var components = Path.Combine(RepositoryRoot(), "src", "OsuRotate.Web", "Components");
        var forms = Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => Form().Matches(File.ReadAllText(file)).Select(form => (File: Path.GetFileName(file), Form: form)))
            .ToList();

        Assert.True(forms.Count >= 7, $"Only {forms.Count} forms found.");
        Assert.All(forms, f =>
        {
            var attributes = f.Form.Groups["attributes"].Value;
            if (attributes.Contains("@onsubmit", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("method=", attributes, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("method=\"post\"", attributes, StringComparison.Ordinal);
                Assert.Contains("<AntiforgeryToken />", f.Form.Value, StringComparison.Ordinal);
            }
        });
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "OsuRotate.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("OsuRotate.slnx not found above the test folder.");
    }

    [Fact]
    public async Task Signing_out_without_the_token_is_refused()
    {
        var (browser, _) = await _factory.LogInAsync();
        var response = await browser.PostAsync(new Uri("/signout", UriKind.Relative), new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public void Detailed_errors_stay_off_in_production_even_when_configured()
    {
        using var factory = new BotFactory().InProduction().With("DetailedErrors", "true");
        try
        {
            Assert.False(factory.Services.GetRequiredService<IOptions<CircuitOptions>>().Value.DetailedErrors);
        }
        finally
        {
            factory.Dispose();
            factory.DeleteFolder();
        }
    }

    [Fact]
    public void Development_keeps_detailed_errors()
    {
        using var factory = new BotFactory();
        try
        {
            Assert.True(factory.Services.GetRequiredService<IOptions<CircuitOptions>>().Value.DetailedErrors);
        }
        finally
        {
            factory.Dispose();
            factory.DeleteFolder();
        }
    }

    // Through Funnel the dashboard is on the public internet (ADR 0002), so this is the whole list of
    // what a stranger can reach without the owner's login. A new page is the owner's by default
    // (the fallback policy); anything added here must be meant for anyone.
    [Fact]
    public void Only_these_routes_are_open_to_anyone()
    {
        var endpoints = _factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .ToList();
        Assert.NotEmpty(endpoints);

        var open = endpoints
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Where(route => !IsStaticFile(route))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["/Error", "/denied", "/healthz", "/not-found", "/signed-out", "/signin", "/signin/osu"], open);
    }

    // Static assets are the app's own CSS, JavaScript and images: files with an extension.
    private static bool IsStaticFile(string route) =>
        Path.HasExtension(route.Split('/')[^1]) && !route.Contains('{', StringComparison.Ordinal);

    [Fact]
    public async Task The_error_page_tells_visitors_nothing_about_the_setup()
    {
        var html = await (await GetAsync(Client("https://localhost"), "/Error")).Content.ReadAsStringAsync();
        Assert.Contains("An error occurred", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Development", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", html, StringComparison.Ordinal);
    }

    [GeneratedRegex("<script[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"<[^>]+\sstyle=", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex("<form(?<attributes>[^>]*)>.*?</form>", RegexOptions.Singleline)]
    private static partial Regex Form();
}
