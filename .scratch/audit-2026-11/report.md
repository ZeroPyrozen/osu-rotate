# Repository audit, 13 November 2026

Scope: the whole repository at `main` (`db6e9df`), with the dashboard's ease of use as the main concern.

## Summary

- **10 bugs fixed**, each with a test. Two of them could leave the bot stuck or silent until someone restarted it.
- **The lobby page was rebuilt around one player list.** It was 3,342 px tall on a desktop and 5,216 px on a phone; it is now 1,161 px and 2,416 px for the same room of seven players.
- **8 smaller items are left open**, because they need a decision from the owner or evidence from a live room.
- **Tests:** 1,426 before, 1,452 after, all passing. No package has a known vulnerability.
- **Not done:** nothing is pushed or deployed, and nothing here has run against real Bancho.

The work is on two local branches, stacked:

| Branch | Holds | Commits |
| --- | --- | --- |
| `audit-bug-fixes` | Back-end fixes, one commit per fix or pair of fixes | `3c3234f`, `b1ef931`, `7d82865`, `ccb412f` |
| `friendlier-dashboard` | The dashboard work, on top of the fixes | `a86d53a`, plus this report |

One branch per fix was asked for. Several fixes touch the same files (`LobbyLifecycle.cs`, `app.css`), so separate branches off `main` would have conflicted with each other on merge. One commit per fix keeps the same audit trail.

## How it was checked

- **Read in full:** the actor, lifecycle, recovery, the engine's core file, the IRC transport, parser and reader, the outbound queue, the osu! API client, the beatmap catalog, match history, the SQLite store, login, hardening, and every dashboard component.
- **Skimmed only:** the engine's rule files (AFK, auto start, host kicks, votes, room keeping, map regulation, host rotation). They were mutation-tested in phases 3 and 4.
- **Not re-read:** the deploy scripts, the CI workflow, the history page and the Bancho probe tool.
- **Dependency scan:** `dotnet list package --vulnerable --include-transitive` reports nothing. Version 11.0.0 of the Microsoft packages exists; staying on 10 is right while the app targets .NET 10.
- **Dashboard:** run locally with a scripted lobby of seven players, at 1280 px and at 375 px, and clicked through (move in queue, kick, edit and save rules, create a room).

## Bugs fixed

### BUG-01: A room Bancho refuses to make leaves the lobby in "Creating" for good

- **Severity:** high. **Category:** functional.
- **Files:** `LobbyLifecycle.cs`, `BanchoParser.cs`, `BanchoIrcTransport.cs`, `LobbyEvents.cs`, `LobbyTrace.cs`.
- **Was:** an osu! account may have four tournament rooms open. Past that, BanchoBot answers `!mp make` with a refusal instead of a room. The bot did not recognise the refusal, so the status stayed "Creating" with no message.
- **Now:** the refusal becomes a `RoomCreateRefused` event. The lobby goes back to Idle with a notice that quotes Bancho.
- **Assumption:** the refusal is matched on how it starts, "You cannot create any more tournament matches". That text is from memory of Bancho's behaviour and has not been seen in this project's logs. If the wording differs, the old behaviour remains, and the Cancel button (new) still gets the owner out.
- **Tests:** lifecycle, parser, trace, and the transport against the Bancho simulator.

### BUG-02: A refused IRC login is invisible on the dashboard

- **Severity:** medium. **Category:** functional.
- **Was:** with a wrong IRC password the bot stops trying, by design. The dashboard showed "Idle, not connected" and nothing else.
- **Now:** a notice says the login was refused and names the two settings to check.

### BUG-03: The beatmap cache grows for as long as the bot runs

- **Severity:** medium. **Category:** reliability.
- **File:** `OsuBeatmapCatalog.cs`.
- **Was:** a cached map was dropped only when the same map was asked for again after a day. A lobby that runs for months looks up thousands of sets and replays few, so the rest stayed in memory. The Pi has no enforced memory limit (phase 6, issue 07), so this would have ended in the kernel killing the service.
- **Now:** once an hour, everything older than a day is dropped.

### BUG-04: A Retry-After date in the past throws

- **Severity:** low. **Category:** edge case.
- **File:** `OsuApiClient.cs`.
- **Was:** the wait came out negative and `Task.Delay` threw, the same class of failure as the actor crash fixed in phase 6. Callers caught it, so the cost was a lost lookup, not a crash.
- **Now:** the wait is never below zero. The new test failed with `ArgumentOutOfRangeException` before the fix.

### BUG-05: An unexpected error in a Bancho session stops the connection for good

- **Severity:** high. **Category:** reliability.
- **File:** `BanchoIrcTransport.cs`.
- **Was:** only network errors led to a reconnect. Any other exception ended the transport, and the bot stayed up but never spoke to Bancho again. The dashboard would show "Reconnecting" indefinitely.
- **Now:** any failure in a session is logged and followed by a reconnect. Wrong credentials still stop it, as before.
- **Note:** no such exception has been seen in production. The test forces one through a log sink that throws.

### BUG-06: "Close when everyone has left" does nothing in a room that is already empty

- **Severity:** low. **Category:** functional.
- **File:** `LobbyActor.cs`.
- **Was:** the check ran only after a Bancho event, which an empty room may never send.
- **Now:** it also runs when the owner turns the option on.

