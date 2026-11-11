using System.Text.RegularExpressions;

namespace OsuRotate.Domain;

public sealed record LobbySettings
{
    // The osu! account the bot runs on. That person is the owner and may use * commands.
    public required string OwnerName { get; init; }
    public bool ShowQueueAfterMatch { get; init; } = true;
    public int QueueMessageMaxLength { get; init; } = 90;
    public TimeSpan QueueMessageCooldown { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan RoomStateRequestCooldown { get; init; } = TimeSpan.FromSeconds(15);

    // How long Bancho may take to confirm a host transfer before the bot asks for !mp settings.
    public TimeSpan HostTransferTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public VoteRule SkipVote { get; init; } = new(0.5, 2);
    // Skip votes are ignored this long after the host changes.
    public TimeSpan SkipVoteCooldown { get; init; } = TimeSpan.FromSeconds(5);
    public VoteRule StartVote { get; init; } = new(0.45, 2);
    public bool StartWhenAllReady { get; init; } = true;
    public VoteRule AbortVote { get; init; } = new(0.3, 2);
    public TimeSpan VoteMessageDelay { get; init; } = TimeSpan.FromSeconds(5);
}

// The lobby's rules. Takes one input at a time and returns the commands for Bancho. It does no I/O
// and reads the time only from the TimeProvider it is given, so every scenario can be replayed exactly.
public sealed partial class LobbyEngine
{
    private const string QueueMessageKey = "host-queue";
    private const string RoomStateKey = "room-state";
    private const string HostTransferTimer = "host-transfer";

    private readonly LobbySettings _settings;
    private readonly TimeProvider _time;
    private readonly List<LobbyCommand> _output = [];
    private readonly Dictionary<string, DateTimeOffset> _lastSent = [];
    private readonly Dictionary<string, string> _deferred = [];

    public LobbyEngine(LobbySettings settings, TimeProvider time)
    {
        _settings = settings;
        _time = time;
        Timers = new Timers(time);
        SkipVotes = new VoteCounter(settings.SkipVote);
        StartVotes = new VoteCounter(settings.StartVote);
        AbortVotes = new VoteCounter(settings.AbortVote);
        Rotation = new HostRotation(Room, DenyList, _output.Add);
    }

    public Room Room { get; } = new();
    public DenyList DenyList { get; } = new();
    public HostRotation Rotation { get; }
    public Timers Timers { get; }

    // When the next timer is due. The runner sleeps until then, or until the next event, and calls Tick.
    public DateTimeOffset? NextDue => Timers.NextDue;

    public IReadOnlyList<LobbyCommand> Tick()
    {
        Timers.FireDue();
        return Flush();
    }

