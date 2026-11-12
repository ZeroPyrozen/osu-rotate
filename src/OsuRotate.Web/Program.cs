using Microsoft.AspNetCore.DataProtection;
using OsuRotate.Infrastructure;
using OsuRotate.Web.Auth;
using OsuRotate.Web.Bot;
using OsuRotate.Web.Components;
using OsuRotate.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Tells systemd when Kestrel is ready (Type=notify) and logs in journald's format.
builder.Services.AddSystemd();
builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure();
builder.Services.AddBot(builder.Configuration, builder.Environment);
builder.Services.AddOwnerLogin(builder.Configuration, builder.Environment);
builder.AddHardening();
builder.Services.AddRazorComponents()
    // Exception details reach the browser only in development, whatever DetailedErrors says.
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("osu-rotate")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

var app = builder.Build();

app.UseHardening();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// No HTTPS redirection: Kestrel is on loopback and Cloudflare terminates TLS (and redirects HTTP).
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Everything else needs the owner (the fallback policy in AddOwnerLogin).
app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapStaticAssets().AllowAnonymous();
app.MapOwnerLogin();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
