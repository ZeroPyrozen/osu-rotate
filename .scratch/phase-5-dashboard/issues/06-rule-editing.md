# Rule editing

Status: done

Blocked by: 02

UC8. Edit the rules live: map regulation, lobby keeper (mode, size, mods, password, title), votes,
AFK handling, auto start, stuck-match abort, host-kick tolerance, info message. Validate before
posting `ChangeSettings` (osu-ahr's option validation tests apply here); changes persist through
the store and keep effects like the keeper re-applying the room settings.

Done when invalid values are refused with a message, valid ones reach the engine and the database,
and a restart keeps them.

## Outcome

- `SettingsRules.Validate` (Domain) lists every problem with a set of rules, one sentence each:
  star and length limits (0 is no limit, the highest not below the lowest), game mode, violations,
  the kept modes, size (up to 16), mods that can be played together, a password with no spaces,
  a one-line title of up to 50 characters, vote shares from 0 to 1 with at least 1 vote, the AFK,
  auto start, stuck match and host-kick limits the chat commands already applied, and an info
  message of up to 1000 characters with lines of up to 400.
- `LobbyEngine.ChangeSettings` refuses invalid rules, keeps the owner and version, and has the same
  effects as the chat commands: a changed keep is put right in the room at once (dropping a kept
  password opens the room), and a changed regulation is announced. The `*regulation` command and
  the dashboard share the announcement. With no room open, the change is only kept; the keeper
  applies it when the bot enters a room.
- The actor saves after every input, so changes reach the database; recovery already loads the
  saved rules on start (phase 4), so a restart keeps them.
- `RuleForm` and `RuleActions` (Web) and `RulesPanel.razor`: one form for all the rules, filled once
  and then left to the owner until saved or reset. The kept password is never put in the page:
  the field stays empty and an empty field keeps the old one. Unknown mod names are refused rather
  than dropped.

Tests: every validation rule on both sides of its limit, the engine's effects for each keep, the
announcements, the actor sending only with a room, saving and recovering through the store, the
form round trip and every field, and rendering without the password.
Mutation check: 31 of 32 mutants are caught. The survivor showed a guard that did nothing (with
neither mode nor size kept, `FixModeAndSize` already sends nothing), so the guard was removed. One
mutant was applied by hand, since my sed pattern could not express the C# `"\r\n"`.
