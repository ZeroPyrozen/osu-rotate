using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Persistence;
using OsuRotate.Web.Components.History;

namespace OsuRotate.Web.Tests;

// Phase 5, issue 07: the match history and stats (UC9).
public class HistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static MatchRecord Match(int beatmapId, DateTimeOffset startedAt, string host = "alice", params MatchRecordScore[] scores) =>
        new(beatmapId, host, null, startedAt, startedAt.AddMinutes(3), MatchOutcome.Finished, scores);

    private static async Task<InMemoryMatchArchive> ArchiveAsync(int count)
    {
        var archive = new InMemoryMatchArchive();
        for (var i = 1; i <= count; i++)
        {
            await archive.SaveAsync(7, Match(i, Now.AddMinutes(-5 * (count - i))), default);
        }

        return archive;
    }

    public class Pager
    {
        private static IEnumerable<long> Ids(MatchPager pager) => pager.Page!.Matches.Select(m => m.Id);

        [Fact]
        public async Task Older_and_newer_walk_the_pages_and_newest_starts_over()
        {
            var pager = new MatchPager(await ArchiveAsync(7), 3);
            await pager.NewestAsync(default);
            Assert.Equal([7L, 6, 5], Ids(pager));
            Assert.False(pager.HasNewer);
            Assert.True(pager.HasOlder);

            await pager.OlderAsync(default);
            Assert.Equal([4L, 3, 2], Ids(pager));
            await pager.OlderAsync(default);
            Assert.Equal([1L], Ids(pager));
            Assert.False(pager.HasOlder);
            Assert.True(pager.HasNewer);

            await pager.OlderAsync(default);
            Assert.Equal([1L], Ids(pager));

            await pager.NewerAsync(default);
            Assert.Equal([4L, 3, 2], Ids(pager));
            await pager.NewerAsync(default);
            Assert.Equal([7L, 6, 5], Ids(pager));
            Assert.False(pager.HasNewer);

            await pager.NewerAsync(default);
            Assert.Equal([7L, 6, 5], Ids(pager));

            await pager.OlderAsync(default);
            await pager.NewestAsync(default);
            Assert.Equal([7L, 6, 5], Ids(pager));
            Assert.False(pager.HasNewer);
        }

        [Fact]
        public async Task New_matches_do_not_shift_an_older_page()
        {
            var archive = await ArchiveAsync(5);
            var pager = new MatchPager(archive, 2);
            await pager.NewestAsync(default);
            await pager.OlderAsync(default);
            Assert.Equal([3L, 2], Ids(pager));
            await archive.SaveAsync(7, Match(99, Now), default);

            await pager.OlderAsync(default);
            Assert.Equal([1L], Ids(pager));
            await pager.NewerAsync(default);
            Assert.Equal([3L, 2], Ids(pager));
            await pager.NewerAsync(default);
            Assert.Equal([6L, 5], Ids(pager));
        }

        [Fact]
        public async Task A_failed_read_keeps_the_page_and_the_way_back()
        {
            var archive = new FailingArchive(await ArchiveAsync(5));
            var pager = new MatchPager(archive, 2);
            await pager.NewestAsync(default);
            await pager.OlderAsync(default);

            archive.Fail = true;
            await Assert.ThrowsAsync<IOException>(() => pager.NewerAsync(default));
            await Assert.ThrowsAsync<IOException>(() => pager.OlderAsync(default));
            Assert.Equal([3L, 2], Ids(pager));
            Assert.True(pager.HasNewer);

            archive.Fail = false;
            await pager.NewerAsync(default);
            Assert.Equal([5L, 4], Ids(pager));
            Assert.False(pager.HasNewer);
        }
    }

    public sealed class Rendering : IAsyncDisposable
    {
        private readonly FailingArchive _archive = new(new InMemoryMatchArchive());
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;

        public Rendering()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IMatchArchiveReader>(_archive);
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            _services = services.BuildServiceProvider();
            _renderer = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        }

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }

        private Task<string> RenderAsync<T>()
            where T : IComponent =>
            _renderer.Dispatcher.InvokeAsync(async () => (await _renderer.RenderComponentAsync<T>(ParameterView.Empty)).ToHtmlString());

        private Task SaveAsync(MatchRecord match) => _archive.Inner.SaveAsync(7, match, default).AsTask();

        [Fact]
        public async Task Matches_show_map_host_outcome_and_scores_as_text()
        {
            await SaveAsync(Match(4242, Now.AddHours(-1), "<b>host</b>", new MatchRecordScore("p1", 1, 812_345, true), new MatchRecordScore("p2", null, 1_000_000, false)));
            await _archive.Inner.SaveAsync(7, new MatchRecord(77, null, null, Now, Now.AddSeconds(20), MatchOutcome.Aborted, []), default);

            var html = await RenderAsync<MatchList>();
            Assert.Contains("href=\"https://osu.ppy.sh/b/4242\"", html, StringComparison.Ordinal);
            Assert.Contains("&lt;b&gt;host&lt;/b&gt;", html, StringComparison.Ordinal);
            Assert.Contains("Finished", html, StringComparison.Ordinal);
            Assert.Contains("Aborted", html, StringComparison.Ordinal);
            Assert.Contains("2 scores", html, StringComparison.Ordinal);
            Assert.Contains("p1: 812,345", html, StringComparison.Ordinal);
            Assert.Contains("p2: 1,000,000 (failed)", html, StringComparison.Ordinal);

            // The best score first, the newest match first.
            Assert.True(html.IndexOf("p2:", StringComparison.Ordinal) < html.IndexOf("p1:", StringComparison.Ordinal));
            Assert.True(html.IndexOf("/b/77", StringComparison.Ordinal) < html.IndexOf("/b/4242", StringComparison.Ordinal));
            Assert.DoesNotContain("Older", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_full_page_offers_older_matches()
        {
            for (var i = 0; i <= MatchList.PageSize; i++)
            {
                await SaveAsync(Match(i, Now.AddMinutes(-i)));
            }

            var html = await RenderAsync<MatchList>();
            Assert.Contains(">Older<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Newer<", html, StringComparison.Ordinal);
            Assert.Equal(MatchList.PageSize, html.Split("<tr>").Length - 2);
        }

        [Fact]
        public async Task An_empty_history_says_so()
        {
            Assert.Contains("No matches yet.", await RenderAsync<MatchList>(), StringComparison.Ordinal);
            Assert.Contains("No matches in this time.", await RenderAsync<MatchStatsPanel>(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_database_error_shows_a_message()
        {
            _archive.Fail = true;
            Assert.Contains("could not be read", await RenderAsync<MatchList>(), StringComparison.Ordinal);
            Assert.Contains("could not be read", await RenderAsync<MatchStatsPanel>(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Stats_cover_the_last_30_days()
        {
            await SaveAsync(Match(1, Now.AddDays(-31), "x", new MatchRecordScore("old", 9, 1, true)));
            await SaveAsync(Match(5, Now.AddDays(-2), "x", new MatchRecordScore("alice", 1, 1, true), new MatchRecordScore("<i>bob</i>", 2, 1, true)));
            await SaveAsync(Match(5, Now.AddDays(-1), "x", new MatchRecordScore("alice", 1, 1, true)));
            await SaveAsync(Match(6, Now.AddDays(-1), "x"));

            var html = await RenderAsync<MatchStatsPanel>();
            Assert.Contains("3 matches", html, StringComparison.Ordinal);
            Assert.Contains("/b/5\" target=\"_blank\" rel=\"noopener noreferrer\">5</a>: 2 plays", html, StringComparison.Ordinal);
            Assert.Contains("6</a>: 1 play<", html, StringComparison.Ordinal);
            Assert.Contains("alice: 2 matches", html, StringComparison.Ordinal);
            Assert.Contains("&lt;i&gt;bob&lt;/i&gt;: 1 match<", html, StringComparison.Ordinal);
            Assert.DoesNotContain("old:", html, StringComparison.Ordinal);
            Assert.Contains("width: 100%", html, StringComparison.Ordinal);
            Assert.Contains("width: 50%", html, StringComparison.Ordinal);
        }
    }

    public sealed class Page : IDisposable
    {
        private readonly BotFactory _factory = new();

        public void Dispose()
        {
            _factory.Dispose();
            _factory.DeleteFolder();
        }

        [Fact]
        public async Task A_visitor_must_log_in_first()
        {
            var response = await _factory.Browser().GetAsync(new Uri("/history", UriKind.Relative));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("https://osu.ppy.sh/oauth/authorize", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_owner_sees_the_history_from_the_database()
        {
            var store = _factory.Services.GetRequiredService<SqliteLobbyStore>();
            await store.SaveAsync(7, Match(31337, DateTimeOffset.UtcNow.AddHours(-1), "alice", new MatchRecordScore("alice", 1, 5, true)), default);
            var (browser, _) = await _factory.LogInAsync();

            var page = await browser.GetAsync(new Uri("/history", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("https://osu.ppy.sh/b/31337", html, StringComparison.Ordinal);
            Assert.Contains("1 match<", html, StringComparison.Ordinal);
            Assert.Contains("alice: 1 match", html, StringComparison.Ordinal);
        }
    }

    // Wraps an archive and fails every read while Fail is set.
    private sealed class FailingArchive(InMemoryMatchArchive inner) : IMatchArchiveReader
    {
        public InMemoryMatchArchive Inner => inner;

        public bool Fail { get; set; }

        public ValueTask<MatchPage> PageAsync(long? beforeId, int size, CancellationToken cancellationToken) =>
            Fail ? throw new IOException("database down") : inner.PageAsync(beforeId, size, cancellationToken);

        public ValueTask<MatchStats> StatsAsync(MatchStatsQuery query, CancellationToken cancellationToken) =>
            Fail ? throw new IOException("database down") : inner.StatsAsync(query, cancellationToken);
    }
}
