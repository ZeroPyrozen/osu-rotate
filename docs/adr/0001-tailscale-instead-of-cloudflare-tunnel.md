# Reach the dashboard through Tailscale, not a Cloudflare Tunnel

Status: accepted, 2026-11-13. Changed by ADR 0002: the dashboard is now public through Funnel.

## Context

The design (docs/system-design.html, Deployment) put the dashboard behind a Cloudflare Tunnel, so
it could be opened from anywhere. A tunnel needs a domain whose DNS Cloudflare manages, and the
owner has a Cloudflare account but no such domain. The dashboard is for the owner alone, and the
owner already uses Tailscale, with HTTPS names under their tailnet's `ts.net` domain.

## Decision

Serve the dashboard to the owner's tailnet only, with `tailscale serve` on the Pi proxying HTTPS to
Kestrel on `127.0.0.1:5180`. No Cloudflare Tunnel, no Tailscale Funnel: nothing is reachable from
the public internet.

The dashboard gets a name of its own, `osu-rotate.<tailnet>.ts.net`, on port 443: a second,
rootless Tailscale node on the Pi, called `osu-rotate`, runs next to the one the Pi already has
(`deploy/tailscale-node.sh`). `Dashboard__Host` is that name, and the osu! OAuth callback is
`https://osu-rotate.<tailnet>.ts.net/signin-osu`. osu! only redirects the owner's browser to
it, so the callback works although osu! itself can't reach the tailnet.

The osu! login stays. The tailnet decides who can reach the page; the login still decides who can
use it, so another device or person on the tailnet gets 403.

## Consequences

- Why a second node: a Tailscale node has one name, and `tailscale serve` only adds ports and
  paths under it. The Pi's node already serves another site on 443, with Funnel. A port on that
  name (`:8443`) works, but the owner wants the dashboard under its own name. Tailscale Services
  could add a name to the existing node, but a Service host must be a tagged device, and tagging
  that node would change its ownership and its Funnel rules. A second node changes nothing that
  exists and is set up like the first one: as the owner's user, userspace networking, a user
  systemd unit, no sudo. It needs key expiry turned off in the admin console.
- No domain to buy, no DNS to move. Every device the owner uses the dashboard from needs the
  Tailscale app and to be signed in to the tailnet.
- The hardening from phase 5 still applies: `tailscale serve` connects to Kestrel from the Pi
  itself, so forwarded headers are trusted only from loopback, as before.
- `tailscale serve` keeps the original `Host` header (port included) and sets
  `X-Forwarded-Proto: https`, `X-Forwarded-For` and `X-Forwarded-Host` (Tailscale v1.102.3,
  `ipn/ipnlocal/serve.go`, `addProxyForwardedHeaders`). So the OAuth callback is built as
  `https://<name>/signin-osu` with no extra setting. To confirm on the first deploy.
- Turning on HTTPS for the tailnet publishes the `ts.net` names in public certificate logs. The
  names become known; the services stay unreachable from outside the tailnet.
- The tailnet and machine names are the owner's details: like the Pi's username, they stay out of
  the public repo. Docs use `osu-rotate.<tailnet>.ts.net`.
