# Timer effects

Status: done

Several rules need time to pass: vote messages, AFK checks, auto start, stuck-match abort, and the
5 second host transfer timeout. Let the engine return timer effects alongside Bancho commands
(start or cancel a named timer) and accept a timer-elapsed input. The actor in phase 4 runs the timers.

First user: osu-ahr's transfer timeout. If Bancho has not confirmed a host transfer after 5 seconds
and the pending player is still in the room, request room state.

Done when a test drives a timer with the manual clock, and phase 4 has nothing to invent.

## Outcome

The engine owns its timers (`Timers`) instead of returning start and cancel effects, so a stale
"elapsed" input for a cancelled timer cannot exist. The phase 4 runner sleeps until
`LobbyEngine.NextDue` or the next event, whichever comes first, and calls `LobbyEngine.Tick()`.
The transfer timeout starts whenever a `TransferHost` is sent and stops once no transfer is pending.
