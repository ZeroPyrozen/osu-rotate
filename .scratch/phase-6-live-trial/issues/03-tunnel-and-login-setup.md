# Cloudflare Tunnel and dashboard login

Status: needs-info

Blocked by: 01

Needs the dashboard's public hostname first (spec, Open).

Owner's steps, on the Pi and in their own accounts:

1. Install `cloudflared` on the Pi and create a tunnel with a public hostname that points at
   `http://127.0.0.1:5180`, leaving the HTTP Host Header empty.
2. Create the osu! OAuth application with the callback `https://<hostname>/signin-osu`, as
   `deploy/README.md` describes.
3. Put `Osu__ClientId`, `Osu__ClientSecret`, `Dashboard__OwnerUserId` and `Dashboard__Host` in
   `/etc/osu-rotate/secrets.env`. Leave `Bancho__Nick` and `Bancho__Password` empty for now, so
   the bot stays Idle while the login is tested.

Agent's part: turn steps 1 to 3 into a checklist or wizard script the owner runs (no secret is
typed into the agent), and add the tunnel setup to `deploy/README.md`, since it only covers the
OAuth app today.

Done when the README covers the tunnel, and the owner has the tunnel and settings in place.
