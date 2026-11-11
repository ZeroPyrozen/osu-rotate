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

## Day-to-day

| Task | Command |
| --- | --- |
| Roll back | `.\deploy\deploy.ps1 -SshHost pi@raspberrypi.local -Rollback` |
| Check health | `ssh pi@raspberrypi.local curl -s http://127.0.0.1:5180/healthz` |
| Status | `ssh pi@raspberrypi.local systemctl status osu-rotate` |
| Follow logs | `ssh pi@raspberrypi.local journalctl -u osu-rotate -f` |
| See what's deployed | `ssh pi@raspberrypi.local readlink /opt/osu-rotate/current` |

The service listens on `127.0.0.1:5180` only. To see the page from your PC before the Cloudflare
Tunnel exists, forward the port and open <http://localhost:5180>:

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
| `/var/lib/osu-rotate/` | Data-protection keys, and later the database |
