# Production hardening

Status: done

Blocked by: 01

Behind Cloudflare Tunnel: forwarded headers trusted only from loopback, `AllowedHosts` set from
configuration, HTTPS-only cookies, antiforgery on every form, a content security policy, and no
Blazor detailed errors in production. Document the osu! OAuth app setup and the callback URL in the
deploy README.

Done when host tests check each header and setting, and the README walks through setting up the
OAuth app.

## Outcome

`src/OsuRotate.Web/Hosting/Hardening.cs`, first in the pipeline:

- Forwarded headers: only `X-Forwarded-For` and `X-Forwarded-Proto`, one hop, and only from
  127.0.0.0/8 and ::1 (cloudflared on the Pi). cloudflared keeps the Host header, so it is not
  forwarded. With the scheme trusted, the OAuth callback is built as `https://<host>/signin-osu`.
- Allowed hosts: `Dashboard:Host` (new setting) plus localhost, 127.0.0.1 and [::1], for the deploy
  health check and the SSH port forward. `"AllowedHosts": "*"` is gone from appsettings.json.
  Without `Dashboard__Host`, production logs a warning and answers on loopback only.
- Cookies: the login and correlation cookies were already HTTPS-only outside development; the
  antiforgery cookie now is too.
- Antiforgery: the only form that posts over HTTP is signing out, and it checks its token. The
  other forms submit through the circuit; before it connects they would only GET the page.
- Headers on every response (added as it starts, so error pages keep them): a CSP of
  `'self'` only, with no inline script or style, no framing, no plugins; `nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, HSTS on HTTPS outside development, and
  no `Server` header.
- To fit the CSP: `<ImportMap />` (an inline script, unused) is removed, and the per-day stats bars
  are `<meter>` elements instead of spans with a `style` width.
- Blazor detailed errors are on only in development, whatever `DetailedErrors` says.

`deploy/README.md` walks through the osu! OAuth app, the user id, the four settings, the tunnel's
service URL, and what each login error means. `Dashboard__Host` is in the env example.

Checked in a real browser (temporarily opened `/history` to anonymous visitors on a local run, then
reverted): with the CSP in force the circuit connects over WebSocket, changing the stats window and
paging re-render, and the console shows no policy violations.

Tests: `HardeningTests` runs the host in Production and checks each setting: forwarded headers
from IPv4 and IPv6 loopback trusted and from elsewhere ignored (via the OAuth callback URL and
HSTS), the forwarding options, the allowed hosts with and without `Dashboard:Host`, secure
cookies, every security header on pages, health, sign-out and 404 responses, rendered pages with
no inline script or style, forms (rendered and in source), the sign-out token, and detailed
errors in Production and Development.

Mutation check: 25 mutants, all caught. One first survived (inline-style bars back on the stats)
because the page test had no matches to draw; it now seeds one. One needed rewriting to compile.

Not verified: the real Cloudflare Tunnel (phase 6). `deploy/tests/pi-install.test.sh` fails 11
checks on Windows with or without this change; it needs Linux.
