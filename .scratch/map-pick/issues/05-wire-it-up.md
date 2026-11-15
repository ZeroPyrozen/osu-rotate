# Wire the pool into the lobby

Status: done

Blocked by: 03, 04

In `LobbyActor`, route the pick command to the pool off the actor's loop (as `LookUpAsync` does),
passing the beatmap ids from the last 20 matches (`IMatchArchive.RecentAsync(20)`) as the ones to
skip, and feed the answer back as the pick event. Register the pool in
`ServiceCollectionExtensions`. A slow API must not hold up other lobby events.

Done when an Application test drives `!pick` through the actor with fake pool and archive and
sees the map set, and the app starts with the real registrations.

## Outcome

`LobbyActor` takes an optional `IMapPool` and routes `PickBeatmap` to it off the loop (`PickAsync`,
like `LookUpAsync`), so the command never reaches Bancho. The maps to skip are the last
`LobbyActor.RecentMatchesSkipped` (20) matches from `IMatchArchive.RecentAsync`, plus any match
whose save failed and is still waiting to be saved. The answer comes back through the inbox as
`BeatmapPicked`.

Failures: an archive that can't be read is reported and the pick goes ahead with nothing skipped;
a pool that throws, or no pool at all, answers `Failed`, so the player hears "Couldn't reach osu!".
Both report through the new `ILobbyDiagnostics.PickFailed`, logged as a warning.

`LobbyHostService` creates an `OsuMapPool` on the same `OsuApiClient` as the catalog and history,
so they share one rate limit and token. There is no DI registration: the host builds the actor's
parts by hand, as it already did for the catalog.

4 actor tests in `LobbyActorTests`: the host's pick through the pool with the skipped maps, a
failing pool, an unreadable history, and a slow pick not holding up the lobby.
