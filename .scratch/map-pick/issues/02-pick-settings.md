# Pick settings

Status: done

Add the owner's two settings: whether `!pick` is on (default off) and the oldest ranked year
(default 2012). They sit beside the regulation in `LobbySettings` but are not part of
`MapRegulation`, so they never affect hosts' own picks. Save and restore them with the other
rules (`SettingsJson`, `SqliteLobbyStore`); a stored lobby from before this feature loads with the
defaults.

No chat commands. Turning it off or changing the year must reach the pool (issue 03) so it is
thrown away.

Done when the settings round-trip through the store with tests, including an old stored lobby.

## Outcome

`MapPickSettings` (`src/OsuRotate.Domain/MapPickSettings.cs`) holds `Enabled` (default false) and
`OldestRankedYear` (default 2012), as `LobbySettings.MapPick`. `SettingsRules.Validate` refuses a
year outside 2007 (osu!'s first ranked year) to 2100; the dashboard (issue 06) should cap it at the
current year. It is saved in the existing settings JSON column, so there is no migration, and a
lobby saved before this feature loads with the defaults (`SqliteLobbyStoreTests`).

The pool keys its cache on the settings it is asked with (issue 03), so a change of year or
turning picks off needs no separate signal.
