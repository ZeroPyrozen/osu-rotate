using OsuRotate.Application;

namespace OsuRotate.Web.Components.History;

// Walks the match history a page at a time: Older goes back, Newer retraces the steps, Newest starts
// over. A failed read leaves the current page as it was.
public sealed class MatchPager(IMatchArchiveReader archive, int pageSize)
{
    // Where each page shown before this one started.
    private readonly Stack<long?> _newer = new();
    private long? _start;

    public MatchPage? Page { get; private set; }

    public bool HasNewer => _newer.Count > 0;

    public bool HasOlder => Page?.OlderThan is not null;

    public async Task NewestAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(null, cancellationToken).ConfigureAwait(false);
        _newer.Clear();
    }

    public async Task OlderAsync(CancellationToken cancellationToken)
    {
        if (Page?.OlderThan is not { } older)
        {
            return;
        }

        var start = _start;
        await LoadAsync(older, cancellationToken).ConfigureAwait(false);
        _newer.Push(start);
    }

    public async Task NewerAsync(CancellationToken cancellationToken)
    {
        if (_newer.Count == 0)
        {
            return;
        }

        await LoadAsync(_newer.Peek(), cancellationToken).ConfigureAwait(false);
        _newer.Pop();
    }

    private async Task LoadAsync(long? before, CancellationToken cancellationToken)
    {
        Page = await archive.PageAsync(before, pageSize, cancellationToken).ConfigureAwait(false);
        _start = before;
    }
}
