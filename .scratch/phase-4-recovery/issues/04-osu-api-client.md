# osu! API client

Status: done

`OsuApiClient` in Infrastructure over osu! API v2 with client credentials: implement
`IBeatmapCatalog` (mode and converts, removed and unavailable maps, the BeatmapCache table) and
read a match's events for recovery and the host-kick counterattack. Retry with backoff, stay well
under 60 requests a minute, and never throw into the actor.

Done when tests against recorded API responses cover each `BeatmapLookupOutcome`, the cache, a
rate-limited reply and a network failure.

## Outcome

`Infrastructure/OsuApi/`:

- `OsuApiClient`: osu! API v2 with a client-credentials token (scope `public`, the request the probe
  already used live). The token is reused and renewed a minute before it lapses or after a 401. A
  sliding window keeps requests at `MaxRequestsPerMinute` (30, half of osu!'s 60) by waiting rather
  than failing. Server errors and network failures are retried twice with a short backoff; a 429
  waits for its Retry-After, unless that is over a minute. 404 is reported as such; anything else
  becomes Failed. HTTP and network errors never throw, and the client secret is never logged. With
  no client id or secret configured, nothing is sent and every lookup is Failed (maps are let
  through, as osu-ahr did without a key).
- `OsuBeatmapCatalog` implements `IBeatmapCatalog` over `GET /api/v2/beatmapsets/lookup`, ported
  from osu-ahr's BeatmapRepository: one request caches every map and convert in the set for a day,
  converts count only when allowed, a disabled download or an availability note is NotAvailable
  (also remembered for a day), and `${title}` is the set's title. Failed lookups are not cached.
- `OsuMatchHistory` implements the new `IMatchHistory` port (Application) over
  `GET /api/v2/matches/{id}`, 100 events a page, with `after` to read on from a known event. It
  returns events, users, games with beatmap and scores, and `RunningGame`: the current game only
  if it has no end time, because `current_game_id` was seen to outlast an abort in the live probe.

Difference from the issue: the beatmap cache is in memory, not a BeatmapCache table. A restart
costs a few extra requests at most, and the cache needs no migration or cleanup.

Tests: 39 tests against a fake `HttpMessageHandler`; all 32 mutants are caught. The fixtures follow the documented API v2
responses and osu-ahr's types for these endpoints; they are not captured from the live API, which
needs the owner's API key. Still to confirm live in phase 6: the converts list in the beatmapset
lookup, and the `pass` field in match scores.

While testing this, a simulator test from issue 02 failed once under load: it polled for the
Reconnecting status, which can last only milliseconds. It now records every status change instead.
