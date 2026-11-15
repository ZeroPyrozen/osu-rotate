# Map pick: `!pick`

A chat command that finds a map fitting the room's rules. When the host types `!pick`, the bot
puts the map in the room; when anyone else does, the bot only suggests it. It is for a host with
no idea what to play, and for players who want to help the next host.

Done when, on the live lobby with the feature turned on, the host's `!pick` sets a fitting map
that the regulation doesn't reject, another player's `!pick` posts a suggestion, the cooldowns
and failure messages behave as below, and turning the feature off on the dashboard makes `!pick`
silent and drops it from `!help`.

## Decisions (interview, 2026-11-14)

1. **Host sets, others suggest.** The host's `!pick` sets the map with `!mp map` and the usual
   accepted-map description. Anyone else's `!pick` posts `Suggestion from <player>: ` followed by
   the same description (`MapRegulation.DescribeMap`, the editable template with mirror links).
   The owner is treated like any player: setting only when they are host.
2. **Source: live osu! search** (`GET /beatmapsets/search`). The room's own match history as a
   source is a later idea, not this feature.
3. **An in-memory pool.** The bot fetches a few pages of matching maps into memory and each
   `!pick` draws one at random, without handing out the same map set twice (issue 08). The pool refills when it
   runs out (issue 03), and is thrown away when the rules or the oldest year change. Nothing is written to
   disk; a restart starts with an empty pool.
4. **Ranked and approved maps only**, ranked in or after an oldest year (default 2012).
5. **The oldest year is a pick setting**, not part of the regulation. Hosts may still pick older
   maps by hand.
6. **No repeats:** maps played in the room's last 20 matches are skipped (`IMatchArchive.RecentAsync`).
7. **Cooldowns:** 30 s per player and 10 s for the room. A `!pick` inside either is ignored
   without a reply.
8. **Rules turned off still apply to `!pick`.** It uses the stored star, length and mode limits
   even while `Regulation.Enabled` is false.
9. **Failure gets one short reply:** "No maps fit the current rules." when nothing matches, or
   "Couldn't reach osu! right now, try again later." when the API fails. The cooldowns still
   count, so a retry doesn't repeat it.
10. **Owner controls on the dashboard only:** on/off and the oldest year, in a "Map picks" section
    of the Rules page, saved and restored like the other rules. No `*` chat commands. The repeat
    window (20) and the cooldowns (30 s, 10 s) are constants.
11. **Off by default.** `!help` mentions `!pick` only while it is on.
12. **Suggestion format** is the description template with the `Suggestion from <player>: ` label.
13. **Ignored during a match**, from everyone, silently.
14. **Only maps made for the room's mode**: the search filters on mode, so no converts.

## Design notes

- The engine stays pure. `!pick` emits a command (like `LookUpBeatmap`) that the actor routes to
  the pool, never to Bancho, and the answer comes back as an event.
- A drawn map is checked again with `MapRegulation.Rate` before use, so the regulation stays the
  one source of truth for the limits.
- For the host, the engine records the map as accepted before sending `SetBeatmap`, so its own
  change coming back as `BeatmapChanged` isn't looked up and rated a second time.
- The answer can arrive after things changed. If the requester is no longer host, or a match has
  started, the pick is dropped without a message.
- `!pick` must not be matched by `ChatCommand`'s BanchoBot exclusions (`!request` is; `!pick` isn't).

## Open

- Pool size: one kept page per fill (70 to 200 fitting difficulties, issue 01), refilled when empty
  (issue 03); adjust after the live check.
- Whether `s=ranked` includes approved maps (only ranked ones were seen).

## Settled by issue 01

`/beatmapsets/search` answers the bot's app-only token. Its filters match whole sets, so the pool
filters each difficulty (stars, length, mode) itself. Pages come by cursor only and `total` is
capped at 10000, so randomness comes from the sort order and how many pages are skipped.

## Issues

01 comes first. 02 and 04 can start alongside it. 03 needs 01. 05 needs 03 and 04. 06 needs 02.
07 needs 05 and 06 and closes the feature. 08 came from the live trial.
