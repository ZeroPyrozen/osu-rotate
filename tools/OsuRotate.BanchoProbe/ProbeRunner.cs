using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;

namespace OsuRotate.BanchoProbe;

internal enum ProbeStep
{
    JoinRoom,
    TypeInChat,
    StartMatch,
    LeaveRoom,
}

internal interface IProbeUi
{
    void Info(string text);

    // Tells the person what to do in osu!. The probe then waits for the effect on Bancho.
    void Instruct(ProbeStep step, string text);
}

internal sealed class ProbeRunner(ProbeOptions options, IProbeUi ui, HttpClient http)
{
    private const string BanchoBot = "BanchoBot";

    private readonly ProbeReport _report = new();
    private ProbeConnection? _connection;
    private long? _roomId;
    private string? _apiToken;
    private bool _playerInRoom;

    private ProbeConnection Connection => _connection!;
    private string Channel => BanchoCommandFormatter.Channel(_roomId!.Value);

    public async Task<ProbeReport> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (options.CheckRoom is { } roomId)
            {
                await CheckRoomAsync(roomId, cancellationToken);
            }
            else
            {
                await RunAllAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _report.Add("Run", ProbeOutcome.Info, "Stopped before finishing.");
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            _report.Add("Run", ProbeOutcome.Fail, $"Connection failed: {exception.Message}");
        }
        finally
        {
            await CleanUpAsync();
        }

