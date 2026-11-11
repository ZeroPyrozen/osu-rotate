# Remaining lobby fact cases

Status: ready-for-agent

Review osu-ahr's LobbyTest (36 tests) for cases not already covered by the rotation port: the 16
player limit, duplicate joins, referee roles from !mp listrefs, and room state requests caused by
contradictions. Port what still applies. LobbyTerminator's close-when-empty is now a dashboard action.
