# Picks repeat the same map set

Status: done

Found by the owner on the live lobby, 2026-11-15, after deploying 1a84de3: `!pick` could give
another difficulty of the set it had just given.

## Cause

`OsuMapPool` kept every fitting difficulty as its own entry and remembered handed-out maps by map
id. The search returns whole sets, often with several difficulties inside the limits, so the next
random draw could land on a sibling of the last map.

## Fix

The pool now hands out sets, not maps:

- A draw picks a random set, then a random difficulty in it, and drops the rest of that set from
  the pool. A set with many fitting difficulties no longer comes up more often than one with one.
- Handed-out sets are remembered for the round. When a refill finds only sets already handed out,
  the round starts over, but not with the set just handed out unless it is the only one.
- A skipped map (from the room's last 20 matches) skips its whole set, when the pool knows its set.
  A map played from outside the pool has no known set, so only that map is skipped.

One fill now lasts about 50 picks (one page of sets) rather than 70 to 200.

Tests in `MapPoolTests`: one difficulty per set, starting over without the last set, a single set
handed out again, a skipped map skipping its set, and none fit when its set was the only one. The
four that cover the bug fail against the old pool.
