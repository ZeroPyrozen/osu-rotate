# Production hardening

Status: ready-for-agent

Blocked by: 01

Behind Cloudflare Tunnel: forwarded headers trusted only from loopback, `AllowedHosts` set from
configuration, HTTPS-only cookies, antiforgery on every form, a content security policy, and no
Blazor detailed errors in production. Document the osu! OAuth app setup and the callback URL in the
deploy README.

Done when host tests check each header and setting, and the README walks through setting up the
OAuth app.
