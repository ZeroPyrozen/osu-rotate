# Deploy script tests on Linux

Status: ready-for-agent

`deploy/tests/pi-install.test.sh` fails 11 checks on Windows (Git Bash), with or without recent
changes: it expects Linux symlinks and tools. It has to pass before the first real deploy, because
it covers the release switch, health check and rollback.

Run it on Linux without touching the Pi: in WSL or a Debian 13 container (the Pi's OS). Fix
whatever is really broken in `pi-install.sh`; if the test itself depends on the machine, make it
say so clearly instead of failing. Add a short note to `deploy/README.md` on how to run it.

Done when the test passes in a Debian 13 container, and the README says how to run it.
