# Queue and host controls

Status: ready-for-agent

Blocked by: 02

UC5, UC6. Reorder the queue (move up and down, or drag), skip the host, give host to a player, start
the match now or with a countdown, and abort. These force the action regardless of votes. The
engine needs `StartMatch` and `AbortMatch` owner inputs, as the `*start` and `*abort` chat commands
already do.

Done when each control's input reaches the engine and its effect shows in the next snapshot.
