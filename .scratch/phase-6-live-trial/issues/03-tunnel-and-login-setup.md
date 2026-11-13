# Tailscale and dashboard login

Status: ready-for-human

Blocked by: 01

The dashboard is served to the owner's tailnet under its own name, by a second, rootless
Tailscale node on the Pi called `osu-rotate` (ADR 0001): `https://osu-rotate.<tailnet>.ts.net`.

Agent's part, done: `deploy/tailscale-node.sh` (install, status, remove) with
`deploy/tests/tailscale-node.test.sh` in CI on Ubuntu and Debian 13, and the setup steps in
`deploy/README.md`.

Owner's steps, on the Pi and in their own accounts, following the README:

1. Copy `deploy/tailscale-node.sh` to the Pi and run `bash tailscale-node.sh install`, without
   sudo. Approve the device from the link it prints. Turn off key expiry for `osu-rotate` in the
   admin console.
2. Create the osu! OAuth application with the callback it printed,
   `https://osu-rotate.<tailnet>.ts.net/signin-osu`.
3. Put `Osu__ClientId`, `Osu__ClientSecret`, `Dashboard__OwnerUserId` and `Dashboard__Host` in
   `/etc/osu-rotate/secrets.env`. Leave `Bancho__Nick` and `Bancho__Password` empty for now, so
   the bot stays Idle while the login is tested.

Done when the node is on the tailnet serving `127.0.0.1:5180` without Funnel, and the OAuth app and
settings are in place.

## Comments

2026-11-13: the owner has no domain on Cloudflare and wants the dashboard reachable only by them,
so it moves from a Cloudflare Tunnel to Tailscale (ADR 0001). The owner's tailnet already serves
another app over HTTPS, so its machine name may already be taken on port 443.

2026-11-13, read from the Pi over SSH (no sudo, no Docker), names left out:

- `tailscaled` 1.102.3 runs rootless as the owner's user: a user systemd unit, enabled, with
  lingering on, in userspace networking mode. Its CLI is `~/.local/bin/tailscale`, not on the
  non-login PATH, and needs `--socket=/run/user/1000/tailscaled.sock`. So `tailscale serve` needs
  no sudo here, and `--bg` settings survive reboots with the daemon.
- The Pi's Tailscale name already serves another site on port 443 (`/` to `127.0.0.1:5080`), with
  Funnel on, so that address is public. The dashboard goes on 8443 without Funnel. Issue 04
  checks that 8443 doesn't answer from outside the tailnet.
- In userspace mode the proxy connects to `127.0.0.1:5180` from the Pi itself, so the
  loopback-only trust of forwarded headers still holds.
- `osu-rotate` is already active from an earlier deploy (`20261111-...-c5fc9b4-dirty`, the phase 1
  trial); issue 04 replaces it.
- Tailscale's source (v1.102.3, `ipn/ipnlocal/serve.go`) keeps the original `Host` header and sets
  `X-Forwarded-Proto: https`, `X-Forwarded-For` (the tailnet address) and `X-Forwarded-Host`. The
  app needs no change for the callback to be `https://<name>:8443/signin-osu`.

2026-11-13: the owner doesn't want the dashboard under the existing node's name (it would have
been `:8443` there), so it gets its own node, `osu-rotate`. Tailscale Services was the other way,
but needs the host node tagged, which would touch the existing public site. The script's parsing
of `tailscale status --json` and `serve status`, and the flags it uses (`up --hostname`,
`serve --bg --https`, `serve reset`, `tailscaled --port=0`), were checked against v1.102.3 on the
Pi, read-only.
