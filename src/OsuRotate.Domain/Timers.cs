namespace OsuRotate.Domain;

// Named one-shot timers owned by the engine. The engine never waits: whoever runs it sleeps until
// NextDue and then calls Tick, which fires every timer that is due. Starting a timer under a name
// that is already running replaces it.
public sealed class Timers(TimeProvider time)
{
    private readonly Dictionary<string, Entry> _running = [];
    private long _sequence;

    public DateTimeOffset? NextDue => _running.Count == 0 ? null : _running.Values.Min(e => e.Due);

    public bool IsRunning(string name) => _running.ContainsKey(name);

    public void Start(string name, TimeSpan delay, Action elapsed) =>
        _running[name] = new Entry(time.GetUtcNow() + delay, _sequence++, elapsed);

    public void Cancel(string name) => _running.Remove(name);

    // Fires due timers one at a time in due order, so a timer started or cancelled by an earlier
    // one is respected.
    public void FireDue()
    {
        var now = time.GetUtcNow();
        while (_running
                   .Where(e => e.Value.Due <= now)
                   .OrderBy(e => e.Value.Due)
                   .ThenBy(e => e.Value.Sequence)
                   .Select(e => (KeyValuePair<string, Entry>?)e)
                   .FirstOrDefault() is { } next)
        {
            _running.Remove(next.Key);
            next.Value.Elapsed();
        }
    }

    private sealed record Entry(DateTimeOffset Due, long Sequence, Action Elapsed);
}
