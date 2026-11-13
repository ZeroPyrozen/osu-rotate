# Deploying to the Raspberry Pi

One command builds on your PC, copies the build to the Pi over SSH and (re)starts it as a systemd
service. The build is self-contained, so the Pi needs no .NET install.

```powershell
.\deploy\deploy.ps1 -SshHost pi@raspberrypi.local
```

If `raspberrypi.local` stops resolving, use the Pi's LAN address instead (for example `pi@192.168.1.50`).

The terminal stays interactive so `sudo` on the Pi can ask for your password. The first deploy
creates an unprivileged `osu-rotate` user, installs and enables the service, and creates
`/etc/osu-rotate/secrets.env`.

Each deploy goes into a new folder under `/opt/osu-rotate/releases/`, then `/opt/osu-rotate/current`
is switched to it and `/healthz` is checked. If that fails, the previous release is restored. The
last 3 releases are kept.

`deploy/tests/pi-install.test.sh` tests the release switch, health check, rollback and pruning
without a Pi. It needs Linux or WSL, since it uses real symbolic links; in Git Bash on Windows it
stops with exit code 2 and says so. CI runs it on Ubuntu and on Debian 13 for every push.

## Day-to-day

| Task | Command |
| --- | --- |
| Roll back | `.\deploy\deploy.ps1 -SshHost pi@raspberrypi.local -Rollback` |
| Check health | `ssh pi@raspberrypi.local curl -s http://127.0.0.1:5180/healthz` |
| Status | `ssh pi@raspberrypi.local systemctl status osu-rotate` |
| Follow logs | `ssh pi@raspberrypi.local journalctl -u osu-rotate -f` |
| See what's deployed | `ssh pi@raspberrypi.local readlink /opt/osu-rotate/current` |

The service listens on `127.0.0.1:5180` only. To see the page from your PC before `tailscale serve`
is set up, forward the port and open <http://localhost:5180>:

```powershell
ssh -L 5180:127.0.0.1:5180 pi@raspberrypi.local
```

## What lives where on the Pi

| Path | Contents |
| --- | --- |
| `/opt/osu-rotate/releases/<timestamp>-<commit>/` | One folder per deploy, read-only to the app |
| `/opt/osu-rotate/current` | Symlink to the running release |
| `/opt/osu-rotate/pi-install.sh` | Copy of the install script, for rollbacks |
| `/etc/osu-rotate/secrets.env` | Settings and secrets (root only). Deploys never overwrite it |
| `/etc/systemd/system/osu-rotate.service` | Replaced on every deploy. Use `sudo systemctl edit osu-rotate` for local changes |
| `/var/lib/osu-rotate/` | Data-protection keys and the database (`rotate.db`, with its `-wal` and `-shm` files) |

## Connecting the bot

The bot reads its settings from `/etc/osu-rotate/secrets.env` (see `osu-rotate.env.example`).
Deploys never overwrite that file, so after a deploy that adds settings, add them by hand:

```bash
sudo nano /etc/osu-rotate/secrets.env
sudo systemctl restart osu-rotate
```

| Setting | What it is |
| --- | --- |
| `Bancho__Nick`, `Bancho__Password` | Your osu! username and IRC password. Without both, the bot stays Idle and does not connect. |
| `Osu__ClientId`, `Osu__ClientSecret` | An osu! OAuth application, for map checks, recovery and the dashboard login. Without it, maps are let through and nobody can log in. |
| `Dashboard__OwnerUserId` | Your osu! user id. Only this account may use the dashboard. |
| `Dashboard__Host` | The Pi's Tailscale name, `<machine>.<tailnet>.ts.net`, without `https://` or port. Other hostnames get 400 Bad Request. |

The journal says which are missing when the service starts (`journalctl -u osu-rotate`).
On a restart, the bot rejoins the room it had open and restores the queue.

## Setting up the dashboard login

The dashboard is only on your tailnet: `tailscale serve` on the Pi gives it an HTTPS address that
only your own Tailscale devices can open, and only your osu! account can log in
(`docs/adr/0001-tailscale-instead-of-cloudflare-tunnel.md`). Nothing is reachable from the internet.

