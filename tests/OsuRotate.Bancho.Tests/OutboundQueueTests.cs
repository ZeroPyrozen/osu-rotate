using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;

namespace OsuRotate.Bancho.Tests;

public class OutboundQueueTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private static OutboundQueue NewQueue(int limit = 8, int maxInfo = 64) => new(limit, Window, TimeSpan.FromSeconds(10), maxInfo);

    private static OutboundMessage Message(string line, CommandPriority priority = CommandPriority.Control, string? key = null) => new(line, priority, key);

    private static List<string> Drain(OutboundQueue queue, DateTimeOffset now)
    {
        var lines = new List<string>();
        while (queue.TryDequeue(now, out _) is { } message)
        {
            lines.Add(message.Line);
        }

        return lines;
    }

    [Fact]
    public void An_empty_queue_waits_forever()
    {
        Assert.Null(NewQueue().TryDequeue(Start, out var wait));
        Assert.Equal(Timeout.InfiniteTimeSpan, wait);
    }

    [Fact]
    public void Sends_at_most_the_limit_in_one_window()
    {
        var queue = NewQueue();
        for (var i = 0; i < 12; i++)
        {
            queue.Enqueue(Message($"m{i}"), Start);
        }

        Assert.Equal(8, Drain(queue, Start).Count);
    }

    [Fact]
    public void Reports_when_the_window_reopens()
    {
        var queue = NewQueue(limit: 1);
        queue.Enqueue(Message("a"), Start);
        queue.Enqueue(Message("b"), Start);
        Assert.NotNull(queue.TryDequeue(Start, out _));

        var now = Start + TimeSpan.FromSeconds(2);
        Assert.Null(queue.TryDequeue(now, out var wait));

        Assert.Equal(TimeSpan.FromSeconds(3), wait);
    }

    [Fact]
    public void The_limit_holds_across_any_sliding_window()
    {
        var queue = NewQueue(limit: 8);
        var sentAt = new List<DateTimeOffset>();
        for (var i = 0; i < 100; i++)
        {
            queue.Enqueue(Message($"m{i}"), Start);
        }

        // Advance in small uneven steps, as a real pump would, sending whatever is allowed.
        var now = Start;
        for (var step = 0; step < 2000 && sentAt.Count < 100; step++)
        {
            now += TimeSpan.FromMilliseconds(37);
            while (queue.TryDequeue(now, out _) is not null)
            {
                sentAt.Add(now);
            }
        }

        Assert.Equal(100, sentAt.Count);
        for (var i = 8; i < sentAt.Count; i++)
        {
            Assert.True(sentAt[i] - sentAt[i - 8] >= Window, $"Message {i} broke the limit.");
        }
    }

    [Fact]
    public void Sends_by_priority_and_keeps_order_within_a_priority()
    {
        var queue = NewQueue();
        queue.Enqueue(Message("info1", CommandPriority.Info), Start);
        queue.Enqueue(Message("admin1", CommandPriority.Admin), Start);
        queue.Enqueue(Message("control1"), Start);
        queue.Enqueue(Message("info2", CommandPriority.Info), Start);
        queue.Enqueue(Message("control2"), Start);

        Assert.Equal(["control1", "control2", "admin1", "info1", "info2"], Drain(queue, Start));
    }

    [Fact]
    public void A_host_command_overtakes_a_backlog_of_announcements()
    {
        var queue = NewQueue(limit: 1);
        queue.Enqueue(Message("announcement1", CommandPriority.Info), Start);
        queue.Enqueue(Message("announcement2", CommandPriority.Info), Start);
        Assert.Equal("announcement1", queue.TryDequeue(Start, out _)?.Line);

        queue.Enqueue(Message("!mp host"), Start);

        Assert.Equal("!mp host", queue.TryDequeue(Start + Window, out _)?.Line);
    }

    [Fact]
    public void Stale_announcements_are_dropped()
    {
        var queue = NewQueue();
        queue.Enqueue(Message("old", CommandPriority.Info), Start);

        Assert.Empty(Drain(queue, Start + TimeSpan.FromSeconds(11)));
    }

    [Fact]
    public void Control_and_admin_messages_never_go_stale()
    {
        var queue = NewQueue();
        queue.Enqueue(Message("control"), Start);
        queue.Enqueue(Message("admin", CommandPriority.Admin), Start);

        Assert.Equal(["control", "admin"], Drain(queue, Start + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Announcements_with_the_same_key_merge_into_the_latest()
    {
        var queue = NewQueue();
        queue.Enqueue(Message("Host order: a, b", CommandPriority.Info, "queue"), Start);
        queue.Enqueue(Message("other", CommandPriority.Info), Start);
        queue.Enqueue(Message("Host order: b, a", CommandPriority.Info, "queue"), Start);

        Assert.Equal(["Host order: b, a", "other"], Drain(queue, Start));
    }

    [Fact]
    public void Merging_refreshes_the_age_of_an_announcement()
    {
        var queue = NewQueue();
        queue.Enqueue(Message("first", CommandPriority.Info, "queue"), Start);
        var later = Start + TimeSpan.FromSeconds(9);
        queue.Enqueue(Message("second", CommandPriority.Info, "queue"), later);

        Assert.Equal(["second"], Drain(queue, later + TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void The_oldest_announcement_is_dropped_when_full()
    {
        var queue = NewQueue(maxInfo: 2);
        queue.Enqueue(Message("a", CommandPriority.Info), Start);
        queue.Enqueue(Message("b", CommandPriority.Info), Start);
        queue.Enqueue(Message("c", CommandPriority.Info), Start);

        Assert.Equal(["b", "c"], Drain(queue, Start));
    }

    [Fact]
    public void Clearing_drops_queued_messages_but_remembers_what_was_sent()
    {
        var queue = NewQueue(limit: 1);
        queue.Enqueue(Message("a"), Start);
        queue.Enqueue(Message("b"), Start);
        Assert.NotNull(queue.TryDequeue(Start, out _));

        queue.Clear();
        Assert.Null(queue.TryDequeue(Start, out var wait));
        Assert.Equal(Timeout.InfiniteTimeSpan, wait);

        queue.Enqueue(Message("c"), Start);
        Assert.Null(queue.TryDequeue(Start, out wait));
        Assert.Equal(Window, wait);
    }
}

public class ReconnectBackoffTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(5, 32)]
    [InlineData(6, 60)]
    [InlineData(1000, 60)]
    public void Doubles_up_to_the_maximum(int attempt, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), ReconnectBackoff.Delay(attempt, Base, Max, jitter: 0));
    }

    [Fact]
    public void Jitter_removes_at_most_half()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), ReconnectBackoff.Delay(10, Base, Max, jitter: 1));
    }
}
