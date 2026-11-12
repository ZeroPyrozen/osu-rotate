using Microsoft.AspNetCore.Builder;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Persistence;
using OsuRotate.Web.Bot;

namespace OsuRotate.Web.Tests;

// The web host with the bot in it, on a throwaway database. IRC credentials are cleared unless a test
// sets them, and every call to osu.ppy.sh goes to FakeOsu, so nothing here reaches the real services.
public sealed class BotFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string> _settings = new()
    {
        ["Bancho:Nick"] = string.Empty,
        ["Bancho:Password"] = string.Empty,
        ["Osu:ClientId"] = "7",
        ["Osu:ClientSecret"] = "app-secret",
        ["Dashboard:OwnerUserId"] = FakeOsu.OwnerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public FakeOsu Osu { get; } = new();

    public BotFactory()
    {
        Folder = Path.Combine(Path.GetTempPath(), "osu-rotate-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        _settings["Bot:DatabasePath"] = DatabasePath;
    }

    public string Folder { get; }

    public string DatabasePath => Path.Combine(Folder, "rotate.db");

    public BotFactory With(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    public LobbyRuntime Runtime => Services.GetRequiredService<LobbyRuntime>();

    // Runs the host as on the Pi instead of in Development.
    public BotFactory InProduction()
    {
        _environment = "Production";
        return this;
    }

    private string _environment = "Development";

    // The address requests come from; TestServer has none. Loopback is cloudflared on the Pi.
    public IPAddress? RemoteAddress { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.UseEnvironment(_environment);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new RemoteAddressFilter(this));
            services.PostConfigure<OAuthOptions>(Auth.OwnerAuthentication.OsuScheme, options => options.Backchannel = new HttpClient(Osu, disposeHandler: false));
            services.AddHttpClient("osu").ConfigurePrimaryHttpMessageHandler(() => Osu);
        });
    }

    private sealed class RemoteAddressFilter(BotFactory factory) : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = factory.RemoteAddress;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    // A browser on https://localhost that does not follow redirects.
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    // Goes through osu!'s login as the given user. Returns the browser and the callback's response.
    public async Task<(HttpClient Browser, HttpResponseMessage Callback)> LogInAsync(int userId = FakeOsu.OwnerId, string returnPath = "/")
    {
        Osu.MeId = userId;
        var browser = Browser();
        var challenge = await browser.GetAsync(new Uri(returnPath, UriKind.Relative));
        var state = StateFrom(challenge);
        var callback = await browser.GetAsync(new Uri($"/signin-osu?code=the-code&state={Uri.EscapeDataString(state)}", UriKind.Relative));
        return (browser, callback);
    }

    public static string StateFrom(HttpResponseMessage challenge)
    {
        var location = challenge.Headers.Location ?? throw new InvalidOperationException($"No redirect: {challenge.StatusCode}");
        return System.Web.HttpUtility.ParseQueryString(location.Query)["state"] ?? throw new InvalidOperationException("No state.");
    }

    // Separate from Dispose, so a test can stop the host and still read the database.
    public void DeleteFolder()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clean up.
        }
    }
}

