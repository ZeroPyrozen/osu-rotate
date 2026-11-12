# AFK handling

Status: done
Blocked by: 01

Port HostSkipper's host AFK check (periodic !stat while the host is idle, skip if AFK) and AfkKicker's
points system (8 tests). The owner must never be kicked, because the bot runs on the owner's account.

## Outcome

`LobbyEngine.Afk.cs`. The host check sends a private `!stat`, waits up to 5 seconds for the reply,
and on an away answer asks again in the room; that public answer skips the host. The kicker's points,
cooldown and threshold match osu-ahr, and the owner is never kicked or given points.

Deliberate differences from osu-ahr:
- `Unknown` and `None` activities never count as away.
- A kicked player's points reset, so the kick is not repeated before Bancho reports them gone.
- An aborted match gives no points.
- `*afkkick_cooltime` takes seconds (minimum 10), not milliseconds.

With host skipping turned off and the kicker on, an away host is kicked after two checks, as in osu-ahr.
