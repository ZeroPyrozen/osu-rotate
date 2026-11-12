# Lobby keeper

Status: done

Port LobbyKeeper (30 tests): keep the room's size, team and score mode, mods, password and title as
configured, and restore them when they drift. The host-kick counterattack needs match history, so
leave that part for phase 4 and note it.

## Outcome

`RoomKeeping.cs` holds the pure parts: team, score and mod names (`RoomModes`, ported from osu-ahr's
Modes.ts including its invalid-combination pruning), the kept settings (`LobbySettings.Keeping`), the
owner's `*keep` / `*no keep` parser and the `SlotKeeper`. `LobbyEngine.Keeper.cs` holds the flow:
everything kept is set on entering the room, the password is set again after every match, the room
is checked with `!mp settings` after a match when mode, mods or title are kept, and slots players
grow or lock make the keeper set the size again.

Left for phase 4: the host-kick counterattack (kick a host who kicks several players), which needs
the match history osu-ahr read it from. osu-ahr's option tests (bad types in the config file) move
to the settings layer in phases 4 and 5.

Deliberate differences from osu-ahr:
- `*keep password x` does not say the password in the lobby, and the description shows only
  "Password: set". (Setting it still goes through `!mp password`, which the channel can see.)
- `*keep title` with no title is ignored; osu-ahr kept an empty title, which Bancho cannot set.
- Kept mods are compared as a set with what `!mp settings` prints, so "DoubleTime, Freemod" matches
  a kept "Freemod dt".
- No mods kept as "none" is sent as `!mp mods None`; osu-ahr sent `!mp mods` with nothing after it.
