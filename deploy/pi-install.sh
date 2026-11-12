#!/usr/bin/env bash
# Installs an osu-rotate release on the Raspberry Pi. deploy.ps1 runs this over SSH, and a copy
# is kept on the Pi for rollbacks:
#
#   sudo bash pi-install.sh install <osu-rotate-bundle.tar.gz> [label]
#   sudo /opt/osu-rotate/pi-install.sh rollback
#
# The first install provisions the Pi: an unprivileged "osu-rotate" user, the systemd service and
# /etc/osu-rotate/secrets.env. Every install unpacks into a new folder under
# /opt/osu-rotate/releases, points /opt/osu-rotate/current at it, restarts the service and waits
# for /healthz. If the new release doesn't come up, the previous one is put back.
set -euo pipefail
export LC_ALL=C

APP=osu-rotate
BINARY=OsuRotate.Web
APP_ROOT=/opt/$APP
RELEASES=$APP_ROOT/releases
CURRENT=$APP_ROOT/current
ENV_FILE=/etc/$APP/secrets.env
UNIT_FILE=/etc/systemd/system/$APP.service
HEALTH_URL=http://127.0.0.1:5180/healthz   # ASPNETCORE_URLS in osu-rotate.service
KEEP_RELEASES=${KEEP_RELEASES:-3}

stage=

log()  { echo "==> $*"; }
warn() { echo "warning: $*" >&2; }
die()  { echo "error: $*" >&2; exit 1; }

# Idempotent: safe to run on every deploy. $1 = folder with the deploy/ files from the bundle.
provision() {
    local src=$1

    command -v curl >/dev/null || die "curl is required for the health check: sudo apt install curl"

    if ! id -u "$APP" >/dev/null 2>&1; then
        log "Creating system user '$APP'"
        useradd --system --user-group --home-dir "/var/lib/$APP" --no-create-home \
            --shell /usr/sbin/nologin "$APP"
    fi
    install -d -m 755 "$APP_ROOT" "$RELEASES"
    install -d -m 750 "/etc/$APP"

    if [[ ! -e $ENV_FILE ]]; then
        log "Creating $ENV_FILE"
        install -m 600 "$src/osu-rotate.env.example" "$ENV_FILE"
    fi

    if ! cmp -s "$src/osu-rotate.service" "$UNIT_FILE"; then
        log "Installing $UNIT_FILE"
        install -m 644 "$src/osu-rotate.service" "$UNIT_FILE"
        systemctl daemon-reload
    fi
    systemctl enable --quiet "$APP"

    install -m 755 "$src/pi-install.sh" "$APP_ROOT/pi-install.sh"
}

switch_to() {
    ln -sfn "$1" "$CURRENT.new"
    mv -Tf "$CURRENT.new" "$CURRENT"   # rename is atomic, so "current" is never missing
}

# Restarts the service and waits for /healthz to answer.
restart_and_check() {
    systemctl restart "$APP" || return 1
    for _ in $(seq 1 30); do
        if curl -fsS -o /dev/null --max-time 5 "$HEALTH_URL" 2>/dev/null; then
            log "Health check OK: $HEALTH_URL"
            return 0
        fi
        systemctl is-active --quiet "$APP" || break
        sleep 1
    done
    warn "health check failed: $HEALTH_URL"
    return 1
}

# Deletes all but the newest $KEEP_RELEASES releases (names start with a UTC timestamp).
prune() {
    local current old all
    current=$(readlink -f "$CURRENT")
    shopt -s nullglob
    all=("$RELEASES"/*/)
    for old in "${all[@]:0:$((${#all[@]} > KEEP_RELEASES ? ${#all[@]} - KEEP_RELEASES : 0))}"; do
        old=${old%/}
        if [[ $old != "$current" ]]; then rm -rf -- "$old"; fi
    done
    shopt -u nullglob
}

cmd_install() {
    local bundle=${1:?usage: pi-install.sh install <osu-rotate-bundle.tar.gz> [label]}
    local label=${2:-manual}
    local release previous=
    label=${label//[^A-Za-z0-9._-]/}
    [[ -f $bundle ]] || die "bundle not found: $bundle"
    [[ $(dpkg --print-architecture) == arm64 ]] || die "this build is for arm64, but this machine is $(dpkg --print-architecture)"
    release=$RELEASES/$(date -u +%Y%m%d-%H%M%S)-$label

    install -d -m 755 "$RELEASES"
    stage=$(mktemp -d "$RELEASES/.incoming.XXXXXX")
    log "Unpacking $(basename "$release")"
    tar -xzf "$bundle" -C "$stage" --no-same-owner --no-same-permissions
    if [[ ! -f $stage/app/$BINARY || ! -f $stage/deploy/osu-rotate.service ]]; then
        die "$bundle isn't an osu-rotate bundle (expected app/$BINARY and deploy/osu-rotate.service)"
    fi
    sed -i 's/\r$//' "$stage"/deploy/*   # tolerate CRLF from a Windows checkout

    provision "$stage/deploy"

    chmod -R u=rwX,go=rX "$stage/app"
    chmod 755 "$stage/app/$BINARY"
    mv "$stage/app" "$release"

    if [[ -L $CURRENT ]]; then previous=$(readlink -f "$CURRENT"); fi
    switch_to "$release"
    if ! restart_and_check; then
        journalctl -u "$APP" -n 30 --no-pager || true
        if [[ -n $previous && -d $previous ]]; then
            log "Rolling back to $(basename "$previous")"
            switch_to "$previous"
            restart_and_check || warn "the previous release isn't healthy either"
        else
            # Nothing to go back to. Stop it, or Restart=always keeps it crash-looping.
            systemctl stop "$APP" || true
            rm -f -- "$CURRENT"
        fi
        rm -rf -- "$release"
        die "deploy failed (full log: journalctl -u $APP -n 200)"
    fi

    prune
    log "Now running $(basename "$release")"
}

# Switches to the previous release.
cmd_rollback() {
    local current target= dir
    [[ -L $CURRENT ]] || die "nothing is installed yet"
    current=$(readlink -f "$CURRENT")
    shopt -s nullglob
    for dir in "$RELEASES"/*/; do
        dir=${dir%/}
        if [[ $dir < $current ]]; then target=$dir; fi   # newest release older than current
    done
    shopt -u nullglob
    [[ -n $target ]] || die "no older release to roll back to"

    log "Rolling back from $(basename "$current") to $(basename "$target")"
    switch_to "$target"
    restart_and_check || die "rolled back, but the service isn't healthy (see: journalctl -u $APP -n 50)"
    log "Now running $(basename "$target")"
}

[[ $EUID -eq 0 ]] || die "run this as root (sudo)"
exec 9>"/run/lock/$APP-deploy.lock"
flock -n 9 || die "another deploy is already running"
trap '[[ -n $stage ]] && rm -rf -- "$stage"' EXIT

case ${1:-} in
    install)  shift; cmd_install "$@" ;;
    rollback) shift; cmd_rollback "$@" ;;
    *)        echo "usage: $0 install <osu-rotate-bundle.tar.gz> [label] | rollback" >&2; exit 2 ;;
esac
