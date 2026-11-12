# Live lobby view

Status: ready-for-agent

Blocked by: 01

UC4. One page that follows `LobbyRuntime` snapshots as they change: status and room (with a link to
the room on osu.ppy.sh), players with slot, team and match status, host, queue in order, current
beatmap, match running, referees, close-when-empty, and whether the bot is connected. Updates push to
the browser without polling.

Done when a bUnit or host test shows a snapshot change reaching the page, and a slow page cannot
slow the actor.
