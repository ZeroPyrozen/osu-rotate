# Auto start and stuck-match abort

Status: ready-for-agent
Blocked by: 01

Port AutoStartTimer (6 tests): start after a delay once a valid map is chosen. Port MatchAborter's
auto abort: when enough players have finished and others are still playing after a delay, check them
with !stat and abort if the match is stuck.
