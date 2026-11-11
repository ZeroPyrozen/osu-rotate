# Map regulation

Status: done

Port MapChecker (44 tests): star, length, game mode and ranked status limits, violation count before
skipping the host, and reverting a rejected map. The rule itself is pure and belongs in the domain;
fetching beatmaps belongs to an application port (IBeatmapCatalog) that phase 4 implements.

## Outcome

`MapRegulation.cs` holds the pure rules (limits, rating, descriptions, the owner's `*regulation`
parser) and `LobbyEngine.Maps.cs` the flow. The engine emits `LookUpBeatmap`, which phase 4 must
route to `IBeatmapCatalog` (in Application) instead of the transport, and feed back the answer as
`BeatmapLookedUp`. The Bancho formatter throws if it is ever sent there.

osu-ahr's option validation tests (bad types in the config file) move to the settings layer in
phases 4 and 5, where the dashboard edits these rules. "Ranked status" in this issue's first draft
meant osu-ahr's not-available and removed checks; there is no ranked-only rule.

Deliberate differences from osu-ahr:
- With no accepted map yet, a rejection only explains; osu-ahr sent `!mp map 0`.
- A failed lookup (osu! API down) lets the map through silently, as osu-ahr did, but explicitly.
- A `*regulation a=1 b=2` command with one bad value changes nothing.
- The regulation text follows osu-ahr's code ("star rating", "Mode: osu!"); its tests expected
  older wording.
