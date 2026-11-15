# Try it on the live lobby

Status: ready-for-human

Blocked by: 05, 06

After deploying, the owner turns Map picks on in the dashboard and checks, in osu!:
- As host, `!pick` sets a map inside the rules and the bot doesn't reject or revert it.
- From a second account, `!pick` posts a suggestion with the description and mirror links.
- A second `!pick` within 30 s, and any `!pick` during a match, get no reply.
- Narrow the rules until nothing fits: one "No maps fit" reply.
- `!help` lists `!pick`; after turning it off, `!pick` is silent and `!help` doesn't list it.
- No map from before 2012 or from the last 20 matches comes up across ~10 picks.

Paste what the bot said, and note anything odd, below. Tune the pool size in the spec's Open
section from what was seen.
