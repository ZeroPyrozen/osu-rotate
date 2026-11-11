#!/usr/bin/env bash
# Tests for deploy/pi-install.sh without root, systemd or a Pi. The script's functions run against
# a temporary folder, with stand-ins for systemctl, curl, journalctl, dpkg and provisioning.
#
#   bash deploy/tests/pi-install.test.sh
#
# Needs Linux, because releases are switched with real symbolic links.

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# Everything above the root check is definitions; leave out the part that runs a command.
sed '/^\[\[ \$EUID -eq 0 \]\]/,$d' "$here/../pi-install.sh" > "$work/functions.sh"
# shellcheck source=/dev/null
source "$work/functions.sh"
set +e   # a failing check must not end the test run

RELEASES=$work/opt/releases
CURRENT=$work/opt/current
mkdir -p "$RELEASES"

fails=0
check() {   # check <what> <expected> <actual>
    if [[ $2 == "$3" ]]; then
        echo "PASS  $1"
    else
        echo "FAIL  $1: expected <$2>, got <$3>"
        fails=$((fails + 1))
    fi
}

# Stand-ins. HEALTHY decides whether the health check passes; calls are recorded.
HEALTHY=yes
calls=$work/calls
systemctl()  { echo "systemctl $*" >> "$calls"; [[ $1 != is-active ]] || [[ $HEALTHY == yes ]]; }
curl()       { [[ $HEALTHY == yes ]]; }
journalctl() { :; }
sleep()      { :; }
provision()  { :; }
dpkg()       { echo "${ARCH:-arm64}"; }
date()       { printf '20260101-%06d\n' "$((++clock))"; }   # fixed width, like the real timestamp
clock=0

bundle=$work/bundle.tar.gz
mkdir -p "$work/b/app" "$work/b/deploy"
touch "$work/b/app/OsuRotate.Web" "$work/b/deploy/osu-rotate.service"
tar -czf "$bundle" -C "$work/b" app deploy

running()  { basename "$(readlink -f "$CURRENT")"; }
releases() { (cd "$RELEASES" && echo *); }
# Each install runs in a subshell because die exits. $clock is advanced here so names stay unique.
install_as() { clock=$((clock + 1)); (set -e; cmd_install "$bundle" "$1") > "$work/log" 2>&1; }

# --- first install fails: nothing left running ------------------------------------
HEALTHY=no
install_as first-bad
check "failed first install: reports failure" 1 "$?"
check "failed first install: no current link" no "$([[ -e $CURRENT || -L $CURRENT ]] && echo yes || echo no)"
check "failed first install: release removed" '*' "$(releases)"
check "failed first install: service stopped" yes "$(grep -q 'systemctl stop osu-rotate' "$calls" && echo yes || echo no)"

# --- successful install -----------------------------------------------------------
HEALTHY=yes
install_as a
check "install: succeeds" 0 "$?"
check "install: current points at the release" yes "$([[ $(running) == *-a ]] && echo yes || echo no)"
good=$(running)

# --- failed upgrade rolls back ----------------------------------------------------
# The stand-in reports unhealthy for the new release only.
curl() { [[ $(readlink -f "$CURRENT") != *-bad ]]; }
systemctl() { echo "systemctl $*" >> "$calls"; [[ $1 != is-active ]] || [[ $(readlink -f "$CURRENT") != *-bad ]]; }
install_as bad
check "failed upgrade: reports failure" 1 "$?"
check "failed upgrade: previous release restored" "$good" "$(running)"
check "failed upgrade: new release removed" "$good" "$(releases)"
curl() { [[ $HEALTHY == yes ]]; }
systemctl() { echo "systemctl $*" >> "$calls"; [[ $1 != is-active ]] || [[ $HEALTHY == yes ]]; }

# --- wrong architecture and bad bundles are refused -------------------------------
ARCH=amd64 install_as wrong-arch
check "wrong architecture: refused" 1 "$?"
check "wrong architecture: nothing changed" "$good" "$(running)"
: > "$work/empty"; tar -czf "$work/notours.tar.gz" -C "$work" empty
clock=$((clock + 1)); (set -e; cmd_install "$work/notours.tar.gz" nope) > "$work/log" 2>&1
check "foreign bundle: refused" 1 "$?"

# --- labels can't escape the releases folder --------------------------------------
install_as 'x; rm -rf /'
check "labels are sanitised" yes "$([[ $(running) == *-xrm-rf ]] && echo yes || echo no)"

# --- old releases are pruned, the running one is kept -----------------------------
rm -rf "$work"/stage; find "$RELEASES" -maxdepth 1 -name '.incoming.*' -exec rm -rf {} +
install_as c; install_as d; install_as e
check "prune: keeps the newest three" 3 "$(releases | wc -w)"
check "prune: keeps the running release" yes "$([[ -d $RELEASES/$(running) ]] && echo yes || echo no)"

# --- rollback ---------------------------------------------------------------------
before=$(running)
(set -e; cmd_rollback) > "$work/log" 2>&1
check "rollback: succeeds" 0 "$?"
check "rollback: goes to the previous release" yes "$([[ $(running) == *-d ]] && echo yes || echo no)"
check "rollback: keeps the newer release on disk" yes "$([[ -d $RELEASES/$before ]] && echo yes || echo no)"

echo
if ((fails)); then echo "$fails check(s) failed"; exit 1; fi
echo "All checks passed"
