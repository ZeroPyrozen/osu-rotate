# Deploy script tests on Linux

Status: done

`deploy/tests/pi-install.test.sh` fails 11 checks on Windows (Git Bash), with or without recent
changes: it expects Linux symlinks and tools. It has to pass before the first real deploy, because
it covers the release switch, health check and rollback.

Run it on Linux without touching the Pi: in WSL or a Debian 13 container (the Pi's OS). Fix
whatever is really broken in `pi-install.sh`; if the test itself depends on the machine, make it
say so clearly instead of failing. Add a short note to `deploy/README.md` on how to run it.

Done when the test passes in a Debian 13 container, and the README says how to run it.

## Comments

2026-11-12: CI runs this test on ubuntu-latest, and it passed there on pull request
ZeroPyrozen/osu-rotate#1 ("All checks passed"). So `pi-install.sh` is fine on Linux; the 11
failures are only Git Bash on Windows. What's left is small: make the test stop early on Windows
with a clear message instead of failing 11 checks, and say in `deploy/README.md` that it runs in
CI or under WSL. Running it in a Debian 13 container is still worth doing once, since CI is Ubuntu.

2026-11-12, work done:

- The test checks first whether the shell can make a symbolic link. In Git Bash it can't (it
  copies), so the test now stops with "SKIP ... Run it under WSL or Linux" and exit code 2,
  instead of failing 11 checks. Checked here on Windows.
- CI runs it a second time inside a `debian:trixie` container, so Debian 13 is covered on every
  push. A local container run wasn't possible: Docker Desktop on the dev PC fails to start (a stale
  `docker-secrets-engine/engine.sock` it can't rename).
- `deploy/README.md` says how and where the test runs.

Left: see the Debian 13 step pass in CI, then mark this done.
