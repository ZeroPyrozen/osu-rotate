# osu! API client

Status: ready-for-agent

`OsuApiClient` in Infrastructure over osu! API v2 with client credentials: implement
`IBeatmapCatalog` (mode and converts, removed and unavailable maps, the BeatmapCache table) and
read a match's events for recovery and the host-kick counterattack. Retry with backoff, stay well
under 60 requests a minute, and never throw into the actor.

Done when tests against recorded API responses cover each `BeatmapLookupOutcome`, the cache, a
rate-limited reply and a network failure.
