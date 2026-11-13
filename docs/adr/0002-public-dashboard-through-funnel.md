# Make the dashboard public through Tailscale Funnel

Status: accepted, 2026-11-13. Changes ADR 0001, which kept the dashboard on the tailnet only.

## Context

ADR 0001 served the dashboard to the owner's tailnet only, from its own Tailscale node,
`osu-rotate`. On the first deploy it turned out that none of the owner's own devices was on the
tailnet: the Pi's other site is on Funnel, so it never needed one. The owner wants to open the
dashboard from any browser, without installing Tailscale on each device.

## Decision

Turn on Funnel for the `osu-rotate` node's HTTPS port 443, so `https://osu-rotate.<tailnet>.ts.net`
is reachable from the internet. Nothing else about the setup changes: the same node, the same
`tailscale serve` proxy to `127.0.0.1:5180`, the same `Dashboard__Host` and OAuth callback.

The osu! login is now the only lock. That is the model the phase 5 hardening was built for (the
original design put the dashboard on the internet behind a Cloudflare Tunnel): every route needs
the owner's osu! account except `/healthz`, `/signed-out`, `/Error`, `/not-found` and the static
files, and a test pins that list.

## Consequences

- Any device can open the address; strangers get sent to osu! and, if they log in, "This
  dashboard is for its owner only." with no session.
- A bug in the login or in an open route is exposed to everyone, not only to the owner's devices.
  This page controls a bot on the owner's own osu! account, so changes to authentication need care,
  and the open-routes test must stay.
- Funnel still connects to Kestrel from the Pi itself, so forwarded headers stay trusted from
  loopback only. Tailscale identity headers are not sent for Funnel requests; the app doesn't use
  them.
- Funnel only allows ports 443, 8443 and 10000, and the tailnet policy must give the node the
  `funnel` attribute. Both hold: the node's capabilities list `funnel-ports?ports=443,8443,10000`.
- Going back to tailnet-only: `tailscale funnel --https=443 off` on the node, then
  `bash tailscale-node.sh install` to serve it on the tailnet again. The Tailscale app on the
  owner's devices then becomes necessary again.
- The error page no longer explains how to turn on detailed errors.
