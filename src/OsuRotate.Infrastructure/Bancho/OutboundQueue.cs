using OsuRotate.Domain;

namespace OsuRotate.Infrastructure.Bancho;

internal sealed record OutboundMessage(string Line, CommandPriority Priority, string? CoalesceKey = null);

// Decides what may be sent and when. Holds no timers and takes the time as a parameter, so its
// behaviour is deterministic. Not thread-safe; OutboundScheduler guards it.
//
// The limit is a sliding window rather than a refilling token bucket. Bancho's rule is N messages
// every 5 seconds, and a token bucket can briefly exceed that across a refill.
internal sealed class OutboundQueue(int limit, TimeSpan window, TimeSpan staleAfter, int maxInfoMessages)
{
    private readonly List<Entry>[] _queues = [[], [], []];
    private readonly Queue<DateTimeOffset> _sent = new();

    public void Enqueue(OutboundMessage message, DateTimeOffset now)
    {
        var queue = _queues[(int)message.Priority];
        var entry = new Entry(message, now);

        if (message.CoalesceKey is not null)
        {
            var existing = queue.FindIndex(e => e.Message.CoalesceKey == message.CoalesceKey);
            if (existing >= 0)
            {
                queue[existing] = entry;
                return;
            }
        }

        queue.Add(entry);

        if (message.Priority == CommandPriority.Info && queue.Count > maxInfoMessages)
        {
            queue.RemoveAt(0);
        }
    }

    // Returns the next message, or null with how long to wait. An infinite wait means nothing is queued.
    public OutboundMessage? TryDequeue(DateTimeOffset now, out TimeSpan wait)
    {
        _queues[(int)CommandPriority.Info].RemoveAll(e => now - e.EnqueuedAt > staleAfter);

        var queue = Array.Find(_queues, q => q.Count > 0);
        if (queue is null)
        {
            wait = Timeout.InfiniteTimeSpan;
            return null;
        }

        while (_sent.TryPeek(out var oldest) && now - oldest >= window)
        {
            _sent.Dequeue();
        }

        if (_sent.Count >= limit)
        {
            wait = _sent.Peek() + window - now;
            return null;
        }

        var entry = queue[0];
        queue.RemoveAt(0);
        _sent.Enqueue(now);
        wait = TimeSpan.Zero;
        return entry.Message;
    }

    // Keeps the send history: messages already sent still count against the limit on Bancho's side.
    public void Clear()
    {
        foreach (var queue in _queues)
        {
            queue.Clear();
        }
    }

    private sealed record Entry(OutboundMessage Message, DateTimeOffset EnqueuedAt);
}
