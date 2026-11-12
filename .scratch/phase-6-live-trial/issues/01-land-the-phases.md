# Land phases 1 to 5

Status: in-progress

The work is on five stacked local branches, and `main` is still the initial commit. Decide how it
lands: one pull request per phase into `main` in order, or one pull request for the whole stack.
Then push, and deploy from `main` from now on, so the release folder names on the Pi
(`<timestamp>-<commit>`) point at commits that exist on GitHub.

Before anything is pushed: scan the full history (not only the last diff) for the owner's
username, LAN addresses and anything secret-shaped, since the repo is public.

Done when `main` holds phases 1 to 5, the history scan is clean, and the full test suite passes on
`main`.

## Comments

2026-11-12, owner's decisions: one pull request for the whole stack (`phase-5-dashboard` into
`main`), and the commits keep the owner's Gmail address as the author email.

History scan, all branches: no usernames, LAN addresses or secrets in any file. The only matches
are the README's placeholder `pi@192.168.1.50` and the license's copyright name. The initial commit
(already on GitHub) has a different author email; it stays as it is.