### BUG-07: Saving the rules silently undoes changes made in the lobby

- **Severity:** medium. **Category:** functional.
- **File:** `RulesPanel.razor`.
- **Was:** the form was filled once when the page opened. If the owner then typed `*regulation` in chat, or saved from another tab, this form kept the old values and its next save put them back.
- **Now:** an untouched form follows the lobby. A form with edits shows a notice that the rules changed underneath it.

### BUG-08: A bot with no Bancho login gives no hint on the dashboard

- **Severity:** medium. **Category:** usability.
- **Was:** Create could be clicked and the status sat at "Connecting" indefinitely. The reason was only in the journal.
- **Now:** the lobby page says the login is missing and which settings to set.

### BUG-09: Status text that is wrong with no room

- **Severity:** low. **Category:** usability.
- **Was:** "Waiting for the host." and an empty players table showed with no room open, and the player count was always out of 16 even with a kept room size of 8.
- **Now:** both show only for a room, and the count uses the kept size.

### BUG-10: The rules form spills out of its card on a phone

- **Severity:** low. **Category:** layout.
- **File:** `app.css`.
- **Was:** the form's columns had a 340 px minimum; a 375 px phone leaves 311 px.
- **Now:** a column may shrink below that.

## Dashboard changes

What made it hard to use, and what changed:

| Problem | Change |
| --- | --- |
| The same players were listed four times (queue, queue editor, players, moderation), each with different buttons. | One player list in host-queue order. Move up or down, give host, kick and never host are on each row. |
| The rules form sat under the lobby, three screens down, with Save at the very bottom. | Rules have their own page (new "Rules" link). The save bar stays at the bottom of the window. |
| Nothing said whether the rules had unsaved changes. | The bar reads "No changes", "Unsaved changes" or "Saved", and Save is off until there is something to save. |
| A star limit like 5.99 had to be hit by dragging a slider with 1,000 steps. | A number box sits beside each star slider. |
| Status words such as "Syncing" or "Reconnecting" were left unexplained. | Each status other than Open has one sentence under it. |
| With no room open, the form to create one was below the fold. | It comes first. |
| While Bancho was making a room, the only way out was a button labelled "Close lobby". | It reads "Cancel" until there is a room. |
| Chat did not stay at the newest line, and names in the bot's queue message broke in two. | Chat is a fixed-height panel pinned to the newest line; the invisible space the bot puts in names is removed on the page. |
| On a phone the top bar wrapped onto two lines. | It fits on one. |

## Left open

None of these is fixed. Each needs a decision or more evidence.

| ID | Severity | What | Why it is open |
| --- | --- | --- | --- |
| OPEN-01 | Low | A dashboard chat line that starts with `!` is sent as "Bot: !…", but the dashboard shows it as typed. So `!mp timer 60` typed there does not run, and the page does not say so. | The prefix exists so player text can never run as a referee command. Whether the owner's own lines should be exempt is the owner's call. |
| OPEN-02 | Low | After signing out, another tab that is already open keeps working until its connection drops. | This is how Blazor Server holds a session. Closing it needs a per-circuit check; worth it only if the dashboard is used on shared machines. |
| OPEN-03 | Low | With a wrong osu! API secret, every map lookup asks for a token again. | Harmless at lobby speed. A back-off would be cleaner. |
| OPEN-04 | Low | A match-history read started for one room can deliver its kicks to the next room if the room is replaced in between. | A narrow race with a small effect; not seen. |
| OPEN-05 | Low | If the engine throws halfway through an event, the commands it had queued go out with the next event. | No such exception is known. Clearing the queue on failure is a small change once there is a case to test against. |
| OPEN-06 | Info | If the connection drops between `!mp make` and Bancho's answer, the room exists but the bot makes another. | Would need the match API to find rooms by owner; one of the four room slots is lost until that room is closed. |
| OPEN-07 | Info | Chat times are in the Pi's time zone, not the browser's. | Converting in the browser needs script, which the content security policy keeps to Blazor's own. |
| OPEN-08 | Info | The current map is shown as a number, not a title. | The title is known to the engine but is not in the dashboard's snapshot yet. |

Still open from before this audit: the 768M memory limit is not enforced on the Pi (phase 6, issue 07).

## Patterns worth guarding against

- **Waits computed from two clock reads.** Two places subtracted times and passed the result to `Task.Delay` (the actor, fixed in phase 6, and the API client, fixed here). Both could go negative. A shared helper that clamps to zero, used everywhere a wait is computed, would end the pattern.
- **States with no way out.** "Creating" depended on Bancho answering. Any state that waits on a remote answer should have a refusal path and a way for the owner to leave it.
- **Catching a list of exception types in a loop that must not end.** The reconnect loop named the errors it expected. For a loop whose job is to keep running, the default should be to carry on and log.
- **Forms filled once from live data.** Any form over live state needs to say what happens when that state moves.

## Suggested next steps

1. Deploy and try the new lobby page with real players; the roster has not been used in a live room.
2. Trigger BUG-01 on purpose once (open four rooms, then create a fifth) to confirm Bancho's wording.
3. Decide OPEN-01.
4. Add a memory figure to the journal once an hour, so the week soak (issue 07) has numbers.
