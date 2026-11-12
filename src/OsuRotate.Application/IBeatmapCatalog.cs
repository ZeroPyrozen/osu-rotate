using OsuRotate.Domain;

namespace OsuRotate.Application;

// Answers the engine's LookUpBeatmap. Implemented over the osu! API in phase 4. A map that exists but
// not in the requested mode comes back as the converted version when converts are allowed and possible,
// otherwise as ModeMismatch. Errors return Failed rather than throwing.
public interface IBeatmapCatalog
{
    ValueTask<BeatmapLookup> FindAsync(int beatmapId, GameMode mode, bool allowConvert, CancellationToken cancellationToken);
}
