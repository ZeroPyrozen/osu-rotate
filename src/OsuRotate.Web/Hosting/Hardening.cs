using System.Net;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace OsuRotate.Web.Hosting;

// Production settings for running behind `tailscale serve` (ADR 0001): Tailscale on the same
// machine is the only proxy, the dashboard's public hostname comes from configuration, and every response carries
// a content security policy.
public static class Hardening
{
    // The names the Pi itself uses: the deploy health check and an SSH port forward.
    public static readonly IReadOnlyList<string> LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    // Blazor Server needs only its own script and a WebSocket to the same origin. No inline script or
    // style is allowed, so nothing on the page may use a style attribute.
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; "
        + "font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static WebApplicationBuilder AddHardening(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var publicHost = PublicHost(builder.Configuration);

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // The proxy adds X-Forwarded-For and X-Forwarded-Proto; the Host header is not forwarded. Only
            // trust them from this machine; anything else could claim to be HTTPS or any address.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Loopback, 8));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.IPv6Loopback, 128));
        });

        // Replaces AllowedHosts from appsettings: loopback plus the one public hostname.
        builder.Services.PostConfigure<HostFilteringOptions>(options =>
        {
            options.AllowedHosts = [.. LoopbackHosts, .. publicHost is null ? [] : new[] { publicHost }];
            options.AllowEmptyHosts = false;
        });

        // The form token cookie is HTTPS-only like the login cookie; development runs on plain HTTP.
        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
        }

        builder.Services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);
        return builder;
    }

    // The dashboard's hostname on the tailnet (Dashboard:Host), without scheme or port.
    public static string? PublicHost(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var host = configuration["Dashboard:Host"]?.Trim();
        return string.IsNullOrEmpty(host) ? null : host;
    }

    // First in the pipeline, so everything after it sees the visitor's address and HTTPS.
    public static WebApplication UseHardening(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseForwardedHeaders();

        if (PublicHost(app.Configuration) is null && !app.Environment.IsDevelopment())
        {
            app.Logger.LogWarning("Dashboard__Host is not set, so the dashboard only answers on localhost. Set it to the Pi's Tailscale name (<machine>.<tailnet>.ts.net).");
        }

        if (!app.Environment.IsDevelopment())
        {
            // Sent only on HTTPS requests, which through the proxy means X-Forwarded-Proto: https.
            app.UseHsts();
        }

        // Added as the response starts, so error pages (whose headers are reset) carry them too.
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                return Task.CompletedTask;
            });
            return next(context);
        });

        return app;
    }
}
