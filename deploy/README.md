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

The service listens on `127.0.0.1:5180` only. To see the page from your PC before its Tailscale node
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
| `Dashboard__Host` | The dashboard's Tailscale name, `osu-rotate.<tailnet>.ts.net`, without `https://`. Other hostnames get 400 Bad Request. |

The journal says which are missing when the service starts (`journalctl -u osu-rotate`).
On a restart, the bot rejoins the room it had open and restores the queue.

## Setting up the dashboard login

The dashboard has a name of its own: a second Tailscale node on the Pi, called `osu-rotate`, serves
it at `https://osu-rotate.<tailnet>.ts.net`, and Tailscale Funnel makes that address reachable
from any browser. Only your osu! account can log in; everyone else gets "for its owner only"
(`docs/adr/0001-tailscale-instead-of-cloudflare-tunnel.md`,
`docs/adr/0002-public-dashboard-through-funnel.md`). Any Tailscale node you already run on the Pi
is left alone.

Below, `<name>` is that address's name, `osu-rotate.<tailnet>.ts.net`. Keep your tailnet's name out
of this repo; it belongs only on the Pi and in osu!.

### 1. Give the dashboard its own node on your tailnet

Your tailnet needs MagicDNS and HTTPS certificates turned on (Tailscale admin console, **DNS**).
Until step 1b turns Funnel on, only devices on your tailnet (with the Tailscale app signed in)
can open the address. The Pi needs `tailscale` and `tailscaled` in `~/.local/bin` (or set `TAILSCALE_BIN`), and
lingering on, so the node keeps running while you're logged out:

```bash
loginctl show-user "$USER" -p Linger   # should say Linger=yes
sudo loginctl enable-linger "$USER"     # only if it doesn't
```

Copy `deploy/tailscale-node.sh` to the Pi and run it as yourself, without `sudo`:

```bash
bash tailscale-node.sh install
```

It writes a user systemd unit, `tailscaled-osu-rotate`, starts it, and joins your tailnet as
`osu-rotate`: open the login link it prints and approve the device. Then it serves
`https://<name>` on port 443 to the app on `127.0.0.1:5180`, without Funnel, and prints the
callback URL and `Dashboard__Host` for the next steps. Running it again changes nothing that's
already in place. `bash tailscale-node.sh status` shows how it's doing, and
`bash tailscale-node.sh remove` takes it all away.

In the Tailscale admin console, open the new `osu-rotate` machine and choose **Disable key
expiry**, so it doesn't drop off your tailnet after a few months.

### 1b. Make it reachable from anywhere (Funnel)

Your tailnet's policy must allow Funnel for the node (the admin console's **Access controls**
gives members the `funnel` attribute when you first turn Funnel on anywhere). Then, on the Pi:

```bash
~/.local/bin/tailscale --socket=/run/user/$(id -u)/tailscaled-osu-rotate.sock funnel --bg --https=443 http://127.0.0.1:5180
```

`bash tailscale-node.sh status` should now show `(Funnel on)` for the address. It can take a
minute or two before the name answers from outside. To make it tailnet-only again:

```bash
~/.local/bin/tailscale --socket=/run/user/$(id -u)/tailscaled-osu-rotate.sock funnel --https=443 off
bash tailscale-node.sh install
```

### 2. Create the osu! OAuth application

1. Sign in at osu.ppy.sh and open <https://osu.ppy.sh/home/account/edit#oauth>.
2. Under **OAuth**, choose **New OAuth Application**.
3. Give it a name, for example `osu-rotate`.
4. Set **Application Callback URLs** to exactly `https://<name>/signin-osu`, as
   `tailscale-node.sh` printed it. No port and no trailing slash.
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
Dashboard__Host=osu-rotate.<tailnet>.ts.net
```

Then restart:

```bash
sudo systemctl restart osu-rotate
```

The app trusts the proxy's `X-Forwarded-Proto` and `X-Forwarded-For` headers only when they come
from the Pi itself, which is where the `osu-rotate` node's `tailscale serve` connects from. That is how it knows you used
HTTPS, and builds the callback URL with `https://`.

### 5. Log in

Open `https://<name>`. The sign-in page's **Log in with osu!** sends you to osu! to approve the
application (the first time), then back to the page you wanted. Anyone else who logs in lands on
"Not allowed: this dashboard is for its owner only." with no session.

If something goes wrong:

| What you see | What to check |
| --- | --- |
| The page doesn't load at all | The node isn't up or Funnel is off: `bash tailscale-node.sh status` on the Pi should show `(Funnel on)`. Right after turning Funnel on, wait a minute or two. |
| "osu! login is not set up." | One of `Osu__ClientId`, `Osu__ClientSecret` or `Dashboard__OwnerUserId` is missing. |
| osu! says the redirect URI is invalid | The callback in step 2 must be exactly `https://<name>/signin-osu`. |
| 400 Bad Request on every page | `Dashboard__Host` doesn't match the name in the address bar. |
| "The osu! login did not complete." | Usually the client secret is wrong, or the login took too long. Try again. |

### What the app does to stay safe

- Cookies are sent over HTTPS only and can't be read by scripts. The login survives closing the
  browser and lasts 7 days, renewed while you keep using the dashboard.
- Every form that posts carries an antiforgery token.
- Every response carries a content security policy that allows only the app's own scripts and
  styles, no framing, and no plugins. HTTPS responses also carry `Strict-Transport-Security`.
- Error details stay in the journal and are never sent to the browser.