    public IReadOnlyList<LobbyCommand> Handle(LobbyEvent lobbyEvent)
    {
        switch (lobbyEvent)
        {
            case PlayerJoined e:
                var joined = Room.GetOrAdd(e.Username);
                if (Room.TryAdd(joined, e.Slot, e.Team))
                {
                    Rotation.OnPlayerJoined(joined);
                    VotesOnPlayerJoined(joined);
                }
                else
                {
                    RequestRoomState();
                }

                break;

            case PlayerLeft e:
                var left = Room.GetOrAdd(e.Username);
                var leftWasHost = left == Room.Host || left == Room.PendingHost;
                if (Room.TryRemove(left))
                {
                    Rotation.OnPlayerLeft(left, fromRoomState: false);
                    VotesOnPlayerLeft(left, leftWasHost);
                }
                else
                {
                    RequestRoomState();
                }

                break;

            case PlayerMoved e:
                Room.GetOrAdd(e.Username).Slot = e.Slot;
                break;

            case PlayerTeamChanged e:
                Room.GetOrAdd(e.Username).Team = e.Team;
                break;

            case HostChanged e:
                var host = Room.GetOrAdd(e.Username);
                if (Room.TrySetHost(host))
                {
                    Rotation.OnHostChanged(host);
                    VotesOnHostChanged();
                }
                else
                {
                    RequestRoomState();
                }

                break;

            case HostCleared:
                Room.ClearHost();
                break;

            case UserNotFound:
                Room.PendingHost = null;
                break;

            case BeatmapChanging:
                Rotation.OnBeatmapChanging();
                break;

            case BeatmapChanged e:
                Room.BeatmapId = e.BeatmapId;
                break;

            case MatchStarted:
                Room.StartMatch();
                Rotation.OnMatchStarted();
                VotesOnMatchStarted();
                break;

            case AllPlayersReady when _settings.StartWhenAllReady:
                StartMatchNow();
                break;

            case CountdownStarted:
                _countdownRunning = true;
                break;

            case CountdownFinished or CountdownAborted:
                _countdownRunning = false;
                break;

            case PlayerFinished e:
                var finisher = Room.GetOrAdd(e.Username);
                finisher.Status = MatchStatus.Finished;
                if (!Room.Contains(finisher))
                {
                    RequestRoomState();
                }

                break;

            case MatchFinished:
                Room.EndMatch();
                Rotation.OnMatchFinished();
                if (_settings.ShowQueueAfterMatch)
                {
                    ShowQueue();
                }

                break;

            // "Not in progress" is also the reply to an abort with no match, so it only counts when
            // the bot thought a match was running. osu-ahr counted it either way.
            case MatchAborted:
            case MatchNotInProgress when Room.IsMatching:
                Rotation.OnMatchAborted(Room.EndMatch());
                break;

            case RoomStateReceived e:
                ApplyRoomState(e.State);
                break;

            case ChatReceived e:
                HandleChat(e.Username, e.Text);
                break;
        }

        return Flush();
    }

    public IReadOnlyList<LobbyCommand> Skip()
    {
        SkipHost();
        return Flush();
    }

    public IReadOnlyList<LobbyCommand> SkipTo(string name)
    {
        if (Room.Find(name) is { } player)
        {
            Rotation.SkipTo(player);
        }

        return Flush();
    }

    // Accepts names separated by commas, including names copied from the queue message.
    public IReadOnlyList<LobbyCommand> Reorder(string names)
    {
        var players = names.Split(',')
            .Select(name => Room.Find(name.Replace(ZeroWidthSpace, string.Empty, StringComparison.Ordinal).Trim()))
            .OfType<Player>()
            .ToList();
        Rotation.Reorder(players);
        return Flush();
    }

    public IReadOnlyList<LobbyCommand> Deny(string name)
    {
        if (DenyList.Add(name) && Room.Find(name) is { } player)
        {
            Rotation.OnDenied(player);
        }

        return Flush();
    }

    public IReadOnlyList<LobbyCommand> Allow(string name)
    {
        if (DenyList.Remove(name) && Room.Find(name) is { } player)
        {
            Rotation.OnAllowed(player);
        }

        return Flush();
    }

    private const string ZeroWidthSpace = "​";

    private void ApplyRoomState(RoomState state)
    {
        var changes = Room.Merge(state);
        foreach (var player in changes.Left)
        {
            Rotation.OnPlayerLeft(player, fromRoomState: true);
            SkipVotes.RemoveVoter(player);
            StartVotes.RemoveVoter(player);
            AbortVotes.RemoveVoter(player);
        }

        foreach (var player in changes.Joined)
        {
            VotesOnPlayerJoined(player);
        }

        if (changes.Any)
        {
            Rotation.OnRoomStateApplied(state, changes);
        }
    }

