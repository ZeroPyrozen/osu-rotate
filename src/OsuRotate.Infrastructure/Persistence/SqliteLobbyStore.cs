using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OsuRotate.Application;

namespace OsuRotate.Infrastructure.Persistence;

// ILobbyStore over one SQLite file in WAL mode, which survives power loss on the Pi's SD card better
// than the default rollback journal. Each call opens its own context; the actor is the only writer.
public sealed class SqliteLobbyStore : ILobbyStore
{
    private readonly DbContextOptions<LobbyDbContext> _options;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private Task? _migration;

    public SqliteLobbyStore(string databasePath, TimeProvider? time = null)
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        _options = new DbContextOptionsBuilder<LobbyDbContext>().UseSqlite(connection.ToString()).Options;
        _time = time ?? TimeProvider.System;
    }

    public async ValueTask<SavedLobby> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);

        var config = await db.LobbyConfig.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var denied = await db.DenyEntries.AsNoTracking().Select(d => d.PlayerKey).ToListAsync(cancellationToken).ConfigureAwait(false);
        var open = await db.LobbySessions.AsNoTracking()
            .Include(s => s.Queue)
            .Where(s => s.ClosedAt == null)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var session = open is null
            ? null
            : new SavedSession(open.RoomId, open.Title, [.. open.Queue.OrderBy(q => q.Position).Select(q => q.Username)], open.HostName);
        return new SavedLobby(session, [.. denied.Order(StringComparer.Ordinal)], config is null ? null : SettingsJson.Read(config.SettingsJson));
    }

    public async ValueTask SaveAsync(SavedLobby lobby, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();

        if (lobby.Settings is { } settings)
        {
            var json = SettingsJson.Write(settings);
            var config = await db.LobbyConfig.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (config is null)
            {
                db.LobbyConfig.Add(new LobbyConfigRow { SettingsJson = json, UpdatedAt = now });
            }
            else if (config.SettingsJson != json)
            {
                config.SettingsJson = json;
                config.UpdatedAt = now;
            }
        }

        var denied = await db.DenyEntries.ToListAsync(cancellationToken).ConfigureAwait(false);
        var keep = lobby.Denied.ToHashSet(StringComparer.Ordinal);
        db.DenyEntries.RemoveRange(denied.Where(d => !keep.Contains(d.PlayerKey)));
        foreach (var key in keep.Except(denied.Select(d => d.PlayerKey), StringComparer.Ordinal))
        {
            db.DenyEntries.Add(new DenyEntryRow { PlayerKey = key, CreatedAt = now });
        }

        var open = await db.LobbySessions.Where(s => s.ClosedAt == null).ToListAsync(cancellationToken).ConfigureAwait(false);
        var current = lobby.Session is { } wanted ? open.FirstOrDefault(s => s.RoomId == wanted.RoomId) : null;
        foreach (var other in open.Where(s => s != current))
        {
            other.ClosedAt = now;
        }

        if (lobby.Session is { } session)
        {
            if (current is null)
            {
                current = new LobbySessionRow { RoomId = session.RoomId, Title = session.Title, StartedAt = now };
                db.LobbySessions.Add(current);
            }
            else
            {
                // Replaced wholesale: the queue is at most 16 names.
                await db.QueueEntries.Where(q => q.SessionId == current.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }

            current.Title = session.Title;
            current.HostName = session.Host;
            current.Queue = [.. session.Queue.Select((name, i) => new QueueEntryRow { Position = i, Username = name })];
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<LobbyDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        Task migration;
        lock (_gate)
        {
            // A migration that failed (a full or missing disk) is tried again by the next call.
            if (_migration is null || _migration.IsFaulted || _migration.IsCanceled)
            {
                _migration = MigrateAsync();
            }

            migration = _migration;
        }

        await migration.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new LobbyDbContext(_options);
    }

    // Once per process. Not cancellable: a half-applied migration is worse than a slow start.
    private async Task MigrateAsync()
    {
        await using var db = new LobbyDbContext(_options);
        await db.Database.MigrateAsync().ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;").ConfigureAwait(false);
    }
}
