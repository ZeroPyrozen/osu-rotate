# Recovery on real systems

Status: ready-for-human

Blocked by: 05

With players in the room, the owner:

1. Restarts the service mid-match (`sudo systemctl restart osu-rotate`).
2. Restarts it between matches.
3. Reboots the Pi.
4. Cuts the Pi's network for about a minute.
5. Kicks the bot's host role by hand a few times, to see the host-kick counterattack and the
   `player-kicked` events from the match API.

Each time, record: how long until the bot is back in the room, whether the queue and host are
the same as before, whether a running match was told apart from a finished one (the match API's
`current_game_id` and end time), and what players saw in chat.

Done when each case brings back the same room and queue, or has its own fix issue.
