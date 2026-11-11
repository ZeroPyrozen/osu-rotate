# Bancho probe

Checks how real Bancho behaves, for the questions its documentation doesn't answer. It creates a
test room, tells you what to do in osu!stable, records what happens, and closes the room at the end,
including after Ctrl+C.

```powershell
dotnet run --project tools/OsuRotate.BanchoProbe
```

It asks for your osu! username and IRC password, which comes from
<https://osu.ppy.sh/home/account/edit#legacy-api> and is not your account password. Nothing is
saved. An osu! OAuth client ID and secret are optional and enable the match history checks. You can
also set `OSU_ROTATE_IRC_NICK`, `OSU_ROTATE_IRC_PASSWORD`, `OSU_ROTATE_CLIENT_ID` and
`OSU_ROTATE_CLIENT_SECRET`.

A full run takes about 10 minutes, 6 of which are an idle listening period. The report is written to
`artifacts/probe/`, which git ignores. It contains the IRC transcript with the password masked.

| Option | Effect |
| --- | --- |
| `--skip-match` | Skips the step where you start a match |
| `--idle-minutes N` | Changes the idle listening period (default 6) |
| `--keep-room` | Leaves the room open, to test whether an empty room survives for days |
| `--check-room ID` | Checks a kept room later, then closes it |
| `--host`, `--port` | Connects somewhere other than Bancho |
