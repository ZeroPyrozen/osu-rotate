# Host-kick counterattack

Status: done

Blocked by: 04

Left over from phase 3's lobby keeper: osu-ahr kicks a host who kicks several players (its
`hostkick_tolerance`, default 4), and at once if they kick a referee or the owner. Kicks by the host
show only in the match history, so read them from the match API. Kicks the bot made itself (AFK)
do not count.

Done when the ported behaviour is tested with recorded match events.

## Outcome

`LobbyEngine.HostKicks.cs`. Settings: `HostKickTolerance` (4, as osu-ahr; 0 turns it off) and
`HostKickCheckInterval` (60 s). Kicks a host makes in the game never show in chat, so the engine
emits a new application-handled command, `ReadMatchHistory(afterEventId)`, when it enters the room,
when a match starts (osu-ahr's default) and every minute between matches. The actor answers it from
`IMatchHistory` beside its loop and feeds back `MatchHistoryRead` with the kicks and where the
history ends; the Bancho formatter refuses the command.

- The first read after entering (or re-entering) the room only finds where the history ends, so
  kicks from before are never judged. If that read fails, the timer asks again from the start.
- Each kick counts against whoever hosted when it happened, from the engine's own record of host
  changes; a turn reaching the tolerance, or kicking the owner or a referee, gets that host kicked if
  still in the room. The owner and referees may kick as host.
- Kicks made by referees, the bot's own AFK kicks among them, are recognised from BanchoBot's
  "Kicked X from the match." reply within five minutes and excused once.

Differences from osu-ahr: it meant to skip its own kicks but never filled that list, so its AFK
kicks counted against the host; it blamed whoever hosted when the history was read; and it judged
kicks from before it joined.

Tests: 19 engine tests (including the first read failing, a rejoin, and the cursor moving on), 4
actor tests, and the formatter guard. All 25 mutants are caught. Still to see live in phase 6: the
`player-kicked` events and their timestamps from the real match API.
