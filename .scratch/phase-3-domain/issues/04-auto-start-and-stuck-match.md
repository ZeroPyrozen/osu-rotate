# Auto start and stuck-match abort

Status: done
Blocked by: 01

Port AutoStartTimer (6 tests): start after a delay once a valid map is chosen. Port MatchAborter's
auto abort: when enough players have finished and others are still playing after a delay, check them
with !stat and abort if the match is stuck.

## Outcome

`LobbyEngine.AutoStart.cs`. Auto start is off by default, as in osu-ahr. The stuck-match check sends
every still-playing player's `!stat` at once instead of one after another; anyone in `Multiplaying`
keeps the match alive and the check repeats after the delay. No answer within 5 seconds counts as
stuck, as in osu-ahr. Map validation (issue 05) will need to hold back auto start until the map passes.
