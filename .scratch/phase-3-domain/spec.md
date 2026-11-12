# Phase 3: domain rules

Port osu-ahr's lobby behaviour into `OsuRotate.Domain` as a pure engine: one input in, Bancho commands
out, no I/O, time read only from the `TimeProvider` it is given. Design: `docs/system-design.html`.

Done when the osu-ahr test scenarios that still apply are ported and pass, and deliberately breaking
each rule makes a test fail.

## Done

- Lobby facts (`Room`), host rotation (`HostRotation`), deny list, chat command parsing, `!queue`,
  owner commands `*reorder`, `*skip`, `*skipto`, `*denylist`.
- All 70 AutoHostSelector scenarios that apply to one lobby, plus the CommandParser chat cases.
- Deviations from osu-ahr, on purpose:
  - "The match is not in progress" only counts as an abort when a match was running.
  - Only the owner (the account the bot runs on) may use `*` commands.
  - One lobby, so osu-ahr's shared deny list across lobbies does not apply.

## Remaining

See `issues/`. Issue 01 is a prerequisite for 02, 03 and 04.
