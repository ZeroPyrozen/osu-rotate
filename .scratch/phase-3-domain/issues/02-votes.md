# Votes: skip, start, abort

Status: done
Blocked by: 01

Port HostSkipper voting (33 tests, minus the AFK parts in 03), MatchStarter (18) and MatchAborter (13).
Votes pass at a share of players with a minimum count, reset on host change or match start, and the
host's own !skip or !start acts at once. Owner *start, *skip and *abort force the action. Progress
messages are deferred so a burst of votes produces one message.

## Outcome

`VoteCounter` and `LobbyEngine.Votes.cs`. Ported: skip votes with the 5 second cooldown after a host
change, start votes, all-ready start, `!start N` countdowns with 30 and 10 second warnings, `!stop`,
abort votes counted among the players who started, and the owner's `*start` and `*abort`.

Deliberate differences from osu-ahr:
- When the host leaves, a running skip vote ends instead of being re-checked. osu-ahr could pass it
  then and skip the next host too.
- A new `!start N` cancels the previous countdown's warnings.
- A vote never passes with zero votes, even if the minimum is set to 0.
- `!start` only accepts ASCII digits.
