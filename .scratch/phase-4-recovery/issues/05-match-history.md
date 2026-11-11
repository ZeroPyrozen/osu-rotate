# Match history

Status: ready-for-agent

Blocked by: 01, 03

Save each match with its scores (Match, MatchScore) when it finishes or is aborted: beatmap, host,
start and end, outcome. This is the history and stats the dashboard shows in phase 5. The engine
needs to report finished matches so the actor can save them.

Done when a played and an aborted match are saved with the right host, map and scores, and a
restart keeps them.
