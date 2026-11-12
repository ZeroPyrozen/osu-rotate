using System.Globalization;
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

    // How long BanchoBot may take to answer a !stat.
    public TimeSpan StatReplyTimeout { get; init; } = TimeSpan.FromSeconds(5);

    // The host AFK check. Zero interval turns it off.
    public TimeSpan HostAfkFirstCheck { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan HostAfkCheckInterval { get; init; } = TimeSpan.FromSeconds(90);
    public bool SkipAfkHost { get; init; } = true;

    public bool AfkKickEnabled { get; init; } = true;
    public int AfkKickThreshold { get; init; } = 6;
    public TimeSpan AfkKickCooldown { get; init; } = TimeSpan.FromSeconds(30);

    public bool AutoStartEnabled { get; init; }
    public bool AutoStartClearsHost { get; init; }
    public TimeSpan AutoStartDelay { get; init; } = TimeSpan.FromSeconds(90);

    // Once this share of players has finished, a match still running after the delay is checked. Zero
    // delay turns it off.
    public double StuckMatchFinishedShare { get; init; } = 0.5;
    public TimeSpan StuckMatchDelay { get; init; } = TimeSpan.FromSeconds(30);
    public bool AbortStuckMatch { get; init; } = true;

    public MapRegulation Regulation { get; init; } = new();

    public RoomKeeping Keeping { get; init; } = new();

    // Said for !info and !help. ${version} becomes BotVersion. Lines are split on \n.
    public string InfoMessage { get; init; } =
        "- osu-rotate ${version} -\n"
        + "- The host rotation is managed with a queue. Players join the back of the queue when they enter the lobby and go back there once their map has been played.\n"
        + "- Commands: !q (queue), !skip, !start, !abort, !r (regulation), !update, !mirror, !rank";
    public TimeSpan InfoMessageCooldown { get; init; } = TimeSpan.FromSeconds(60);

    // Says the info message this often. Only intervals over 3 minutes count; zero turns it off.
    public TimeSpan InfoAnnouncementInterval { get; init; } = TimeSpan.Zero;

    // The running build, filled in by the host.
    public string BotVersion { get; init; } = "0.0.0";
}

// The lobby's rules. Takes one input at a time and returns the commands for Bancho. It does no I/O
// and reads the time only from the TimeProvider it is given, so every scenario can be replayed exactly.
public sealed partial class LobbyEngine
{
    private const string QueueMessageKey = "host-queue";
    private const string RoomStateKey = "room-state";
    private const string HostTransferTimer = "host-transfer";

