#!/usr/bin/env bash
# Tests for deploy/tailscale-node.sh without Tailscale, systemd or a Pi. The script's functions run
# against a temporary home, with stand-ins for systemctl, loginctl and the tailscale CLI.
#
#   bash deploy/tests/tailscale-node.test.sh

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# shellcheck source=/dev/null
source "$here/../tailscale-node.sh"
set +e   # a failing check must not end the test run

fails=0
check() {   # check <what> <expected> <actual>
    if [[ $2 == "$3" ]]; then
        echo "PASS  $1"
    else
        echo "FAIL  $1: expected <$2>, got <$3>"
        fails=$((fails + 1))
    fi
}
has() { grep -qF -- "$2" "$1" && echo yes || echo no; }   # has <file> <text>

export HOME=$work/home XDG_RUNTIME_DIR=$work/run USER=owner
unset XDG_CONFIG_HOME XDG_DATA_HOME
export TAILSCALE_BIN=$work/bin
mkdir -p "$HOME" "$XDG_RUNTIME_DIR" "$TAILSCALE_BIN"
calls=$work/calls
export calls state=$work/ts-state serves=$work/ts-serves

# The tailscale CLI stand-in keeps its login state and serve config in files.
cat > "$TAILSCALE_BIN/tailscale" <<'TS'
#!/usr/bin/env bash
shift   # --socket=...
echo "tailscale $*" >> "$calls"
case "$1 $2" in
    "status --json")
        printf '{\n  "BackendState": "%s",\n  "Self": {\n    "DNSName": "osu-rotate.example.ts.net.",\n  },\n  "Peer": {\n    "x": {\n      "DNSName": "peer.example.ts.net.",\n    }\n  }\n}\n' "$(cat "$state" 2>/dev/null || echo NeedsLogin)" ;;
    "up "*)
        echo "To authenticate, visit: https://login.tailscale.com/a/x"
        [[ ${UP_FAILS:-} == yes ]] || echo Running > "$state" ;;
    "serve status") cat "$serves" 2>/dev/null ;;
    "serve --bg")   echo "|-- / proxy $4" >> "$serves" ;;
    "serve reset")  : > "$serves" ;;
    "logout ")      echo NeedsLogin > "$state" ;;
esac
TS
printf '#!/usr/bin/env bash\n' > "$TAILSCALE_BIN/tailscaled"
chmod +x "$TAILSCALE_BIN/tailscale" "$TAILSCALE_BIN/tailscaled"

systemctl()       { echo "systemctl $*" >> "$calls"; }
loginctl()        { echo "${LINGER:-Linger=yes}"; }
daemon_running()  { true; }   # no real daemon makes a socket here
is_root()         { [[ ${ROOT:-no} == yes ]]; }

run() { (set -e; setup "${NAME_ARG:-osu-rotate}"; "$@") > "$work/out" 2> "$work/err"; }

# --- first install ----------------------------------------------------------------
run cmd_install
check "install: succeeds" 0 "$?"
unit=$HOME/.config/systemd/user/tailscaled-osu-rotate.service
check "install: unit written" yes "$([[ -f $unit ]] && echo yes || echo no)"
check "install: userspace networking" yes "$(has "$unit" -- "--tun=userspace-networking")"
check "install: its own UDP port" yes "$(has "$unit" -- "--port=0")"
check "install: its own socket" yes "$(has "$unit" -- "--socket=%t/tailscaled-osu-rotate.sock")"
check "install: its own state" yes "$(has "$unit" -- "--statedir=$HOME/.local/share/tailscale-osu-rotate")"
if [[ $(uname -s) == Linux ]]; then   # Windows ignores chmod
    check "install: state is private" 700 "$(stat -c %a "$HOME/.local/share/tailscale-osu-rotate")"
fi
check "install: unit enabled and started" yes "$(has "$calls" "systemctl --user enable --now tailscaled-osu-rotate")"
check "install: joins with the name" yes "$(has "$calls" "tailscale up --hostname=osu-rotate")"
check "install: serves 443 to the app" yes "$(has "$calls" "tailscale serve --bg --https=443 http://127.0.0.1:5180")"
check "install: no funnel" no "$(has "$calls" funnel)"
check "install: prints the callback" yes "$(has "$work/out" "https://osu-rotate.example.ts.net/signin-osu")"
check "install: prints Dashboard__Host" yes "$(has "$work/out" "Dashboard__Host:      osu-rotate.example.ts.net")"
check "install: no linger warning" no "$(has "$work/err" linger)"

# --- running it again changes nothing ---------------------------------------------
: > "$calls"
run cmd_install
check "again: succeeds" 0 "$?"
check "again: unit not rewritten" no "$(has "$calls" daemon-reload)"
check "again: doesn't log in again" no "$(has "$calls" "tailscale up")"
check "again: doesn't serve twice" no "$(has "$calls" "serve --bg")"

# --- a changed unit is rewritten --------------------------------------------------
echo "# old" > "$unit"; : > "$calls"
run cmd_install
check "changed unit: rewritten" yes "$(has "$unit" -- "--port=0")"
check "changed unit: reloaded" yes "$(has "$calls" "systemctl --user daemon-reload")"

# --- refusals and warnings --------------------------------------------------------
NAME_ARG='Bad Name' run cmd_install
check "bad name: refused" 1 "$?"
ROOT=yes run cmd_install
check "root: refused" 1 "$?"
LINGER=Linger=no run cmd_install
check "no linger: still installs" 0 "$?"
check "no linger: warns" yes "$(has "$work/err" "enable-linger")"
mv "$TAILSCALE_BIN/tailscaled" "$work/td"; run cmd_install
check "missing tailscaled: refused" 1 "$?"
mv "$work/td" "$TAILSCALE_BIN/tailscaled"

# --- login not approved -----------------------------------------------------------
echo NeedsLogin > "$state"; : > "$serves"; : > "$calls"
UP_FAILS=yes run cmd_install
check "not approved: fails" 1 "$?"
check "not approved: nothing served" no "$(has "$calls" "serve --bg")"

# --- another name ------------------------------------------------------------------
NAME_ARG=lobby run cmd_install
check "other name: own unit" yes "$([[ -f $HOME/.config/systemd/user/tailscaled-lobby.service ]] && echo yes || echo no)"
check "other name: joins with it" yes "$(has "$calls" "tailscale up --hostname=lobby")"

# --- remove -----------------------------------------------------------------------
: > "$calls"
run cmd_remove
check "remove: succeeds" 0 "$?"
check "remove: unit deleted" no "$([[ -f $unit ]] && echo yes || echo no)"
check "remove: state deleted" no "$([[ -d $HOME/.local/share/tailscale-osu-rotate ]] && echo yes || echo no)"
check "remove: unit disabled" yes "$(has "$calls" "systemctl --user disable --now tailscaled-osu-rotate")"
check "remove: leaves the tailnet" yes "$(has "$calls" "tailscale logout")"
check "remove: stops serving" yes "$(has "$calls" "tailscale serve reset")"

echo
if ((fails)); then echo "$fails check(s) failed"; exit 1; fi
echo "All checks passed"
