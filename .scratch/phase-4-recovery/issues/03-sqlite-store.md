# SQLite store

Status: ready-for-agent

Blocked by: 01

`ILobbyStore` in Application, `SqliteLobbyStore` with EF Core in Infrastructure, in WAL mode. Tables
from figure 11 that recovery needs: LobbySession (room id, status, settings), QueueEntry, LobbyConfig
(the owner's rules), DenyEntry and Player. The actor saves the session and queue whenever they
change, before the commands that follow are sent.

Done when the store round-trips a session, queue, settings and deny list, a failed save is retried
or reported without losing the lobby, and the database survives being reopened.