public sealed class HostTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly BotFactory _factory = new();

    public void Dispose()
    {
        _factory.Dispose();
        _factory.DeleteFolder();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _factory.CreateClient().GetAsync(new Uri("/healthz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Home_page_shows_status_from_the_inner_layers()
    {
        var (browser, _) = await _factory.LogInAsync();
        var html = await browser.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("Lobby status: <strong>Idle</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nothing_saved_and_no_credentials_the_bot_starts_idle()
    {
        _factory.CreateClient();
        await WaitUntilAsync(() => _factory.Runtime.Actor is not null);

        var snapshot = _factory.Runtime.Snapshot!;
        Assert.Equal(LobbyStatus.Idle, snapshot.Status);
        Assert.Null(snapshot.RoomId);
        Assert.Equal(LobbyHostService.Version, snapshot.Settings.BotVersion);
        Assert.NotEqual("0.0.0", LobbyHostService.Version);
        Assert.DoesNotContain('+', LobbyHostService.Version);
        Assert.True(File.Exists(_factory.DatabasePath));
    }

    [Fact]
    public async Task A_saved_room_is_recovered_from_the_database()
    {
        await new SqliteLobbyStore(_factory.DatabasePath).SaveAsync(
            new SavedLobby(new SavedSession(77, "saved room", ["p1"], "p1"), ["denied"], new LobbySettings { OwnerName = "old", AfkKickThreshold = 3 }),
            default);
        SqliteConnection.ClearAllPools();

        _factory.CreateClient();
        await WaitUntilAsync(() => _factory.Runtime.Actor is not null);

        // With no credentials it can only wait to connect, but it knows the room and the rules.
        var snapshot = _factory.Runtime.Snapshot!;
        Assert.Equal(LobbyStatus.Connecting, snapshot.Status);
        Assert.Equal(77, snapshot.RoomId);
        Assert.Equal("saved room", snapshot.Title);
        Assert.Equal(3, snapshot.Settings.AfkKickThreshold);
        Assert.Equal(["denied"], snapshot.Denied);
    }

    [Fact]
    public async Task Owner_actions_reach_the_running_lobby()
    {
        _factory.CreateClient();
        await WaitUntilAsync(() => _factory.Runtime.Actor is not null);

        Assert.True(await _factory.Runtime.PostAsync(new DenyPlayer("someone"), default));
        await WaitUntilAsync(() => _factory.Runtime.Snapshot!.Denied.Contains("someone"));
    }

    [Fact]
    public async Task Stopping_the_host_stops_the_lobby_and_keeps_what_was_saved()
    {
        _factory.CreateClient();
        await WaitUntilAsync(() => _factory.Runtime.Actor is not null);
        await _factory.Runtime.PostAsync(new DenyPlayer("someone"), default);
        await WaitUntilAsync(() => _factory.Runtime.Snapshot!.Denied.Contains("someone"));
        var runtime = _factory.Runtime;

        var stopping = Task.Run(() => _factory.Dispose());
        await stopping.WaitAsync(Timeout);

        Assert.Null(runtime.Actor);
        Assert.Equal(LobbyStatus.Idle, runtime.Current);
        Assert.False(await runtime.PostAsync(new SkipHost(), default));
        Assert.Equal(["someone"], (await new SqliteLobbyStore(_factory.DatabasePath).LoadAsync(default)).Denied);
    }

    [Fact]
    public async Task With_credentials_but_bancho_unreachable_it_keeps_trying_and_still_stops_quickly()
    {
        // A port nothing listens on.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        _factory.With("Bancho:Nick", "creator").With("Bancho:Password", "secret")
            .With("Bancho:Host", "127.0.0.1").With("Bancho:Port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _factory.CreateClient();
        await WaitUntilAsync(() => _factory.Runtime.Actor is not null);
        await Task.Delay(200);
        Assert.Equal(LobbyStatus.Idle, _factory.Runtime.Current);

        await Task.Run(() => _factory.Dispose()).WaitAsync(Timeout);
    }

    [Fact]
    public void The_database_defaults_to_systemds_state_directory()
    {
        var environment = new FakeEnvironment();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        Environment.SetEnvironmentVariable("STATE_DIRECTORY", "/var/lib/osu-rotate");
        try
        {
            Assert.Equal(Path.Combine("/var/lib/osu-rotate", "rotate.db"), BotConfiguration.From(configuration, environment).DatabasePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STATE_DIRECTORY", null);
        }

        Assert.Equal(Path.Combine(environment.ContentRootPath, "data", "rotate.db"), BotConfiguration.From(configuration, environment).DatabasePath);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+4f2a9c1", "1.2.3")]
    [InlineData("1.2.3-beta.1+4f2a9c1", "1.2.3-beta.1")]
    public void The_version_drops_the_commit(string informational, string expected)
    {
        Assert.Equal(expected, LobbyHostService.ShortVersion(informational));
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("creator", null, false)]
    [InlineData("", "secret", false)]
    [InlineData("creator", "secret", true)]
    public void The_bot_connects_only_with_both_nick_and_password(string? nick, string? password, bool expected)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bancho:Nick"] = nick, ["Bancho:Password"] = password })
            .Build();
        Assert.Equal(expected, BotConfiguration.From(configuration, new FakeEnvironment()).CanConnect);
    }

    private sealed class FakeEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "osu-rotate";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "content");
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