        return _report;
    }

    private async Task RunAllAsync(CancellationToken ct)
    {
        if (!await ConnectAsync("Registration", ct) || !await CreateRoomAsync(ct))
        {
            return;
        }

        await CheckEmptyRoomStateAsync(ct);
        await CheckRefereeListAsync(ct);
        await JoinAndCheckCoexistenceAsync(ct);

        if (_playerInRoom)
        {
            await CheckChatEchoAsync(ct);
            await CheckMatchFlowAsync(ct);
        }
        else
        {
            _report.Add("In-game chat reaches IRC", ProbeOutcome.Skipped, "You did not join the room.");
            _report.Add("Match flow", ProbeOutcome.Skipped, "You did not join the room.");
        }

        await CheckMatchHistoryApiAsync(ct);
        await CheckReconnectAsync(ct);
        await CheckIdleAsync(ct);
    }

    private async Task<bool> ConnectAsync(string name, CancellationToken ct)
    {
        ui.Info("Connecting to Bancho...");
        _connection = await ProbeConnection.OpenAsync(options, _report, ct);

        var started = DateTimeOffset.UtcNow;
        var welcomeAt = (DateTimeOffset?)null;
        var result = await Connection.WaitForAsync(
            m =>
            {
                if (m.Command == "001")
                {
                    welcomeAt = DateTimeOffset.UtcNow;
                }

                return m.Command is "376" or "422" || BanchoLogin.IsRejection(m);
            },
            options.ServerReplyWait,
            ct);

        if (result is not null && BanchoLogin.IsRejection(result))
        {
            _report.Add(name, ProbeOutcome.Fail, "Bancho rejected the username or IRC password. Use the server password shown at osu.ppy.sh/home/account/edit#legacy-api, which is not your account password.");
            return false;
        }

        if (result is null)
        {
            _report.Add(name, ProbeOutcome.Fail, Connection.IsClosed
                ? "Bancho closed the connection before login finished."
                : "Bancho did not finish login in time.");
            return false;
        }

        var gap = welcomeAt is { } at ? $"{(DateTimeOffset.UtcNow - at).TotalMilliseconds:F0} ms between 001 and end of MOTD" : "no 001 seen";
        _report.Add(name, ProbeOutcome.Pass, $"Registered in {(DateTimeOffset.UtcNow - started).TotalSeconds:F1} s, {gap}.");
        return true;
    }

    private async Task<bool> CreateRoomAsync(CancellationToken ct)
    {
        ui.Info("Creating a test room...");
        await Connection.SendChatAsync(BanchoBot, "!mp make osu-rotate probe", ct);

        var order = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var reply = await Connection.WaitForAsync(
                m => IsSelfJoin(m) || (IsPrivateFromBot(m) && m.Parameters[1].StartsWith("Created the tournament match", StringComparison.Ordinal)),
                options.ServerReplyWait,
                ct);

            if (reply is null)
            {
                break;
            }

            if (reply.Command == "JOIN")
            {
                order.Add("JOIN");
                _roomId ??= long.Parse(reply.Parameters[0].AsSpan(4), CultureInfo.InvariantCulture);
            }
            else
            {
                order.Add("Created reply");
                _roomId ??= Connection.Parsed.OfType<RoomCreated>().LastOrDefault()?.RoomId;
            }
        }

        if (_roomId is null)
        {
            _report.Add("Create room", ProbeOutcome.Fail, "No room was created. You may already have 4 rooms open.");
            return false;
        }

        ui.Info($"Created room {_roomId}.");
        _report.Add("Create room", order.Count == 2 ? ProbeOutcome.Pass : ProbeOutcome.Fail, $"Room {_roomId}. Reply order: {string.Join(", then ", order)}.");
        return true;
    }

    private async Task CheckEmptyRoomStateAsync(CancellationToken ct)
    {
        var state = await RequestRoomStateAsync(ct);
        _report.Add("Empty room settings", state is { Players.Count: 0 } ? ProbeOutcome.Pass : ProbeOutcome.Fail,
            state is null ? "The !mp settings reply did not complete." : $"Parsed with {state.Players.Count} player(s).");
    }

    private async Task CheckRefereeListAsync(CancellationToken ct)
    {
        await Connection.SendChatAsync(Channel, "!mp listrefs", ct);
        var seen = await Connection.WaitForAsync(_ => Connection.Parsed.OfType<RefereeAdded>().Any(), options.ServerReplyWait, ct);
        var referees = Connection.Parsed.OfType<RefereeAdded>().Select(r => r.Username).ToList();
        _report.Add("Referee list", seen is null ? ProbeOutcome.Fail : ProbeOutcome.Pass,
            seen is null ? "No referee names were parsed." : $"Parsed: {string.Join(", ", referees)}.");
    }

    private async Task JoinAndCheckCoexistenceAsync(CancellationToken ct)
    {
        await Connection.SendChatAsync(Channel, $"!mp invite {Connection.IrcNick}", ct);
        ui.Instruct(ProbeStep.JoinRoom, "Open osu!stable, log in, and accept the invite to the room \"osu-rotate probe\" (it arrives as a private message from yourself or BanchoBot).");

        var joined = await Connection.WaitForAsync(
            _ => Connection.Parsed.OfType<PlayerJoined>().Any(p => IsSelfName(p.Username)),
            options.JoinWait,
            ct);

        if (joined is null)
        {
            _report.Add("Game and IRC on one account", ProbeOutcome.Skipped, Connection.IsClosed
                ? "The IRC session closed while waiting for you to join."
                : "You did not join the room in time.");
            return;
        }

        _playerInRoom = true;
        ui.Info("Seen you join. Checking that the IRC session survives...");
        await Connection.WaitForAsync(_ => false, options.CoexistWait, ct);

        _report.Add("Game and IRC on one account", Connection.IsClosed ? ProbeOutcome.Fail : ProbeOutcome.Pass,
            Connection.IsClosed ? "Joining from the game ended the IRC session." : "Both stayed connected.");

        var state = await RequestRoomStateAsync(ct);
        var player = state?.Players.FirstOrDefault(p => IsSelfName(p.Username));
        _report.Add("Room settings with a player", player is null ? ProbeOutcome.Fail : ProbeOutcome.Pass,
            player is null ? "Your slot line was not parsed." : $"Parsed slot {player.Slot}, user {player.OsuUserId}, ready state \"{player.ReadyState}\".");
    }

    private async Task CheckChatEchoAsync(CancellationToken ct)
    {
        var token = $"probe-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3))}";
        ui.Instruct(ProbeStep.TypeInChat, $"In the match chat in osu!, type exactly: {token}");

        var echoed = await Connection.WaitForAsync(
            m => m.Command == "PRIVMSG" && m.Parameters.Count >= 2 && m.Parameters[0] == Channel && m.Parameters[1].Contains(token, StringComparison.Ordinal),
            options.ChatWait,
            ct);

        _report.Add("In-game chat reaches IRC", ProbeOutcome.Info, echoed is null
            ? "NO. Your in-game messages do not reach this IRC session, so commands you type in-game will not reach the bot."
            : $"YES. Arrived from \"{echoed.Nick}\", so commands you type in-game will reach the bot.");
    }

    private async Task CheckMatchFlowAsync(CancellationToken ct)
    {
        if (options.SkipMatch)
        {
            _report.Add("Match flow", ProbeOutcome.Skipped, "Skipped with --skip-match.");
            return;
        }

        await Connection.SendChatAsync(Channel, $"!mp host {Connection.IrcNick}", ct);
        ui.Instruct(ProbeStep.StartMatch, "You are now host. Pick any map you have downloaded and press Start. The probe aborts the match a few seconds later.");

        var started = await Connection.WaitForAsync(_ => Connection.Parsed.OfType<MatchStarted>().Any(), options.MatchWait, ct);
        if (started is null)
        {
            _report.Add("Match flow", ProbeOutcome.Skipped, "No match started in time.");
            return;
        }

        var running = await ReadCurrentGameAsync(ct);
        await Connection.WaitForAsync(_ => false, options.AbortAfter, ct);
        await Connection.SendChatAsync(Channel, "!mp abort", ct);
        var aborted = await Connection.WaitForAsync(_ => Connection.Parsed.OfType<MatchAborted>().Any(), options.ServerReplyWait, ct);
        var afterAbort = await ReadCurrentGameAsync(ct);

        _report.Add("Match flow", aborted is null ? ProbeOutcome.Fail : ProbeOutcome.Pass, aborted is null ? "The abort was not confirmed." : "Start and abort messages parsed.");
        _report.Add("API shows a running match", ProbeOutcome.Info, $"current_game_id during the match: {running}. After abort: {afterAbort}.");
    }

    private async Task CheckMatchHistoryApiAsync(CancellationToken ct)
    {
        if (!await EnsureApiTokenAsync(ct))
        {
            _report.Add("Match history API", ProbeOutcome.Skipped, "No osu! OAuth client was provided.");
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://osu.ppy.sh/api/v2/matches/{_roomId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _report.Add("Match history API", ProbeOutcome.Fail, $"HTTP {(int)response.StatusCode}.");
            return;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var fields = new[] { "match", "events", "users", "latest_event_id", "current_game_id" };
        var missing = fields.Where(f => !document.RootElement.TryGetProperty(f, out _)).ToList();
        _report.Add("Match history API", missing.Count == 0 ? ProbeOutcome.Pass : ProbeOutcome.Fail,
            missing.Count == 0 ? "All expected fields are present." : $"Missing: {string.Join(", ", missing)}.");
    }

    private async Task CheckReconnectAsync(CancellationToken ct)
    {
        ui.Info("Disconnecting IRC to test recovery. Stay in the room...");
        await Connection.DisposeAsync();
        _connection = await ProbeConnection.OpenAsync(options, _report, ct);

        var welcome = await Connection.WaitForAsync(m => m.Command == "001" || BanchoLogin.IsRejection(m), options.ServerReplyWait, ct);
        if (welcome?.Command != "001")
        {
            _report.Add("Reconnect", ProbeOutcome.Fail, "Could not log in again.");
            return;
        }

        // Sent on purpose before the channel is rejoined: this is what osu-ahr does after a reconnect.
        await Connection.SendChatAsync(Channel, "!mp settings", ct);

        var answeredEarly = false;
        var autoJoined = false;
        string? refusal = null;
        bool Inspect(IrcMessage m)
        {
            answeredEarly |= m.Nick == BanchoBot && m.Parameters.Count >= 2 && m.Parameters[1].StartsWith("Room name:", StringComparison.Ordinal);
            autoJoined |= IsSelfJoin(m);
            if (m.Command is "401" or "403" or "404" or "442")
            {
                refusal = $"{m.Command}: {m.Parameters[^1]}";
            }

            return false;
        }

        await Connection.WaitForAsync(m => Inspect(m) || m.Command is "376" or "422", options.ServerReplyWait, ct);
        await Connection.WaitForAsync(Inspect, options.AfterMotdWait, ct);

        _report.Add("Command before rejoining", ProbeOutcome.Info,
            $"Answered: {(answeredEarly ? "yes" : "no")}. Refusal: {refusal ?? "none"}. Bancho rejoined the channel by itself: {(autoJoined ? "yes" : "no")}.");

        await Connection.SendRawAsync($"JOIN {Channel}", ct);
        var joined = autoJoined ? null : await Connection.WaitForAsync(m => IsSelfJoin(m) || m.Command is "403" or "405" or "471" or "473" or "474" or "475", options.ServerReplyWait, ct);
        if (!autoJoined && (joined is null || joined.Command != "JOIN"))
        {
            _report.Add("Room survives a referee disconnect", ProbeOutcome.Fail, joined is null ? "No reply to JOIN." : $"Refused: {joined.Parameters[^1]}");
            _roomId = null;
            return;
        }

        var state = await RequestRoomStateAsync(ct);
        _report.Add("Room survives a referee disconnect", state is null ? ProbeOutcome.Fail : ProbeOutcome.Pass,
            state is null ? "Rejoined, but settings did not complete." : $"Rejoined with {state.Players.Count} player(s) still in the room.");
    }

    private async Task CheckIdleAsync(CancellationToken ct)
    {
        if (_playerInRoom)
        {
            ui.Instruct(ProbeStep.LeaveRoom, "Leave the room in osu! now. The next check needs an empty, quiet lobby.");
            await Connection.WaitForAsync(_ => Connection.Parsed.OfType<PlayerLeft>().Any(p => IsSelfName(p.Username)), options.JoinWait, ct);
        }

        ui.Info($"Listening to an idle lobby for {options.IdleDuration.TotalMinutes:F0} minute(s) without sending anything...");
        var started = DateTimeOffset.UtcNow;
        var pingsBefore = Connection.ServerPings;
        await Connection.WaitForAsync(_ => false, options.IdleDuration, ct);
        var ended = DateTimeOffset.UtcNow;

        if (Connection.IsClosed)
        {
            _report.Add("Idle connection", ProbeOutcome.Fail, "Bancho closed an idle connection.");
            return;
        }

        var traffic = Connection.ServerTraffic.Where(t => t >= started).Prepend(started).Append(ended).ToList();
        var longestSilence = traffic.Zip(traffic.Skip(1), (a, b) => b - a).Max();
        var pings = Connection.ServerPings - pingsBefore;

        await Connection.SendRawAsync("PING :osu-rotate-probe", ct);
        var pong = await Connection.WaitForAsync(m => m.Command == "PONG", options.ServerReplyWait, ct);

        var healthy = longestSilence < options.DeadAfter || pong is not null;
        _report.Add("Idle connection", healthy ? ProbeOutcome.Pass : ProbeOutcome.Fail,
            $"Server PINGs: {pings}. Longest silence: {longestSilence.TotalSeconds:F0} s. Answers a client PING: {(pong is null ? "no" : "yes")}.");
    }

    private async Task CheckRoomAsync(long roomId, CancellationToken ct)
    {
        if (!await ConnectAsync("Registration", ct))
        {
            return;
        }

        _roomId = roomId;
        await Connection.SendRawAsync($"JOIN {Channel}", ct);
        var joined = await Connection.WaitForAsync(m => IsSelfJoin(m) || m.Command is "403" or "405" or "473" or "474" or "475", options.ServerReplyWait, ct);
        if (joined?.Command != "JOIN")
        {
            _report.Add("Kept room still open", ProbeOutcome.Fail, joined is null ? "No reply to JOIN." : $"Gone: {joined.Parameters[^1]}");
            _roomId = null;
            return;
        }

        var state = await RequestRoomStateAsync(ct);
        _report.Add("Kept room still open", state is null ? ProbeOutcome.Fail : ProbeOutcome.Pass, state is null ? "Joined, but settings did not complete." : $"Room \"{state.Name}\" is still open.");
    }

    private async Task<RoomState?> RequestRoomStateAsync(CancellationToken ct)
    {
        var before = Connection.Parsed.OfType<RoomStateReceived>().Count();
        await Connection.SendChatAsync(Channel, "!mp settings", ct);
        var done = await Connection.WaitForAsync(_ => Connection.Parsed.OfType<RoomStateReceived>().Count() > before, options.ServerReplyWait, ct);
        return done is null ? null : Connection.Parsed.OfType<RoomStateReceived>().Last().State;
    }

    private async Task<string> ReadCurrentGameAsync(CancellationToken ct)
    {
        if (!await EnsureApiTokenAsync(ct))
        {
            return "not checked (no OAuth client)";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://osu.ppy.sh/api/v2/matches/{_roomId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return $"HTTP {(int)response.StatusCode}";
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.TryGetProperty("current_game_id", out var id) ? id.ToString() is { Length: > 0 } text ? text : "null" : "missing";
    }

    private async Task<bool> EnsureApiTokenAsync(CancellationToken ct)
    {
        if (_apiToken is not null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            return false;
        }

        using var response = await http.PostAsJsonAsync(
            "https://osu.ppy.sh/oauth/token",
            new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["grant_type"] = "client_credentials",
                ["scope"] = "public",
            },
            ct);

        if (!response.IsSuccessStatusCode)
        {
            _report.Add("osu! OAuth client", ProbeOutcome.Fail, $"Token request returned HTTP {(int)response.StatusCode}.");
            return false;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        _apiToken = document.RootElement.GetProperty("access_token").GetString();
        return _apiToken is not null;
    }

    // Runs even after a failure or Ctrl+C, because an account can only hold 4 open rooms.
    private async Task CleanUpAsync()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            if (_roomId is { } roomId)
            {
                if (options.KeepRoom)
                {
                    _report.Add("Room left open", ProbeOutcome.Info, $"Room {roomId}. Check it later with --check-room {roomId}.");
                    ui.Info($"Leaving room {roomId} open. Check it later with: --check-room {roomId}");
                    return;
                }

                await CloseRoomAsync(roomId, limit.Token);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            _report.Add("Close room", ProbeOutcome.Fail, $"Could not close room {_roomId}: {exception.Message}");
            ui.Info($"Could not close room {_roomId}. In osu! chat, join #mp_{_roomId} and type: !mp close");
        }
        finally
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
    }

    private async Task CloseRoomAsync(long roomId, CancellationToken ct)
    {
        if (_connection is null || Connection.IsClosed)
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            _connection = await ProbeConnection.OpenAsync(options, _report, ct);
            await Connection.WaitForAsync(m => m.Command is "376" or "422", options.ServerReplyWait, ct);
            await Connection.SendRawAsync($"JOIN {BanchoCommandFormatter.Channel(roomId)}", ct);
            await Connection.WaitForAsync(IsSelfJoin, options.ServerReplyWait, ct);
        }

        await Connection.SendChatAsync(BanchoCommandFormatter.Channel(roomId), "!mp close", ct);
        var closed = await Connection.WaitForAsync(_ => Connection.Parsed.OfType<RoomCloseAcknowledged>().Any(), options.ServerReplyWait, ct);
        _report.Add("Close room", closed is null ? ProbeOutcome.Fail : ProbeOutcome.Pass, closed is null ? $"Room {roomId} did not confirm closing." : $"Room {roomId} closed.");
        ui.Info(closed is null ? $"Room {roomId} did not confirm closing. In osu! chat, join #mp_{roomId} and type: !mp close" : "Closed the test room.");
    }

    private bool IsSelfJoin(IrcMessage message) =>
        message.Command == "JOIN" && string.Equals(message.Nick, Connection.IrcNick, StringComparison.OrdinalIgnoreCase)
        && message.Parameters.Count > 0 && message.Parameters[0].StartsWith("#mp_", StringComparison.Ordinal);

    private bool IsPrivateFromBot(IrcMessage message) =>
        message.Command == "PRIVMSG" && message.Nick == BanchoBot && message.Parameters.Count >= 2
        && string.Equals(message.Parameters[0], Connection.IrcNick, StringComparison.OrdinalIgnoreCase);

    private bool IsSelfName(string username) =>
        string.Equals(PlayerNames.ToIrcName(username), Connection.IrcNick, StringComparison.OrdinalIgnoreCase);
}
