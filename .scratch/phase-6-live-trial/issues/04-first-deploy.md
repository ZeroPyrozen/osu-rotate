# First deploy on the Pi

Status: ready-for-human

Blocked by: 02, 03

The owner runs `.\deploy\deploy.ps1 -SshHost <user>@<pi>`, with the IRC credentials still empty.
Then checks, and records here:

- The service starts and `/healthz` answers. How long from `systemctl restart` to healthy.
- Memory after start (`systemctl status osu-rotate`), against `MemoryMax=768M`.
- The journal: the warnings it gives at start, and nothing unexpected.
- Logging in at `https://<hostname>`: the owner gets in; a second osu! account gets "This
  dashboard is for its owner only."; signing out works.
- In the browser's developer tools: no CSP violations on the lobby and history pages, the
  `osu-rotate` cookie is Secure and HttpOnly, and `Strict-Transport-Security` is sent.
- A request with another Host header through the tunnel (if Cloudflare lets one through) gets 400.
- A rollback (`-Rollback`) and a deploy back again both work.

Done when every check above is recorded as seen, or has its own fix issue.
