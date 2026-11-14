# Wire the pool into the lobby

Status: ready-for-agent

Blocked by: 03, 04

In `LobbyActor`, route the pick command to the pool off the actor's loop (as `LookUpAsync` does),
passing the beatmap ids from the last 20 matches (`IMatchArchive.RecentAsync(20)`) as the ones to
skip, and feed the answer back as the pick event. Register the pool in
`ServiceCollectionExtensions`. A slow API must not hold up other lobby events.

Done when an Application test drives `!pick` through the actor with fake pool and archive and
sees the map set, and the app starts with the real registrations.
