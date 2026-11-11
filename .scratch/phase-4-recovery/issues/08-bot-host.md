# Bot host

Status: ready-for-agent

Blocked by: 01, 02, 03

Wire it together in Web: a `BackgroundService` that outlives UI circuits and runs the actor, with the
transport, store and API client from configuration and secrets (the IRC password and API secret come
from the systemd environment file, never from appsettings or logs). Replace `IdleLobbyStatusReader`
with the actor's snapshot. Fill `BotVersion` from the assembly.

Done when the service starts on the Pi with no saved session and stays Idle, and the host tests
cover startup and shutdown.
