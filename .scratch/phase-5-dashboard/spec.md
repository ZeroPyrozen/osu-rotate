# Phase 5: dashboard

A Blazor Server dashboard for the owner, on top of phase 4's `LobbyRuntime`: log in with osu!, watch
the lobby live, run its lifecycle, control the queue and host, chat and moderate, edit the rules,
and see the history. Design: `docs/system-design.html` (use cases UC1 to UC10, figure 8, Security).

Done when a full session can be run from the browser, and a non-owner gets 403.

## Decisions carried in

- osu! OAuth authorization code flow, scope `identify`. Only the configured osu! user id gets a
  session; anyone else gets 403 and no cookie. The osu! token is thrown away after the check.
- HttpOnly, Secure, SameSite=Lax cookie; data-protection keys persist (phase 1 already does).
- Kestrel stays on loopback behind Cloudflare Tunnel; forwarded headers only from loopback.
- The dashboard reads snapshots and posts `LobbyInput`s. It never touches the engine directly.
- Chat is shown live but never written to disk.

## Open

- The dashboard's public hostname. The osu! OAuth app's callback URL must match it. Everything is
  configuration, so this does not block building; a local callback works for development.

## Issues

01 comes first: every page needs the login. The rest are independent of each other.
