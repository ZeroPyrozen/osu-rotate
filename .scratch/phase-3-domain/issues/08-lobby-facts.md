# Remaining lobby fact cases

Status: done

Review osu-ahr's LobbyTest (36 tests) for cases not already covered by the rotation port: the 16
player limit, duplicate joins, referee roles from !mp listrefs, and room state requests caused by
contradictions. Port what still applies. LobbyTerminator's close-when-empty is now a dashboard action.

## Outcome

`LobbyFactsTests.cs` holds what still applied: who is in the room (including the osu-ahr bug where a
name joined with spaces chats with underscores), the 16 player limit, the pending host leaving, match
status counts, and the contradictions osu-ahr raised `UnexpectedAction` for (a stranger leaving or
finishing, joining twice, host given to someone outside the room). Each of those makes the bot ask
for `!mp settings`, within its 15 s cooldown, and the answer puts the room right. The room title
cleaning case went to the Bancho formatter tests.

New: the room tracks referees from `!mp addref`, `!mp removeref` and `!mp listrefs` (a list replaces
what was known). Joining an existing room asks for the list. A referee's `!mp` commands are left to
Bancho, as the owner's were, instead of being read as player commands. Phase 4 needs the list for the
host-kick counterattack, and the dashboard can show it.

Not ported, because they test osu-ahr plumbing that is gone or covered elsewhere: room creation and
closing (phase 4's runner), message sending and cooldowns (the Bancho layer and each feature's own
cooldown tests), event wiring, `!mp settings` loading (HostRotationTests.RoomStates_), `!stats`
(the AFK tests), clear-host and start-timer flags (HostRotationTests.ClearedHost, VoteTests), and
abort counting (HostRotationTests.Aborts).

Mutation testing: all 14 mutants of the room's limits, statuses, referee tracking and contradiction
requests are caught.
