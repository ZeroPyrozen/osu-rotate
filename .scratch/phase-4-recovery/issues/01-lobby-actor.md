# Lobby actor

Status: done

Add `LobbyActor` in Application: a single reader of a `Channel<LobbyInput>` that owns a `LobbyEngine`.
Inputs are Bancho events (pumped from `ILobbyTransport.Events`), beatmap lookup answers, and owner
actions from the dashboard (skip, skip to, reorder, deny, allow, change settings, say). It sleeps
until the next input or `engine.NextDue` and then calls `Tick()`.

Commands from the engine go to the transport, except `LookUpBeatmap`, which runs on
`IBeatmapCatalog` and comes back as a `BeatmapLookedUp` input. A failing lookup becomes `Failed`; a
failing send or a throwing handler is logged and the lobby keeps running.

After each input the actor publishes an immutable `LobbySnapshot` (players, host, queue, match state,
beatmap, settings) for the dashboard.

Done when tests with a fake transport, a fake catalog and a fake clock show events in, commands out,
timers firing without events, lookups answered out of band, and one bad input not stopping the loop.

## Outcome

`LobbyActor` (Application) owns the engine and reads one unbounded channel. `LobbyInput` covers
Bancho events and the owner's actions (`SkipHost`, `SkipToPlayer`, `ReorderQueue`, `DenyPlayer`,
`AllowPlayer`, `ChangeSettings`, `SayInLobby`, which goes out at Admin priority). Between inputs it
waits on the inbox or `Task.Delay` until `engine.NextDue` on the same `TimeProvider`, then ticks.
`LookUpBeatmap` runs on `IBeatmapCatalog` beside the loop and returns as `BeatmapLookedUp`.
`DrainAsync` completes once everything posted before it has been handled.

Application may only use the base class library, so failures go to `ILobbyDiagnostics` (input
failed, send failed, lookup failed, event stream ended) for the host to log, instead of `ILogger`.
`LobbySnapshot` is compared by content, so `SnapshotChanged` fires only on real changes.

New test project `OsuRotate.Application.Tests` uses `FakeTimeProvider`
(Microsoft.Extensions.TimeProvider.Testing 10.11.0). 15 tests, stable over 15 repeated runs; all 16
mutants of the loop, dispatch, error handling and snapshot equality are caught.

Not yet here, by plan: lifecycle (02) and saving before sending (03).
