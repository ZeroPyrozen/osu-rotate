using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Persistence;

namespace OsuRotate.Infrastructure.Tests;

public sealed class SqliteLobbyStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "osu-rotate-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public SqliteLobbyStoreTests() => Directory.CreateDirectory(_folder);

    private string DatabasePath => Path.Combine(_folder, "rotate.db");

    private SqliteLobbyStore Open() => new(DatabasePath, _time);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clean up.
        }
    }

    private static readonly LobbySettings Settings = new()
    {
        OwnerName = "creator",
        AfkKickThreshold = 9,
        SkipVote = new VoteRule(0.6, 3),
        HostTransferTimeout = TimeSpan.FromSeconds(7),
        Regulation = new MapRegulation { StarMin = 4, StarMax = 6.5, Mode = GameMode.Taiko, AllowConvert = false },
        Keeping = new RoomKeeping
        {
            Mode = new KeptMode(TeamMode.TeamVs, ScoreMode.ScoreV2),
            Size = 8,
            Mods = Mods.Freemod | Mods.DoubleTime,
            Password = "pw",
            Title = "my room",
        },
    };

    private static SavedLobby Lobby(long roomId = 7, string? host = "p1", params string[] queue) =>
        new(new SavedSession(roomId, "my room", queue.Length == 0 ? ["p1", "p2", "p 3"] : queue, host), ["denied1", "denied2"], Settings);

    [Fact]
    public async Task A_new_database_loads_empty()
    {
        var loaded = await Open().LoadAsync(default);
        Assert.Equal(SavedLobby.Empty, loaded);
        Assert.True(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Round_trips_a_session_queue_settings_and_deny_list()
    {
        var lobby = Lobby();
        await Open().SaveAsync(lobby, default);

        var loaded = await Open().LoadAsync(default);
        Assert.Equal(lobby, loaded);
        Assert.Equal(Settings, loaded.Settings);
        Assert.Equal(["p1", "p2", "p 3"], loaded.Session!.Queue);
    }

    [Fact]
    public async Task Survives_the_process_going_away()
    {
        await Open().SaveAsync(Lobby(), default);
        SqliteConnection.ClearAllPools();

        Assert.Equal(Lobby(), await Open().LoadAsync(default));
    }

    [Fact]
    public async Task Later_saves_replace_the_queue_host_and_deny_list()
    {
        var store = Open();
        await store.SaveAsync(Lobby(), default);
        var changed = new SavedLobby(new SavedSession(7, "renamed", ["p2", "p4"], "p2"), ["denied2", "denied3"], Settings with { AfkKickThreshold = 3 });
        await store.SaveAsync(changed, default);

        Assert.Equal(changed, await Open().LoadAsync(default));
    }

    [Fact]
    public async Task An_empty_queue_and_no_host_are_saved()
    {
        var store = Open();
        await store.SaveAsync(Lobby(), default);
        var empty = new SavedLobby(new SavedSession(7, "my room", [], null), [], Settings);
        await store.SaveAsync(empty, default);

        Assert.Equal(empty, await store.LoadAsync(default));
    }

    [Fact]
    public async Task No_session_closes_the_saved_one()
    {
        var store = Open();
        await store.SaveAsync(Lobby(), default);
        await store.SaveAsync(new SavedLobby(null, ["denied1"], Settings), default);

        var loaded = await store.LoadAsync(default);
        Assert.Null(loaded.Session);
        Assert.Equal(["denied1"], loaded.Denied);
        Assert.Equal(Settings, loaded.Settings);
    }

    [Fact]
    public async Task A_new_room_closes_the_old_session_and_starts_its_own_queue()
    {
        var store = Open();
        await store.SaveAsync(Lobby(7), default);
        await store.SaveAsync(Lobby(8, "p9", "p9"), default);

        var loaded = await store.LoadAsync(default);
        Assert.Equal(8, loaded.Session!.RoomId);
        Assert.Equal(["p9"], loaded.Session.Queue);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM LobbySession WHERE ClosedAt IS NULL"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM LobbySession"));
    }

    [Fact]
    public async Task Coming_back_to_a_room_after_closing_it_starts_a_new_session()
    {
        var store = Open();
        await store.SaveAsync(Lobby(7), default);
        await store.SaveAsync(new SavedLobby(null, [], Settings), default);
        await store.SaveAsync(Lobby(7, "p1", "p1"), default);

        Assert.Equal(["p1"], (await store.LoadAsync(default)).Session!.Queue);
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM LobbySession"));
    }

    [Fact]
    public async Task Sessions_record_when_they_started_and_closed()
    {
        var store = Open();
        await store.SaveAsync(Lobby(7), default);
        _time.Advance(TimeSpan.FromHours(3));
        await store.SaveAsync(new SavedLobby(null, [], Settings), default);

        await using var connection = Connect();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT StartedAt, ClosedAt FROM LobbySession";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.Parse(reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero), DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Settings_left_out_keep_what_was_saved()
    {
        var store = Open();
        await store.SaveAsync(Lobby(), default);
        await store.SaveAsync(new SavedLobby(null, [], null), default);

        Assert.Equal(Settings, (await store.LoadAsync(default)).Settings);
    }

    [Fact]
    public async Task Unreadable_settings_load_as_none_and_the_rest_still_loads()
    {
        var store = Open();
        await store.SaveAsync(Lobby(), default);
        await ExecuteAsync("UPDATE LobbyConfig SET SettingsJson = '{\"AfkKickThreshold\": \"many\"}'");

        var loaded = await store.LoadAsync(default);
        Assert.Null(loaded.Settings);
        Assert.Equal(7, loaded.Session!.RoomId);
    }

    [Fact]
    public async Task The_database_is_in_wal_mode()
    {
        await Open().LoadAsync(default);
        await using var connection = Connect();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", (string?)await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task A_database_made_elsewhere_is_switched_to_wal()
    {
        await ExecuteAsync("PRAGMA journal_mode=DELETE;");
        Assert.Equal("delete", await JournalModeAsync());

        await Open().LoadAsync(default);
        Assert.Equal("wal", await JournalModeAsync());
    }

    [Fact]
    public async Task Settings_are_stored_with_enum_names()
    {
        await Open().SaveAsync(Lobby(), default);
        await using var connection = Connect();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SettingsJson FROM LobbyConfig";
        var json = (string)(await command.ExecuteScalarAsync())!;

        Assert.Contains("\"Taiko\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TeamVs\"", json, StringComparison.Ordinal);
        Assert.Contains("Freemod", json, StringComparison.Ordinal);
    }

    private async Task<string?> JournalModeAsync()
    {
        await using var connection = Connect();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return (string?)await command.ExecuteScalarAsync();
    }

    [Fact]
    public async Task A_failed_save_changes_nothing_and_the_store_keeps_working()
    {
        var store = Open();
        await store.SaveAsync(Lobby(), default);

        // A trigger makes the write fail after the session part has already been written.
        await ExecuteAsync("CREATE TRIGGER fail_next BEFORE INSERT ON DenyEntry BEGIN SELECT RAISE(ABORT, 'disk said no'); END;");
        var changed = new SavedLobby(new SavedSession(7, "my room", ["p2"], "p2"), ["new"], Settings);
        await Assert.ThrowsAnyAsync<Exception>(async () => await store.SaveAsync(changed, default));
        Assert.Equal(Lobby(), await store.LoadAsync(default));

        await ExecuteAsync("DROP TRIGGER fail_next;");
        await store.SaveAsync(changed, default);
        Assert.Equal(changed, await store.LoadAsync(default));
    }

    [Fact]
    public async Task A_missing_folder_fails_and_is_tried_again_once_it_exists()
    {
        var path = Path.Combine(_folder, "later", "rotate.db");
        var store = new SqliteLobbyStore(path, _time);
        await Assert.ThrowsAnyAsync<Exception>(async () => await store.LoadAsync(default));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.Equal(SavedLobby.Empty, await store.LoadAsync(default));
    }

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = Connect();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = Connect();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
