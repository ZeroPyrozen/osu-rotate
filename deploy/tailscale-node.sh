#!/usr/bin/env bash
# Gives the dashboard its own name on your tailnet, https://<name>.<tailnet>.ts.net, by running a
# second Tailscale node on the Pi next to any you already have (docs/adr/0001). It runs as your own
# user, in userspace networking mode, as a user systemd unit: no sudo, and nothing else changes.
#
#   bash deploy/tailscale-node.sh install [name]   # default name: osu-rotate
#   bash deploy/tailscale-node.sh status  [name]
#   bash deploy/tailscale-node.sh remove  [name]
#
# install writes the unit, starts it, joins the tailnet (it prints a login link for you to open)
# and serves https://<name>.<tailnet>.ts.net on port 443 to the app on 127.0.0.1:5180. No Funnel:
# the dashboard stays on your tailnet. Running it again changes nothing that is already in place.
#
# Needs tailscale and tailscaled (TAILSCALE_BIN, default ~/.local/bin) and lingering on, so the
# unit runs while you're logged out: sudo loginctl enable-linger "$USER".
set -euo pipefail
export LC_ALL=C

TARGET=http://127.0.0.1:5180   # ASPNETCORE_URLS in osu-rotate.service

die()  { echo "error: $*" >&2; exit 1; }
info() { echo "==> $*"; }

setup() {   # setup <name>: everything below works from these
    NAME=$1
    [[ $NAME =~ ^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$ ]] || die "the name must be lowercase letters, digits and dashes: $NAME"
    BIN=${TAILSCALE_BIN:-$HOME/.local/bin}
    UNIT=tailscaled-$NAME
    UNIT_FILE=${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/$UNIT.service
    STATE_DIR=${XDG_DATA_HOME:-$HOME/.local/share}/tailscale-$NAME
    SOCKET=${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/$UNIT.sock
}

ts() { "$BIN/tailscale" --socket="$SOCKET" "$@"; }

is_root() { [[ $EUID -eq 0 ]]; }
daemon_running() { [[ -S $SOCKET ]]; }

unit_text() {
    cat <<EOF
# Written by osu-rotate's deploy/tailscale-node.sh. A second Tailscale node, "$NAME", that serves
# the osu-rotate dashboard on the tailnet only.
[Unit]
Description=Tailscale node $NAME for osu-rotate (rootless, userspace networking)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
# --port=0 picks a free UDP port, so it never clashes with another tailscaled on this machine.
ExecStart=$BIN/tailscaled --tun=userspace-networking --port=0 --socket=%t/$UNIT.sock --statedir=$STATE_DIR
Restart=on-failure
RestartSec=5
SyslogIdentifier=$UNIT

[Install]
WantedBy=default.target
EOF
}

# The backend state from `tailscale status --json`: NeedsLogin, Running, Stopped, ...
backend_state() { ts status --json 2>/dev/null | sed -n 's/^[[:space:]]*"BackendState": "\([A-Za-z]*\)".*/\1/p' | head -n 1; }

# This node's MagicDNS name, without the trailing dot. Self comes before the peers in the JSON.
dns_name() { ts status --json 2>/dev/null | sed -n 's/^[[:space:]]*"DNSName": "\(.*\)\.",*$/\1/p' | head -n 1; }

wait_for_socket() {
    local i
    for ((i = 0; i < 30; i++)); do
        daemon_running && return 0
        sleep 1
    done
    die "$UNIT didn't start; see: journalctl --user -u $UNIT"
}

cmd_install() {
    ! is_root || die "run this as your own user, not root"
    [[ -x $BIN/tailscale && -x $BIN/tailscaled ]] || die "tailscale and tailscaled not found in $BIN (set TAILSCALE_BIN)"
    if [[ $(loginctl show-user "$USER" -p Linger 2>/dev/null) != Linger=yes ]]; then
        echo "warning: lingering is off, so the node stops when you log out. Fix: sudo loginctl enable-linger $USER" >&2
    fi

    mkdir -p "$(dirname "$UNIT_FILE")" "$STATE_DIR"
    chmod 700 "$STATE_DIR"
    if [[ ! -f $UNIT_FILE ]] || [[ $(cat "$UNIT_FILE") != "$(unit_text)" ]]; then
        info "Writing $UNIT_FILE"
        unit_text > "$UNIT_FILE"
        systemctl --user daemon-reload
    fi
    systemctl --user enable --now "$UNIT"
    wait_for_socket

    if [[ $(backend_state) != Running ]]; then
        info "Joining the tailnet as $NAME. Open the link it prints and approve the device."
        ts up --hostname="$NAME"
    fi
    [[ $(backend_state) == Running ]] || die "$NAME isn't on the tailnet yet; run this again after approving it"

    if ts serve status 2>/dev/null | grep -qF "proxy $TARGET"; then
        info "Already serving https://$(dns_name) to $TARGET"
    else
        info "Serving https://$(dns_name) to $TARGET (tailnet only)"
        ts serve --bg --https=443 "$TARGET"
    fi
    ts serve status

    cat <<EOF

Done. The dashboard's address is https://$(dns_name)
  - osu! OAuth callback:  https://$(dns_name)/signin-osu
  - Dashboard__Host:      $(dns_name)
In the Tailscale admin console, turn off key expiry for "$NAME" so it stays on the tailnet.
EOF
}

cmd_status() {
    systemctl --user --no-pager status "$UNIT" | head -n 5 || true
    daemon_running || { echo "$UNIT is not running"; return 0; }
    echo "Tailscale: $(backend_state), name: $(dns_name)"
    ts serve status
}

cmd_remove() {
    if daemon_running; then
        info "Leaving the tailnet as $NAME"
        ts serve reset || true
        ts logout || true
    fi
    systemctl --user disable --now "$UNIT" 2>/dev/null || true
    rm -f "$UNIT_FILE"
    systemctl --user daemon-reload
    rm -rf "$STATE_DIR"
    info "Removed $NAME. Delete it from the Tailscale admin console too, if it's still listed."
}

[[ ${BASH_SOURCE[0]} == "$0" ]] || return 0   # sourced by the tests: definitions only

command=${1:-}
setup "${2:-osu-rotate}"
case $command in
    install) cmd_install ;;
    status)  cmd_status ;;
    remove)  cmd_remove ;;
    *) echo "usage: $0 install|status|remove [name]" >&2; exit 2 ;;
esac
