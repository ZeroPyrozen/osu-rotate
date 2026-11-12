# Bot host

Status: done

Blocked by: 01, 02, 03

Wire it together in Web: a `BackgroundService` that outlives UI circuits and runs the actor, with the
transport, store and API client from configuration and secrets (the IRC password and API secret come
from the systemd environment file, never from appsettings or logs). Replace `IdleLobbyStatusReader`
with the actor's snapshot. Fill `BotVersion` from the assembly.

Done when the service starts on the Pi with no saved session and stays Idle, and the host tests
cover startup and shutdown.

## Outcome

`src/OsuRotate.Web/Bot/`:

- `LobbyHostService` is a `BackgroundService` that outlives dashboard circuits. On start it opens the
  SQLite store, recovers the saved lobby (`LobbyRecovery`), and runs the actor with the Bancho
  transport, the osu! API catalog and match history, and the store as both lobby store and match
  archive. On stop it cancels the actor and leaves the room open for the next start.
- `BotConfiguration` reads `Bancho:Nick`, `Bancho:Password`, `Bancho:Host`/`Port`, `Osu:ClientId`,
  `Osu:ClientSecret` and `Bot:DatabasePath` from configuration; on the Pi the secrets come from
  systemd's EnvironmentFile (`deploy/osu-rotate.env.example` and the deploy README now list them).
  The database defaults to systemd's `STATE_DIRECTORY` (`/var/lib/osu-rotate/rotate.db`), or
  `data/rotate.db` beside the app elsewhere (git-ignored). Without nick and password the bot does
  not connect and stays Idle; without an API key maps are let through. Both are logged once at
  start; secrets never are.
- `LobbyRuntime` gives the dashboard the running actor: its snapshot, `PostAsync` for owner actions
  (false while the bot is not running), and `ILobbyStatusReader`, replacing `IdleLobbyStatusReader`.
- `LoggingLobbyDiagnostics` sends the actor's failures to the journal.
- `BotVersion` is the assembly's informational version without the commit suffix.

Checked by running the Release build on Windows with no credentials: `/healthz` is Healthy, the
page shows Idle, the database is created, and the journal names the missing settings. The bot is
ready about 2.7 s after launch here (EF Core's first model build is most of it); Kestrel reports
ready before that, so the deploy health check is not delayed.

Not done here: starting it on the Pi. That is the user's deploy (`deploy/deploy.ps1`), which needs
their sudo password; measure the start time there too.

Tests: 13 host tests (start idle, recover a saved room from the database, owner actions, stop and
keep what was saved, unreachable Bancho still stopping quickly, configuration defaults, version);
all 9 mutants are caught.

Along the way a simulator test from issue 02 failed once: it checked for `!mp password` before the
send queue had delivered it. It now waits for the line.
