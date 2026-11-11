# Host-kick counterattack

Status: ready-for-agent

Blocked by: 04

Left over from phase 3's lobby keeper: osu-ahr kicks a host who kicks several players (its
`hostkick_tolerance`, default 4), and at once if they kick a referee or the owner. Kicks by the host
show only in the match history, so read them from the match API. Kicks the bot made itself (AFK)
do not count.

Done when the ported behaviour is tested with recorded match events.
