using Microsoft.AspNetCore.DataProtection;
using OsuRotate.Infrastructure;
using OsuRotate.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Tells systemd when Kestrel is ready (Type=notify) and logs in journald's format.
builder.Services.AddSystemd();
builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("osu-rotate")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// No HTTPS redirection or HSTS here: Kestrel is on loopback and Cloudflare terminates TLS.
app.UseAntiforgery();

app.MapHealthChecks("/healthz");
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
