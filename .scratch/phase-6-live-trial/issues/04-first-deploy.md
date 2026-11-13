# First deploy on the Pi

Status: done

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

2026-11-13, service and journal on the Pi, read-only (release `20261113-040935-e377122-dirty`; the
rollback and the redeploy of the merged fixes are still to do):

- Start: systemd "Starting" to "Now listening" in about 1 s; the bot reports started 1 s later.
  No restarts since.
- Memory: 191 MB resident, 17 threads, after 23 minutes idle; the Pi has 6.8 GB available. CPU:
  11 s in that time.
- `MemoryMax=768M` is not enforced: the Pi's kernel has no memory cgroup controller
  (`cgroup.controllers` lists only `cpuset cpu io pids`), so systemd can't track or cap the
  service's memory. Turning it on needs `cgroup_enable=memory` on the kernel command line and a
  reboot; the owner decides. Until then, memory is watched by hand in issue 07.
- Journal at start: the expected "Bancho__Nick or Bancho__Password is not set" (credentials left
  empty on purpose), and no "Osu__ClientId ..." warning, so the OAuth app settings are read. No
  `Dashboard__Host` warning either. One more: Data Protection's "No XML encryptor configured", so
  the login-cookie keys are stored unencrypted in `/var/lib/osu-rotate/keys`, which only the
  service user (and root) can read. Accepted for now.
- Fixed in the log text: the startup warnings named `Bancho:Nick` and `Osu:ClientId` in the
  colon form, unlike the `__` form the secrets file uses, and "Saved room: (null)" now reads
  "Saved room: none".

2026-11-13, rollback and redeploy, run from the agent in the owner's terminal (the owner typed the
sudo password each time):

- `-Rollback` went from `20261113-040935-e377122-dirty` back to `20261111-025245-c5fc9b4-dirty`;
  health check OK.
- Deploying merged `main` gave `20261113-043656-570b703`, the first clean label; health check OK.
  The service restarted in under a second, the bot reported started a second later, 166 MB
  resident right after start, no restarts.
- From outside: `/healthz` 200, the login redirect still has the `https://` callback, and the
  public error page has the empty icon and no "Development" text.
- The startup log still shows "Bancho:Nick" and "Saved room: (null)": that wording fix is on a
  branch, not yet merged.

Left: a second osu! account (owner, if they have one), and the persistent login cookie seen in
the browser.

2026-11-13, closed. After logging in again on `570b703`, the `osu-rotate` cookie shows an expiry
date instead of "Session" (the 7-day login), and the console shows no errors (the favicon 404 is
gone). A second osu! account was not tried live; `OwnerLoginTests` covers another user getting 403
and no cookie, against the fake osu!. Carried to issue 07: memory isn't capped (no memory cgroup
controller on the Pi). Carried to issue 08: watch for the one-off "Unsafe attempt to load URL".

