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
- From a device that isn't on the tailnet (a phone on mobile data, Tailscale off), the address
  doesn't answer at all.
- The Pi's other node and its public site work as before.
- A rollback (`-Rollback`) and a deploy back again both work.

Done when every check above is recorded as seen, or has its own fix issue.
