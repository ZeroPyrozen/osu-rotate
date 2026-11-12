# Week-long soak

Status: ready-for-human

Blocked by: 06

Leave the lobby running for 7 days without touching the Pi. It should spend some of that time
empty, including at least one stretch of 24 hours or more, to answer the design report's last open
item: does an empty room survive for days on Bancho, and what does the bot do if it doesn't?

The agent checks in from the journal and a database copy the owner shares, and records:

- Uptime, restarts and their causes, reconnects to Bancho.
- Memory and CPU over the week; the database and WAL size; anything that grows without stopping.
- The `/history` stats' speed with a week of real data, and an estimate for a year from the
  phase 5 numbers (30 ms a page, 160 ms a month, 1.1 s a year on the dev PC).
- Whether the empty room was kept, closed by Bancho, or re-created.

Done when 7 days pass with no manual fix on the Pi, and the findings are recorded.