Below, `<name>` is the Pi's Tailscale name, `<machine>.<tailnet>.ts.net`, and `<port>` the HTTPS
port you pick in step 1. Keep both out of this repo; they belong only on the Pi and in osu!.

### 1. Serve the dashboard on your tailnet

Your tailnet needs MagicDNS and HTTPS certificates turned on (Tailscale admin console, **DNS**).
On the Pi:

```bash
tailscale status          # the Pi is on your tailnet; install Tailscale first if it isn't
tailscale serve status    # what the Pi already serves
```

Pick the port. If `tailscale serve status` shows nothing on `:443`, use 443 and the address is
`https://<name>`. If another app already has 443, use another port such as 8443, and the address
is `https://<name>:8443`.

```bash
sudo tailscale serve --bg --https=<port> http://127.0.0.1:5180
```

If `tailscaled` runs rootless as your own user (userspace networking), leave out `sudo` and point
the CLI at your daemon's socket, for example
`~/.local/bin/tailscale --socket=/run/user/1000/tailscaled.sock serve ...`.

`--bg` keeps it after reboots. Don't use `tailscale funnel` on this port, which would publish the
dashboard to the internet. Funnel is set per port, so another site on the same name can stay public. To stop serving it: `sudo tailscale serve --https=<port> off`.

### 2. Create the osu! OAuth application

1. Sign in at osu.ppy.sh and open <https://osu.ppy.sh/home/account/edit#oauth>.
2. Under **OAuth**, choose **New OAuth Application**.
3. Give it a name, for example `osu-rotate`.
4. Set **Application Callback URLs** to exactly the dashboard's address plus `/signin-osu`:
   `https://<name>/signin-osu` on port 443, or `https://<name>:<port>/signin-osu` on any other.
   No trailing slash.
5. Choose **Register application**. Keep the page open: it shows the **Client ID** and
   **Client Secret**. The secret stays on that page; never paste it anywhere but the Pi.

osu! never has to reach your tailnet: it only sends your browser to that address after you
approve. The same application serves the bot's map checks, so you need only one.

### 3. Find your osu! user id

Open your osu! profile. The number in the address, `https://osu.ppy.sh/users/<number>`, is your id.

### 4. Put the settings on the Pi

```bash
sudo nano /etc/osu-rotate/secrets.env
```

Set these four lines (remove the `#` in front of each). `Dashboard__Host` is the name only, with
no `https://` and no port:

```bash
Osu__ClientId=<Client ID>
Osu__ClientSecret=<Client Secret>
Dashboard__OwnerUserId=<your user id>
Dashboard__Host=<machine>.<tailnet>.ts.net
```

Then restart:

```bash
sudo systemctl restart osu-rotate
```

The app trusts the proxy's `X-Forwarded-Proto` and `X-Forwarded-For` headers only when they come
from the Pi itself, which is where `tailscale serve` connects from. That is how it knows you used
HTTPS, and builds the callback URL with `https://`.

### 5. Log in

From a device on your tailnet, open the address from step 1. You are sent to osu! to approve the
application, then back to the dashboard. Anyone else who logs in gets "This dashboard is for its
owner only." and no session.

If something goes wrong:

| What you see | What to check |
| --- | --- |
| The page doesn't load at all | This device isn't on your tailnet, or `tailscale serve status` doesn't show the port. |
| "osu! login is not set up." | One of `Osu__ClientId`, `Osu__ClientSecret` or `Dashboard__OwnerUserId` is missing. |
| osu! says the redirect URI is invalid | The callback in step 2 must match the address in the browser plus `/signin-osu`, port included. |
| 400 Bad Request on every page | `Dashboard__Host` doesn't match the name in the address bar. |
| "The osu! login did not complete." | Usually the client secret is wrong, or the login took too long. Try again. |

### What the app does to stay safe

- Cookies are sent over HTTPS only, can't be read by scripts, and the login lasts 7 days.
- Every form that posts carries an antiforgery token.
- Every response carries a content security policy that allows only the app's own scripts and
  styles, no framing, and no plugins. HTTPS responses also carry `Strict-Transport-Security`.
- Error details stay in the journal and are never sent to the browser.