    private LobbySettings _settings;
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
        _slotKeeper = new SlotKeeper(time.GetUtcNow()) { Size = settings.Keeping.Size };
    }

    public LobbySettings Settings => _settings;

    // Rules can change while the lobby runs. Running timers keep their old delay.
    public void UpdateSettings(LobbySettings settings)
    {
        _settings = settings;
        SkipVotes.Rule = settings.SkipVote;
        StartVotes.Rule = settings.StartVote;
        AbortVotes.Rule = settings.AbortVote;
        _slotKeeper.Size = settings.Keeping.Size;
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
            case RoomCreated or RoomJoined:
                KeeperOnRoomEntered();
                StartInfoAnnouncement();
                if (lobbyEvent is RoomJoined)
                {
                    // A room joined after a restart may have referees the bot has not seen added.
                    _output.Add(new RequestReferees());
                }

                break;

            // The list that follows is complete, so it replaces what was known.
            case RefereeListStarted:
                Room.ClearReferees();
                break;

            case RefereeAdded e:
                Room.AddReferee(e.Username);
                break;

            case RefereeRemoved e:
                Room.RemoveReferee(e.Username);
                break;

            case PlayerJoined e:
                var joined = Room.GetOrAdd(e.Username);
                if (Room.TryAdd(joined, e.Slot, e.Team))
                {
                    Rotation.OnPlayerJoined(joined);
                    VotesOnPlayerJoined(joined);
                    AfkOnPlayerJoined(joined);
                    KeeperOnPlayerJoined(e.Slot);
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
                    AfkOnPlayerLeft(left);
                    KeeperOnPlayerLeft(left.Slot);
                    CheckStuckMatch();
                }
                else
                {
                    RequestRoomState();
                }

                break;

            case PlayerMoved e:
                var moved = Room.GetOrAdd(e.Username);
                var from = moved.Slot;
                moved.Slot = e.Slot;
                KeeperOnPlayerMoved(from, e.Slot);
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
                    StartHostAfkCheck(first: true);
                    MapsOnHostChanged();
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
                MapsOnBeatmapChanging();
                StartHostAfkCheck(first: false);
                AutoStartOnBeatmapChanging();
                break;

            case BeatmapChanged e:
                Room.BeatmapId = e.BeatmapId;
                InfoOnBeatmapChanged(e);
                _mapChangedSinceMatch = true;
                StartHostAfkCheck(first: false);
                MapsOnBeatmapChanged(e.BeatmapId, e.Title);
                AutoStartOnBeatmapChanged(e.BeatmapId);
                break;

            case BeatmapLookedUp e:
                InfoOnLookedUp(e.BeatmapId, e.Result);
                MapsOnLookedUp(e.BeatmapId, e.Result);
                break;

            case MatchStarted:
                Room.StartMatch();
                HistoryOnMatchStarted();
                Rotation.OnMatchStarted();
                VotesOnMatchStarted();
                AfkOnMatchStarted();
                MapsOnMatchStarted();
                _lastPlayedBeatmapId = Room.BeatmapId;
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
                    break;
                }

                AfkOnPlayerFinished(finisher, e.Score);
                HistoryOnPlayerFinished(finisher, e.Score, e.Passed);
                CheckStuckMatch();

                break;

            case MatchFinished:
                Room.EndMatch();
                HistoryOnMatchEnded(MatchOutcome.Finished);
                StopStuckMatchCheck();
                AfkOnMatchFinished();
                Rotation.OnMatchFinished();
                KeeperOnMatchFinished();
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
                HistoryOnMatchEnded(MatchOutcome.Aborted);
                StopStuckMatchCheck();
                AfkOnMatchAborted();
                break;

            case PlayerStatReceived e:
                AfkOnStat(e.Stat, e.ViaPrivateMessage);
                StuckMatchOnStat(e.Stat);
                break;

            case RoomStateReceived e:
                ApplyRoomState(e.State);
                KeeperOnRoomState(e.State);
                break;

            case ChatReceived e:
                HandleChat(e.Username, e.Text);
                break;

            case PrivateMessageReceived e:
                OnPrivateMessage(e.Username, e.Text);
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
        var isOwner = IsOwner(username);
        if (Room.Find(username) is { } speaker && Room.Contains(speaker))
        {
            AfkOnChat(speaker);
        }

        // Bancho runs a referee's !mp commands itself, so they are not read as player commands.
        if ((isOwner || Room.IsReferee(username)) && text.StartsWith("!mp", StringComparison.OrdinalIgnoreCase))
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
            case "!r" or "!regulation":
                ShowRegulation();
                return;
            case "!info" or "!help":
                ShowInfo();
                return;
            case "!version" or "!v":
                ShowVersion();
                return;
            case "!update":
                Recast();
                return;
            case "!mirror":
                ShowMirror();
                return;
            case "!rank":
                ShowRank(player?.Name ?? username);
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
            case "*keep":
            case "*no" when command.Argument.TrimStart().StartsWith("keep", StringComparison.Ordinal):
                ChangeKeeping(command.Name, command.Argument);
                break;
            case "*regulation" or "*no" or "*mapchecker_enable" or "*mapchecker_disable":
                ChangeRegulation(command.Name, command.Argument);
                break;
            case "*afkkick_enable":
                UpdateSettings(_settings with { AfkKickEnabled = true });
                break;
            case "*afkkick_disable":
                UpdateSettings(_settings with { AfkKickEnabled = false });
                break;
            case "*afkkick_threshold" when int.TryParse(command.Argument, CultureInfo.InvariantCulture, out var threshold):
                UpdateSettings(_settings with { AfkKickThreshold = Math.Max(threshold, 1) });
                break;
            case "*afkkick_cooltime" when int.TryParse(command.Argument, CultureInfo.InvariantCulture, out var cooldownSeconds):
                UpdateSettings(_settings with { AfkKickCooldown = TimeSpan.FromSeconds(Math.Max(cooldownSeconds, 10)) });
                break;
            case "*autostart_enable" when !Room.IsMatching:
                UpdateSettings(_settings with { AutoStartEnabled = true });
                break;
            case "*autostart_disable" when !Room.IsMatching:
                UpdateSettings(_settings with { AutoStartEnabled = false });
                break;
            case "*autostart_time" when !Room.IsMatching && int.TryParse(command.Argument, CultureInfo.InvariantCulture, out var delaySeconds):
                UpdateSettings(_settings with { AutoStartDelay = TimeSpan.FromSeconds(Math.Max(delaySeconds, (int)MinimumAutoStartDelay.TotalSeconds)) });
                break;
            case "*autostart_clearhost_enable" when !Room.IsMatching:
                UpdateSettings(_settings with { AutoStartClearsHost = true });
                break;
            case "*autostart_clearhost_disable" when !Room.IsMatching:
                UpdateSettings(_settings with { AutoStartClearsHost = false });
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

    private bool IsOwner(string username) => Player.KeyOf(username) == Player.KeyOf(_settings.OwnerName);

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
