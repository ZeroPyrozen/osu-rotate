# Phase 6: live trial

Run osu-rotate for real: on the Raspberry Pi, on the owner's osu! account, against Bancho and the
osu! API, with the dashboard behind a Cloudflare Tunnel. Phases 1 to 5 were built and tested
against simulators and fakes; this phase finds out what the real systems do differently, and
fixes it. Design: `docs/system-design.html` (Deployment, Security, Open items).

Done when the lobby has run on the Pi for 7 days without anyone touching the Pi: it survives at
least one service restart and one reboot with the same room and queue, the owner can log in
through the tunnel and anyone else gets 403, and every "still to see live" item below is either
confirmed or fixed.

## Who does what

The owner's credentials never pass through an agent. Anything that needs the IRC password, the
OAuth secret, the Cloudflare account, `sudo` on the Pi, or being in osu! is done by the owner,
following a written checklist or a script the agent prepares. The agent reads logs and the
database the owner shares, writes the fixes, and records what was seen in each issue.

Issues marked `ready-for-human` are the owner's steps; `ready-for-agent` ones an agent can do alone.

## Decisions carried in

- Kestrel stays on `127.0.0.1:5180`; only `cloudflared` on the Pi reaches it (phase 5, issue 08).
- `Dashboard__Host` and the OAuth callback must match the tunnel's public hostname.
- Settings and secrets live in `/etc/osu-rotate/secrets.env`, which deploys never overwrite.
- One lobby, open 24/7, on the owner's own account. No automatic closing.
- Only match history and stats are stored; chat is never written to disk.
- System changes on the Pi go through scripts the owner runs. Nothing works around `sudo`.
- The repo is public: no usernames, addresses or secrets of the owner's go into commits.

## Still to see live (carried from earlier phases)

- Phase 4: the service starting on the Pi and its start time; the osu! API's converts list in the
  beatmapset lookup; the `pass` field in match scores; `player-kicked` events and their
  timestamps; telling from the match API whether a match is really running.
- Phase 5: the login through the tunnel (HTTPS callback, secure cookies); the CSP with real use;
  the stats' speed on a year of data on the Pi.
- Design report: whether an empty room survives for days on Bancho.

## Open

- The dashboard's public hostname. Needed by issue 03.
- The phases are on stacked local branches (`phase-1-foundation` to `phase-5-dashboard`) and
  nothing is pushed; `main` is still the initial commit. Issue 01 settles how they land.
- `BanchoIrcTransportTests.Stopping_ends_the_event_stream` failed once in phase 5 and not since.
- `deploy/tests/pi-install.test.sh` fails 11 checks on Windows; it needs Linux (issue 02).

## Issues

01 and 02 come first. 03 needs 01 and the hostname. 04 needs 02 and 03. 05 and 06 need 04.
07 needs 06. 08 runs alongside 05 to 07 and closes the phase.

Anything the trial finds that needs a code change becomes its own issue, numbered from 09.
