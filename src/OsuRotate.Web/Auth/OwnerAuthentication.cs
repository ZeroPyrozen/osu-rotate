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

    public static IServiceCollection AddOwnerLogin(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var login = OwnerLoginOptions.From(configuration);
        services.AddSingleton(login);

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OsuScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;

                // Cloudflare serves the dashboard over HTTPS; local development runs on plain HTTP.
                options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToAccessDenied = context => Forbidden(context.HttpContext);
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
                            return NotConfigured(context.HttpContext);
                        }

                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnCreatingTicket = context => AddOsuIdentityAsync(context),
                    OnTicketReceived = context =>
                    {
                        if (IsOwner(context.Principal, login))
                        {
                            return Task.CompletedTask;
                        }

                        // No session for anyone else.
                        context.HandleResponse();
                        return Forbidden(context.HttpContext);
                    },
                    OnRemoteFailure = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return context.Response.WriteAsync("The osu! login did not complete. Go back to the dashboard and try again.");
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
            return Results.Redirect("/signed-out");
        }).DisableAntiforgery();

        endpoints.MapGet("/signed-out", () => Results.Text("Signed out of osu-rotate.")).AllowAnonymous();
        return endpoints;
    }

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

    private static Task Forbidden(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync("This dashboard is for its owner only.");
    }

    private static Task NotConfigured(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync("osu! login is not set up. Set Osu__ClientId, Osu__ClientSecret and Dashboard__OwnerUserId, then restart.");
    }
}
