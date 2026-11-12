namespace OsuRotate.Application;

public enum ChatSource
{
    // Someone in the lobby (the owner too, when they type in the game).
    Player,

    // The bot's own announcements, sent from the owner's account.
    Bot,

    // The owner typing on the dashboard.
    Owner,
}

public sealed record ChatLine(DateTimeOffset At, string From, string Text, ChatSource Source, bool IsAction = false);

// The lobby's recent chat for the dashboard: the last Capacity lines, in memory only. Chat is never
// written to disk. Safe to read from any thread; lines are added on the actor's loop.
public sealed class ChatLog(int capacity = ChatLog.DefaultCapacity)
{
    public const int DefaultCapacity = 300;

    private readonly Queue<ChatLine> _lines = new();
    private readonly Lock _gate = new();

    public int Capacity { get; } = capacity;

    // Raised on the actor's loop: handlers must only hand the line on, never wait.
    public event Action<ChatLine>? Added;

    public IReadOnlyList<ChatLine> Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    public void Add(ChatLine line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > Capacity)
            {
                _lines.Dequeue();
            }
        }

        Added?.Invoke(line);
    }
}
