namespace OsuRotate.Infrastructure.Bancho;

internal sealed class OutboundScheduler(OutboundQueue queue, TimeProvider time) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _wake = new(0);

    public void Enqueue(OutboundMessage message)
    {
        lock (_gate)
        {
            queue.Enqueue(message, time.GetUtcNow());
        }

        Wake();
    }

    public void Clear()
    {
        lock (_gate)
        {
            queue.Clear();
        }
    }

    public async Task PumpAsync(Func<string, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        while (true)
        {
            OutboundMessage? message;
            TimeSpan wait;
            lock (_gate)
            {
                message = queue.TryDequeue(time.GetUtcNow(), out wait);
            }

            if (message is not null)
            {
                await write(message.Line, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Rounded up so a sub-millisecond wait cannot spin.
            var timeout = wait == Timeout.InfiniteTimeSpan ? wait : TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds) + 1);
            await _wake.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => _wake.Dispose();

    private void Wake()
    {
        if (_wake.CurrentCount == 0)
        {
            _wake.Release();
        }
    }
}
