using System.Reflection;
using System.Runtime.CompilerServices;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;
using OsuRotate.Infrastructure.OsuApi;
using OsuRotate.Infrastructure.Persistence;

namespace OsuRotate.Web.Bot;

// Configuration for the bot. Secrets come from the environment (systemd's EnvironmentFile on the
// Pi: Bancho__Password, Osu__ClientSecret), never from appsettings, and are never logged.
public sealed class BotConfiguration
{
    // The osu! account the bot runs on, which is also the owner. Without it and the IRC password the
    // bot does not connect and the lobby stays Idle.
    public string? Nick { get; init; }
    public string? IrcPassword { get; init; }
    public string IrcHost { get; init; } = "irc.ppy.sh";
    public int IrcPort { get; init; } = 6667;
    public string DatabasePath { get; init; } = "rotate.db";
    public int ApiClientId { get; init; }
    public string? ApiClientSecret { get; init; }

    public bool CanConnect => !string.IsNullOrWhiteSpace(Nick) && !string.IsNullOrEmpty(IrcPassword);

    public static BotConfiguration From(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // systemd sets STATE_DIRECTORY for StateDirectory=; elsewhere the database sits beside the app.
        var folder = Environment.GetEnvironmentVariable("STATE_DIRECTORY") is { Length: > 0 } state
            ? state.Split(':')[0]
            : Path.Combine(environment.ContentRootPath, "data");
        return new BotConfiguration
        {
            Nick = configuration["Bancho:Nick"],
            IrcPassword = configuration["Bancho:Password"],
            IrcHost = configuration["Bancho:Host"] is { Length: > 0 } host ? host : "irc.ppy.sh",
            IrcPort = int.TryParse(configuration["Bancho:Port"], out var port) ? port : 6667,
            DatabasePath = configuration["Bot:DatabasePath"] is { Length: > 0 } path ? path : Path.Combine(folder, "rotate.db"),
            ApiClientId = int.TryParse(configuration["Osu:ClientId"], out var id) ? id : 0,
            ApiClientSecret = configuration["Osu:ClientSecret"],
        };
    }
}

// Runs the lobby for as long as the process runs, apart from any dashboard circuit (design figure 2).
// On start it recovers the saved lobby (figure 4); on stop it leaves the room open, so the next start
// rejoins it.
internal sealed partial class LobbyHostService(
    BotConfiguration configuration,
    LobbyRuntime runtime,
    SqliteLobbyStore store,
    IHttpClientFactory httpClients,
    ILoggerFactory loggers,
    TimeProvider time) : BackgroundService
{
    private readonly ILogger _logger = loggers.CreateLogger<LobbyHostService>();

    public static string Version { get; } = ReadVersion();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var api = new OsuApiClient(
            httpClients.CreateClient("osu"),
            new OsuApiOptions { ClientId = configuration.ApiClientId, ClientSecret = configuration.ApiClientSecret ?? string.Empty },
            loggers.CreateLogger<OsuApiClient>(),
            time);
        var history = new OsuMatchHistory(api);
        var owner = configuration.Nick ?? string.Empty;

        var recovered = await LobbyRecovery.RecoverAsync(
            store,
            history,
            new LobbySettings { OwnerName = owner, BotVersion = Version },
            time,
            stoppingToken).ConfigureAwait(false);

        using var transport = configuration.CanConnect
            ? new BanchoIrcTransport(new BanchoOptions { Host = configuration.IrcHost, Port = configuration.IrcPort, Nick = owner, Password = configuration.IrcPassword! }, loggers.CreateLogger<BanchoIrcTransport>(), time)
            : null;
        if (transport is null)
        {
            LogNotConfigured(_logger);
            runtime.Problem = NotConfigured;
        }

        if (!new OsuApiOptions { ClientId = configuration.ApiClientId, ClientSecret = configuration.ApiClientSecret ?? string.Empty }.IsConfigured)
        {
            LogNoApi(_logger);
        }

        var actor = new LobbyActor(
            recovered.Engine,
            transport ?? (ILobbyTransport)new IdleTransport(),
            new OsuBeatmapCatalog(api, time),
            time,
            new LoggingLobbyDiagnostics(loggers.CreateLogger<LoggingLobbyDiagnostics>()),
            recovered.Lifecycle,
            store,
            store,
            history);
        LogStarted(_logger, Version, recovered.Session?.RoomId.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none", recovered.MatchRunning);

        runtime.Attach(actor);
        try
        {
            await actor.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            runtime.Attach(null);
            LogStopped(_logger);
        }
    }

    public const string NotConfigured =
        "The bot has no Bancho login, so it cannot connect or open a room. Set Bancho__Nick and Bancho__Password in the service's environment file, then restart the service.";

    private static string ReadVersion() =>
        ShortVersion(typeof(LobbyHostService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    // The SDK can append "+<commit>"; the lobby shows the short form.
    internal static string ShortVersion(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bancho__Nick or Bancho__Password is not set, so the bot will not connect and the lobby stays Idle.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Osu__ClientId or Osu__ClientSecret is not set: maps are not checked, and recovery cannot tell whether a match is running.")]
    private static partial void LogNoApi(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "osu-rotate {Version} started. Saved room: {RoomId}. Match running: {MatchRunning}.")]
    private static partial void LogStarted(ILogger logger, string version, string roomId, bool matchRunning);

    [LoggerMessage(Level = LogLevel.Information, Message = "The lobby has stopped. The room is left open for the next start.")]
    private static partial void LogStopped(ILogger logger);

    // Stands in for Bancho when the bot has no credentials: no events, and nothing is sent.
    private sealed class IdleTransport : ILobbyTransport
    {
        public TransportCapabilities Capabilities { get; } = new(CanSetHost: false, HasChat: false);

        public async IAsyncEnumerable<LobbyEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            yield break;
        }

        public ValueTask SendAsync(LobbyCommand command, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}

public static class BotServiceCollectionExtensions
{
    public static IServiceCollection AddBot(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton(BotConfiguration.From(configuration, environment));
        services.AddSingleton<LobbyRuntime>();
        services.AddSingleton<ILobbyStatusReader>(sp => sp.GetRequiredService<LobbyRuntime>());
        services.AddSingleton(TimeProvider.System);

        // One store for the bot and the history pages, so the database is migrated once.
        services.AddSingleton(sp =>
        {
            var bot = sp.GetRequiredService<BotConfiguration>();
            if (Path.GetDirectoryName(Path.GetFullPath(bot.DatabasePath)) is { } folder)
            {
                Directory.CreateDirectory(folder);
            }

            return new SqliteLobbyStore(bot.DatabasePath, sp.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton<IMatchArchiveReader>(sp => sp.GetRequiredService<SqliteLobbyStore>());
        services.AddHttpClient("osu", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddHostedService<LobbyHostService>();
        return services;
    }
}
