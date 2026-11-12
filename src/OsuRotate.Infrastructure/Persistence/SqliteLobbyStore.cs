using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Infrastructure.Persistence;

// ILobbyStore over one SQLite file in WAL mode, which survives power loss on the Pi's SD card better
// than the default rollback journal. Each call opens its own context; the actor is the only writer.
public sealed class SqliteLobbyStore : ILobbyStore, IMatchArchive, IMatchArchiveReader
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

    public async ValueTask SaveAsync(long? roomId, MatchRecord match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var sessionId = roomId is { } room
            ? await db.LobbySessions.Where(s => s.RoomId == room && s.ClosedAt == null).Select(s => (int?)s.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        db.Matches.Add(new MatchRow
        {
            SessionId = sessionId,
            RoomId = roomId,
            BeatmapId = match.BeatmapId,
            HostName = match.HostName,
            HostUserId = match.HostUserId,
            // Kept in UTC, so the stored text sorts by time and the stats can compare it.
            StartedAt = match.StartedAt.ToUniversalTime(),
            EndedAt = match.EndedAt.ToUniversalTime(),
            Outcome = match.Outcome,
            Scores = [.. match.Scores.Select((s, i) => new MatchScoreRow
            {
                Position = i,
                Username = s.Username,
                OsuUserId = s.OsuUserId,
                Score = s.Score,
                Passed = s.Passed,
            })],
        });

        var seen = match.Scores.Where(s => s.OsuUserId is not null).Select(s => (Id: s.OsuUserId!.Value, s.Username)).ToList();
        if (match.HostUserId is { } hostId && match.HostName is { } hostName)
        {
            seen.Add((hostId, hostName));
        }

        foreach (var (id, username) in seen.DistinctBy(p => p.Id))
        {
            var player = await db.Players.FindAsync([id], cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                db.Players.Add(new PlayerRow { OsuUserId = id, Username = username, FirstSeenAt = match.EndedAt, LastSeenAt = match.EndedAt });
            }
            else
            {
                player.Username = username;
                player.LastSeenAt = match.EndedAt;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<ArchivedMatch>> RecentAsync(int count, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Matches.AsNoTracking()
            .Include(m => m.Scores)
            .OrderByDescending(m => m.Id)
            .Take(count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(ToArchived)];
    }

    public async ValueTask<MatchPage> PageAsync(long? beforeId, int size, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Matches.AsNoTracking()
            .Include(m => m.Scores)
            .Where(m => beforeId == null || m.Id < beforeId)
            .OrderByDescending(m => m.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new MatchPage([.. rows.Take(size).Select(ToArchived)], rows.Count > size);
    }

    // Raw SQL: EF cannot compare or group the DateTimeOffset text SQLite stores. The text is UTC (see
    // SaveAsync) in EF's format, so comparing it as text compares the times, and the StartedAt index
    // keeps a short window fast however long the history grows.
    public async ValueTask<MatchStats> StatsAsync(MatchStatsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var top = Math.Clamp(query.Top, 1, MatchStatsQuery.MaxTop);
        var since = query.Since.ToUniversalTime().ToString(StoredDateTimeOffset, CultureInfo.InvariantCulture);
        var shift = string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(query.DayOffset.TotalMinutes)} minutes");
        await using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);

        var matches = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM \"Match\" WHERE StartedAt >= {since}")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        var maps = await db.Database
            .SqlQuery<MapPlays>($"""
                SELECT BeatmapId, COUNT(*) AS Plays FROM "Match" WHERE StartedAt >= {since}
                GROUP BY BeatmapId ORDER BY Plays DESC, BeatmapId LIMIT {top}
                """)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Grouped before the names are looked up, and CROSS JOIN keeps SQLite walking the window's
        // matches by time: left to itself it walks every score by player and takes ten times longer.
        // A player has one score per match, so scores count matches.
        var players = await db.Database
            .SqlQuery<PlayerRowCount>($"""
                SELECT COALESCE(p.Username, x.Name) AS Name, x.OsuUserId AS OsuUserId, x.Matches AS Matches
                FROM (
                    SELECT s.OsuUserId AS OsuUserId, MIN(s.Username) AS Name, COUNT(*) AS Matches
                    FROM "Match" m CROSS JOIN "MatchScore" s ON s.MatchId = m.Id
                    WHERE m.StartedAt >= {since}
                    GROUP BY s.OsuUserId, CASE WHEN s.OsuUserId IS NULL THEN s.Username END
                ) x
                LEFT JOIN "Player" p ON p.OsuUserId = x.OsuUserId
                ORDER BY Matches DESC, Name LIMIT {top}
                """)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var days = await db.Database
            .SqlQuery<DayRowCount>($"""
                SELECT date(StartedAt, {shift}) AS Day, COUNT(*) AS Matches FROM "Match" WHERE StartedAt >= {since}
                GROUP BY Day ORDER BY Day
                """)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new MatchStats(
            matches,
            maps,
            [.. players.Select(p => new PlayerMatches(p.Name, p.OsuUserId, p.Matches))],
            [.. days.Select(d => new DayCount(DateOnly.ParseExact(d.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture), d.Matches))]);
    }

    // How EF Core stores a DateTimeOffset in SQLite.
    private const string StoredDateTimeOffset = @"yyyy\-MM\-dd HH\:mm\:ss.FFFFFFFzzz";

    private sealed record PlayerRowCount(string Name, int? OsuUserId, int Matches);

    private sealed record DayRowCount(string Day, int Matches);

    private static ArchivedMatch ToArchived(MatchRow m) => new(
        m.Id,
        m.RoomId,
        new MatchRecord(
            m.BeatmapId,
            m.HostName,
            m.HostUserId,
            m.StartedAt,
            m.EndedAt,
            m.Outcome,
            [.. m.Scores.OrderBy(s => s.Position).Select(s => new MatchRecordScore(s.Username, s.OsuUserId, s.Score, s.Passed))]));

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
