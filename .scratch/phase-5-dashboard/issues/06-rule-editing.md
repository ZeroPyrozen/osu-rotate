# Rule editing

Status: ready-for-agent

Blocked by: 02

UC8. Edit the rules live: map regulation, lobby keeper (mode, size, mods, password, title), votes,
AFK handling, auto start, stuck-match abort, host-kick tolerance, info message. Validate before
posting `ChangeSettings` (osu-ahr's option validation tests apply here); changes persist through
the store and keep effects like the keeper re-applying the room settings.

Done when invalid values are refused with a message, valid ones reach the engine and the database,
and a restart keeps them.