    private void HandleChat(string username, string text)
    {
        var isOwner = Player.KeyOf(username) == Player.KeyOf(_settings.OwnerName);

        // The owner is a referee, so Bancho runs their !mp commands itself.
        if (isOwner && text.StartsWith("!mp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Only players in the room count, except the owner, who may be chatting from IRC.
        var player = Room.Find(username) is { } found && Room.Contains(found) ? found : null;
        if (ChatCommand.Parse(text) is not { } command || (!isOwner && player is null))
        {
            return;
        }

        switch (command.Name)
        {
            case "!q" or "!queue":
                ShowQueue();
                return;
            case "!skip":
                OnSkipCommand(player, command.Argument);
                return;
            case "!start":
                OnStartCommand(player, isOwner, command.Argument);
                return;
            case "!stop":
                OnStopCommand(player, isOwner);
                return;
            case "!abort" when Room.IsMatching:
                OnAbortVoteCommand(player);
                return;
            case "!abort":
                OnStopCommand(player, isOwner);
                return;
        }

        if (!isOwner)
        {
            return;
        }

        switch (command.Name)
        {
            case "*start" when !Room.IsMatching:
                StartMatchNow();
                break;
            case "*abort" when Room.IsMatching:
                AbortMatchNow();
                break;
            case "*reorder" or "*order" when command.Argument.Length > 0:
                Reorder(command.Argument);
                break;
            case "*skip":
                SkipHost();
                break;
            case "*skipto" when command.Argument.Length > 0:
                SkipTo(command.Argument);
                break;
            case "*denylist":
                var match = DenyListArgument().Match(command.Argument);
                if (match.Success)
                {
                    if (match.Groups[1].Value.Equals("add", StringComparison.OrdinalIgnoreCase))
                    {
                        Deny(match.Groups[2].Value);
                    }
                    else
                    {
                        Allow(match.Groups[2].Value);
                    }
                }

                break;
        }
    }

    private void ShowQueue()
    {
        if (!CooledDown(QueueMessageKey, _settings.QueueMessageCooldown))
        {
            return;
        }

        // A zero-width space after the first letter stops the message from pinging every player.
        var names = string.Join(", ", Rotation.Queue.Select(p => p.Name.Length > 1 ? $"{p.Name[0]}{ZeroWidthSpace}{p.Name[1..]}" : p.Name));
        if (names.Length > _settings.QueueMessageMaxLength)
        {
            names = $"{names[.._settings.QueueMessageMaxLength]}...";
        }

        _output.Add(new Say($"Host order: {names}", CommandPriority.Info, QueueMessageKey));
    }

    private void Announce(string text) => _output.Add(new Say(text));

    // Sends text after a delay under a key. A later message under the same key replaces the text and,
    // unless restart is false, the delay too, so a burst of updates becomes one message.
    private void Defer(string key, string text, TimeSpan delay, bool restart)
    {
        _deferred[key] = text;
        var timer = $"say:{key}";
        if (restart || !Timers.IsRunning(timer))
        {
            Timers.Start(timer, delay, () =>
            {
                if (_deferred.Remove(key, out var message))
                {
                    _output.Add(new Say(message, CommandPriority.Info, key));
                }
            });
        }
    }

    private void CancelDeferred(string key)
    {
        Timers.Cancel($"say:{key}");
        _deferred.Remove(key);
    }

    private void RequestRoomState()
    {
        if (CooledDown(RoomStateKey, _settings.RoomStateRequestCooldown))
        {
            _output.Add(new RequestRoomState());
        }
    }

    private bool CooledDown(string key, TimeSpan cooldown)
    {
        var now = _time.GetUtcNow();
        if (_lastSent.TryGetValue(key, out var last) && now - last < cooldown)
        {
            return false;
        }

        _lastSent[key] = now;
        return true;
    }

    // Bancho may never confirm a transfer, for example when a message was lost. Ask for the room state
    // then, since the bot no longer knows who is host. A pending host who left is already cleared by
    // Room.TryRemove, so the player is still in the room here.
    private void OnHostTransferTimedOut()
    {
        if (Room.PendingHost is null)
        {
            return;
        }

        Room.PendingHost = null;
        _lastSent[RoomStateKey] = _time.GetUtcNow();
        _output.Add(new RequestRoomState());
    }

    private List<LobbyCommand> Flush()
    {
        if (_output.Any(c => c is TransferHost))
        {
            Timers.Start(HostTransferTimer, _settings.HostTransferTimeout, OnHostTransferTimedOut);
        }
        else if (Room.PendingHost is null)
        {
            Timers.Cancel(HostTransferTimer);
        }

        var commands = _output.ToList();
        _output.Clear();
        return commands;
    }

    [GeneratedRegex(@"^(add|remove)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DenyListArgument();
}
