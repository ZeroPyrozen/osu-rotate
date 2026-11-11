# Info commands

Status: done

Port !info, !help, !version, !regulation, MapRecaster's !update (3 tests) and MiscLoader's !rank and
!mirror. Each answers in chat with a cooldown.

## Outcome

`LobbyEngine.Info.cs` holds them (`!regulation` came with issue 05). Settings: `InfoMessage` (with
`${version}`), `InfoMessageCooldown` (60 s), `InfoAnnouncementInterval` (off; only intervals over 3
minutes announce, as in osu-ahr) and `BotVersion`, which the host fills in.

- `!info` / `!help` share a cooldown; by private message they are whispered back to players in the
  lobby, with a cooldown per player. `!version` / `!v` say "osu-rotate v. x".
- `!update` sets the current map again once per map the host picks. The bot's own map changes (a
  recast, a regulation revert) do not allow another.
- `!mirror` uses the map the checker already looked up, or asks `IBeatmapCatalog` through
  `LookUpBeatmap` when the checker is off, and keeps the answer for the map.
- `!rank` asks BanchoBot `!stats` in the lobby, whose answer shows the rank, with a 10 s cooldown.

Deliberate differences from osu-ahr:
- The default info message describes osu-rotate and lists the commands instead of linking to
  osu-ahr; links can go in the setting.
- `!mirror` links BeatConnect and osu.direct, the mirror the regulation's map description already
  uses, in place of osu-ahr's Kitsu.
- `!rank` always uses BanchoBot's `!stats` rather than the osu! API. osu-ahr used the API, for the
  lobby's game mode, when it had a key; that can come with the API client in phase 4.

Mutation testing: 29 of 30 mutants caught. The survivor drops the "no map yet" guard in `!mirror`,
which the pending-request check already covers; the guard stays for clarity.
