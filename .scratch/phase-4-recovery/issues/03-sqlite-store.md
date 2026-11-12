# SQLite store

Status: done

Blocked by: 01

`ILobbyStore` in Application, `SqliteLobbyStore` with EF Core in Infrastructure, in WAL mode. Tables
from figure 11 that recovery needs: LobbySession (room id, status, settings), QueueEntry, LobbyConfig
(the owner's rules), DenyEntry and Player. The actor saves the session and queue whenever they
change, before the commands that follow are sent.

Done when the store round-trips a session, queue, settings and deny list, a failed save is retried
or reported without losing the lobby, and the database survives being reopened.

## Outcome

`ILobbyStore` (Application) loads and saves one `SavedLobby`: the open session (room id, title,
queue, host), the deny list and the owner's settings. `SqliteLobbyStore` (Infrastructure,
`Persistence/`) implements it with EF Core 10.0.15 on SQLite, with a first migration
(`InitialLobbyStore`) applied on first use and the file switched to WAL. A migration that fails
(say, a missing folder) is tried again on the next call. `InMemoryLobbyStore` is there for tests and
running without a disk.

- Tables: LobbyConfig (one row, settings as JSON with enums by name, so new settings need no
  migration), LobbySession (one per room run; the open one has no ClosedAt), QueueEntry and
  DenyEntry. The queue and deny list are kept by username, which is what the engine uses; osu! user
  ids are not always known.
- Each save is one transaction, so a failed save leaves the previous state whole.
- Unreadable settings JSON loads as none and the rest still loads.

The actor saves after each input when what survives a restart changed, before that input's
commands are sent (figure 9). A failed save goes to `ILobbyDiagnostics.SaveFailed`, the commands
still go, and the next input saves again.

Moved to issue 05: the Player table, which match history needs and recovery does not.

Tests: 16 store tests on real SQLite files (round trip, reopening, replacing, sessions opening and
closing, timestamps, WAL, a failed write rolling back, a missing folder), 6 actor tests for saving
(including save-before-send), and a simulator test that reads the saved room and queue back from
the database. All 19 mutants are caught.
