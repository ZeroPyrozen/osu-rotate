# Tailscale and dashboard login

Status: ready-for-agent

Blocked by: 01

The dashboard is served to the owner's tailnet with `tailscale serve` (ADR 0001), at
`https://<machine>.<tailnet>.ts.net[:port]`. The owner already has a tailnet with HTTPS names.

Owner's steps, on the Pi and in their own accounts:

1. Check Tailscale on the Pi (`tailscale status`); install it and join the tailnet if needed.
   Check what `tailscale serve status` already serves, to pick the port: 443 if it's free,
   otherwise another, such as 8443.
2. Serve the dashboard on port 8443: `tailscale serve --bg --https=8443 http://127.0.0.1:5180`
   (with the Pi's rootless setup, no `sudo`, and the CLI needs its `--socket`; see Comments).
   No Funnel.
3. Create the osu! OAuth application with the callback
   `https://<machine>.<tailnet>.ts.net[:port]/signin-osu`, as `deploy/README.md` describes.
4. Put `Osu__ClientId`, `Osu__ClientSecret`, `Dashboard__OwnerUserId` and `Dashboard__Host`
   (the `ts.net` name, without port) in `/etc/osu-rotate/secrets.env`. Leave `Bancho__Nick` and
   `Bancho__Password` empty for now, so the bot stays Idle while the login is tested.

Agent's part: turn steps 1 to 4 into a checklist the owner follows (no secret or tailnet name is
typed into the agent or the repo), and replace the Cloudflare Tunnel parts of `deploy/README.md`
with the Tailscale setup.

Done when the README covers Tailscale, and the owner has `tailscale serve` and the settings in
place.

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
