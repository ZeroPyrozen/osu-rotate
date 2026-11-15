using OsuRotate.Domain;

namespace OsuRotate.Application;

// Answers the engine's !pick: one random map that fits the query, never one in skip (the room's
// recent maps), and not the same map twice while the pool lasts. Errors return Failed rather than throwing.
public interface IMapPool
{
    ValueTask<MapPickResult> DrawAsync(MapPickQuery query, IReadOnlySet<int> skip, CancellationToken cancellationToken);
}
