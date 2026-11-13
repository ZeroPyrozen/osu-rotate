using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;

namespace OsuRotate.Web.Auth;

// Who may use the dashboard, from configuration: the osu! OAuth app (the same one the bot uses for the
// API) and the owner's osu! user id. Without all three, nobody can log in.
public sealed class OwnerLoginOptions
{
    public int OwnerUserId { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string OsuBaseUrl { get; init; } = "https://osu.ppy.sh";

    public bool IsConfigured => OwnerUserId > 0 && ClientId.Length > 0 && ClientSecret.Length > 0;

    public static OwnerLoginOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new OwnerLoginOptions
        {
            OwnerUserId = int.TryParse(configuration["Dashboard:OwnerUserId"], NumberStyles.None, CultureInfo.InvariantCulture, out var owner) ? owner : 0,
            ClientId = configuration["Osu:ClientId"] ?? string.Empty,
            ClientSecret = configuration["Osu:ClientSecret"] ?? string.Empty,
        };
    }
}

// Design figure 8: osu!'s login proves who someone is, and the check against the owner's id is the
// access control. Anyone else gets 403 and no session; the osu! token is used once and dropped.
public static partial class OwnerAuthentication
{
    public const string OsuScheme = "osu";
    public const string OwnerPolicy = "Owner";
    public const string CookieName = "osu-rotate";
    public const string CallbackPath = "/signin-osu";
    public const string OsuIdClaim = "osu:id";

    // The pages around the login (Components/Pages/Account): anyone may open them.
    public const string SignInPath = "/signin";
    public const string StartOsuLoginPath = "/signin/osu";
    public const string DeniedPath = "/denied";
    public const string SignedOutPath = "/signed-out";

    public static IServiceCollection AddOwnerLogin(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var login = OwnerLoginOptions.From(configuration);
        services.AddSingleton(login);

        // A visitor without a session is sent to the sign-in page, which starts the osu! login.
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;

                // tailscale serve gives the dashboard HTTPS; local development runs on plain HTTP.
                options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                options.LoginPath = SignInPath;
                options.AccessDeniedPath = DeniedPath;
            })
            .AddOAuth(OsuScheme, options =>
            {
                // Placeholders keep startup working when the app is not configured; logging in is refused.
                options.ClientId = login.ClientId.Length > 0 ? login.ClientId : "not-configured";
                options.ClientSecret = login.ClientSecret.Length > 0 ? login.ClientSecret : "not-configured";
                options.CallbackPath = CallbackPath;
                options.AuthorizationEndpoint = $"{login.OsuBaseUrl}/oauth/authorize";
                options.TokenEndpoint = $"{login.OsuBaseUrl}/oauth/token";
                options.UserInformationEndpoint = $"{login.OsuBaseUrl}/api/v2/me";
                options.Scope.Clear();
                options.Scope.Add("identify");
                options.SaveTokens = false;
                options.CorrelationCookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.Events = new OAuthEvents
                {
                    OnRedirectToAuthorizationEndpoint = context =>
                    {
                        if (!login.IsConfigured)
                        {
                            // The sign-in page says what is missing.
                            context.Response.Redirect(SignInPath);
                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnCreatingTicket = context => AddOsuIdentityAsync(context),
                    OnTicketReceived = context =>
                    {
                        if (IsOwner(context.Principal, login))
                        {
                            // Survives closing the browser: the cookie lasts ExpireTimeSpan (7 days),
                            // renewed while the owner keeps using the dashboard.
                            context.Properties!.IsPersistent = true;
                            return Task.CompletedTask;
                        }

                        // No session for anyone else.
                        context.HandleResponse();
                        context.Response.Redirect(DeniedPath);
                        return Task.CompletedTask;
                    },
                    OnRemoteFailure = context =>
                    {
                        // A forged, replayed or failed callback: no session, and the sign-in page says so.
                        context.HandleResponse();
                        context.Response.Redirect($"{SignInPath}?failed=1");
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(OwnerPolicy, policy => policy.RequireAuthenticatedUser().RequireAssertion(context => IsOwner(context.User, login)))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => IsOwner(context.User, login)).Build());
        services.AddCascadingAuthenticationState();
        return services;
    }

    public static IEndpointRouteBuilder MapOwnerLogin(this IEndpointRouteBuilder endpoints)
    {
        // A form post with an antiforgery token, so another site cannot sign the owner out.
        endpoints.MapPost("/signout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context).ConfigureAwait(false))
            {
                return Results.BadRequest();
            }

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            return Results.Redirect(SignedOutPath);
        }).DisableAntiforgery();

        // The sign-in page's button. Only a path on this site is kept to come back to, so the login
        // can't be used to send someone elsewhere.
        endpoints.MapGet(StartOsuLoginPath, (string? returnUrl) =>
            Results.Challenge(new AuthenticationProperties { RedirectUri = LocalPath(returnUrl) }, [OsuScheme])).AllowAnonymous();
        return endpoints;
    }

    // A path on this site to return to after logging in, or "/" for anything else.
    public static string LocalPath(string? returnUrl) =>
        returnUrl is { Length: > 0 } url
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Contains("://", StringComparison.Ordinal)
        && !url.Any(char.IsControl)
            ? url
            : "/";

    public static bool IsOwner(ClaimsPrincipal? user, OwnerLoginOptions login) =>
        login.OwnerUserId > 0
        && user?.Identity?.IsAuthenticated == true
        && user.FindFirst(OsuIdClaim)?.Value == login.OwnerUserId.ToString(CultureInfo.InvariantCulture);

    private static async Task AddOsuIdentityAsync(OAuthCreatingTicketContext context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted).ConfigureAwait(false));
        var id = me.RootElement.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
        var name = me.RootElement.TryGetProperty("username", out var username) ? username.GetString() ?? id : id;
        context.Identity?.AddClaim(new Claim(OsuIdClaim, id));
        context.Identity?.AddClaim(new Claim(ClaimTypes.Name, name));
    }
}
