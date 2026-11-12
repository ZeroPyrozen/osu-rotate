using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OsuRotate.Infrastructure.Persistence;

// The tables recovery needs (design figure 11). Match history comes with its own tables later.
internal sealed class LobbyDbContext(DbContextOptions<LobbyDbContext> options) : DbContext(options)
{
    public DbSet<LobbyConfigRow> LobbyConfig => Set<LobbyConfigRow>();
    public DbSet<LobbySessionRow> LobbySessions => Set<LobbySessionRow>();
    public DbSet<QueueEntryRow> QueueEntries => Set<QueueEntryRow>();
    public DbSet<DenyEntryRow> DenyEntries => Set<DenyEntryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LobbyConfigRow>(e =>
        {
            e.ToTable("LobbyConfig");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<LobbySessionRow>(e =>
        {
            e.ToTable("LobbySession");
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.ClosedAt);
            e.HasMany(r => r.Queue).WithOne().HasForeignKey(q => q.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QueueEntryRow>(e =>
        {
            e.ToTable("QueueEntry");
            e.HasKey(r => new { r.SessionId, r.Position });
        });

        modelBuilder.Entity<DenyEntryRow>(e =>
        {
            e.ToTable("DenyEntry");
            e.HasKey(r => r.PlayerKey);
        });
    }
}

internal sealed class LobbyConfigRow
{
    public const int SingleId = 1;

    public int Id { get; set; } = SingleId;
    public required string SettingsJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// One row per room the bot has run. The open one has no ClosedAt.
internal sealed class LobbySessionRow
{
    public int Id { get; set; }
    public long RoomId { get; set; }
    public required string Title { get; set; }
    public string? HostName { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public List<QueueEntryRow> Queue { get; set; } = [];
}

internal sealed class QueueEntryRow
{
    public int SessionId { get; set; }
    public int Position { get; set; }
    public required string Username { get; set; }
}

internal sealed class DenyEntryRow
{
    public required string PlayerKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// Lets "dotnet ef migrations add" build the model without the web host.
internal sealed class DesignTimeLobbyDbContextFactory : IDesignTimeDbContextFactory<LobbyDbContext>
{
    public LobbyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LobbyDbContext>().UseSqlite("Data Source=design.db").Options);
}
