# Votes: skip, start, abort

Status: ready-for-agent
Blocked by: 01

Port HostSkipper voting (33 tests, minus the AFK parts in 03), MatchStarter (18) and MatchAborter (13).
Votes pass at a share of players with a minimum count, reset on host change or match start, and the
host's own !skip or !start acts at once. Owner *start, *skip and *abort force the action. Progress
messages are deferred so a burst of votes produces one message.
