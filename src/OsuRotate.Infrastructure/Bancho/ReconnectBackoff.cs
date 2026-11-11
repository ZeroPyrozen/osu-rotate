namespace OsuRotate.Infrastructure.Bancho;

internal static class ReconnectBackoff
{
    // Doubles from baseDelay up to maxDelay. jitter is 0 to 1 and removes up to half of the delay.
    public static TimeSpan Delay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, double jitter)
    {
        var doublings = Math.Clamp(attempt - 1, 0, 30);
        var delay = Math.Min(baseDelay.TotalMilliseconds * Math.Pow(2, doublings), maxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(delay * (1 - (jitter / 2)));
    }
}
