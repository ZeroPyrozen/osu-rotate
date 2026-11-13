# First deploy on the Pi

Status: ready-for-human

Blocked by: 02, 03

The owner runs `.\deploy\deploy.ps1 -SshHost <user>@<pi>`, with the IRC credentials still empty.
Then checks, and records here:

- The service starts and `/healthz` answers. How long from `systemctl restart` to healthy.
- Memory after start (`systemctl status osu-rotate`), against `MemoryMax=768M`.
- The journal: the warnings it gives at start, and nothing unexpected.
- The osu! login page's `redirect_uri` is `https://osu-rotate.<tailnet>.ts.net/signin-osu`
  (Tailscale's source says serve keeps `Host` and sends `X-Forwarded-Proto: https`).
- Logging in at `https://osu-rotate.<tailnet>.ts.net`: the owner gets in; a second osu! account gets "This
  dashboard is for its owner only."; signing out works.
- In the browser's developer tools: no CSP violations on the lobby and history pages, the
  `osu-rotate` cookie is Secure and HttpOnly, and `Strict-Transport-Security` is sent.
- A request to the `osu-rotate` node's Tailscale IP instead of its name gets 400 (not an allowed
  host).
- From a device that isn't on the tailnet (a phone on mobile data, no Tailscale), the address
  loads through Funnel and sends you to osu!.
- The Pi's other node and its public site work as before.
- A rollback (`-Rollback`) and a deploy back again both work.

Done when every check above is recorded as seen, or has its own fix issue.

## Comments

2026-11-13: deployed `20261113-040935-e377122-dirty` (`raspberrypi.local` didn't resolve; the LAN
address worked). On the Pi: the service is active, `/healthz` answers 200, and a request with
another Host header gets 400. The dashboard address didn't load for the owner, because no client
device was on the tailnet: both Pi nodes saw only each other. The owner's other site is on Funnel,
so it never needed one. Fix: the Tailscale app on the owner's devices. The README now says so.

2026-11-13: the owner wants the dashboard reachable from any browser, so Funnel goes on for the
`osu-rotate` node (ADR 0002). Checked first, read-only: the node's capabilities allow Funnel on
443, 8443 and 10000. Added before going public: a test that pins the routes open to anyone
(`/Error`, `/healthz`, `/not-found`, `/signed-out` and static files; opening `/history` by mistake
makes it fail), and the error page no longer tells visitors how to turn on detailed errors.

2026-11-13, Funnel on; checked from outside the tailnet (the dev PC, plain HTTPS):

- `/healthz` answers 200. `/` and `/history` without a login redirect (302) to osu!.
- The osu! `redirect_uri` is `https://osu-rotate.<tailnet>.ts.net/signin-osu`: Tailscale's
  `X-Forwarded-Proto` is trusted, as its source said. `Strict-Transport-Security` is sent, which
  the app only does on HTTPS requests.
- The CSP and `X-Frame-Options: DENY` are on the response; the correlation cookie is `secure`,
  `httponly`.
- A request through Funnel with `Host: evil.example.com` gets 400.

Left for the owner: logging in, the browser console and cookie checks on the dashboard, signing
out, a second account, and the rollback and redeploy.

2026-11-13, the owner's browser checks through Funnel: logging in lands on the dashboard; the
circuit's WebSocket connects; no CSP errors on the lobby or history pages; both cookies are Secure
and HttpOnly (`osu-rotate` Lax, antiforgery Strict); signing out works. Found and fixed:

- `/favicon.ico` 404 in the console: the page now declares an empty `data:` icon (the CSP allows
  `data:` images).
- The login cookie was a browser-session cookie, so closing the browser logged the owner out
  before the 7 days the README promised. The owner chose 7 days: the owner's login is now
  persistent (sliding, 7 days), with a test on the cookie's expiry.
- Once, right after opening History, the console showed "Unsafe attempt to load URL
  https://osu.ppy.sh/oauth/authorize..." from the lobby page: a background request was answered
  with the login redirect, which the browser blocked. It didn't happen again on a second try. The
  other console lines then were osu!'s `Permissions-Policy` warnings (kept by Preserve log) and
  browser extensions; the app sends no `Permissions-Policy`. Watch for it to come back, and if it
  does, capture its Network request and initiator.

Left: memory and journal at start, a second osu! account, the rollback and redeploy (which will
carry these fixes).

