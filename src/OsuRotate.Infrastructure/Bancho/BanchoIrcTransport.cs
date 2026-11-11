using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Infrastructure.Bancho;

public sealed class BanchoIrcTransport : ILobbyTransport, IDisposable
{
    private const string BanchoBot = "BanchoBot";
    private const string ChannelPrefix = "#mp_";
    private const string ActionPrefix = "\u0001ACTION ";

    // No such channel, channel full, invite only, banned, wrong key.
    private static readonly HashSet<string> JoinErrors = ["403", "471", "473", "474", "475"];

    private readonly BanchoOptions _options;
    private readonly ILogger<BanchoIrcTransport> _logger;
    private readonly TimeProvider _time;
    private readonly OutboundScheduler _scheduler;
    private readonly Channel<LobbyEvent> _events = Channel.CreateUnbounded<LobbyEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly string _ircNick;
    private int _started;

    private volatile string? _channel;
    private volatile bool _awaitingCreatedRoom;
    private volatile bool _connected;

    public BanchoIrcTransport(BanchoOptions options, ILogger<BanchoIrcTransport> logger, TimeProvider? time = null)
    {
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _ircNick = PlayerNames.ToIrcName(options.Nick);
        _scheduler = new OutboundScheduler(
            new OutboundQueue(options.MessagesPerWindow, options.RateWindow, options.InfoMessagesStaleAfter, options.MaxQueuedInfoMessages),
            _time);
    }

    public TransportCapabilities Capabilities { get; } = new(CanSetHost: true, HasChat: true);

    public async IAsyncEnumerable<LobbyEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("Events can only be enumerated once.");
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connection = Task.Run(() => RunAsync(stop.Token), CancellationToken.None);
        try
        {
            await foreach (var lobbyEvent in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return lobbyEvent;
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await connection.ConfigureAwait(false);
        }
    }

    public ValueTask SendAsync(LobbyCommand command, CancellationToken cancellationToken)
    {
        if (!_connected)
        {
            _logger.LogWarning("Dropped {Command} because Bancho is not connected.", command.GetType().Name);
            return ValueTask.CompletedTask;
        }

        switch (command)
        {
            case CreateRoom:
                _channel = null;
                _awaitingCreatedRoom = true;
                break;
            case JoinRoom join:
                _channel = BanchoCommandFormatter.Channel(join.RoomId);
                _awaitingCreatedRoom = false;
                break;
        }

        var messages = BanchoCommandFormatter.Format(command, _channel);
        if (messages.Count == 0)
        {
            _logger.LogWarning("Dropped {Command} because no room is joined.", command.GetType().Name);
        }

        foreach (var message in messages)
        {
            _scheduler.Enqueue(message);
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose() => _scheduler.Dispose();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string reason;
                try
                {
                    await RunSessionAsync(() => attempt = 0, cancellationToken).ConfigureAwait(false);
                    reason = "Connection closed.";
                }
                catch (BanchoAuthenticationException)
                {
                    // Retrying a wrong password only risks the account being locked out.
                    _logger.LogError("Bancho rejected the IRC credentials. Not reconnecting.");
                    Emit(new TransportAuthenticationFailed());
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is IOException or SocketException or TimeoutException or OperationCanceledException)
                {
                    reason = exception.Message;
                }

                ResetSession(reason);

                attempt++;
                var delay = ReconnectBackoff.Delay(attempt, _options.ReconnectBaseDelay, _options.ReconnectMaxDelay, Random.Shared.NextDouble());
                _logger.LogInformation("Reconnecting to Bancho in {Delay} (attempt {Attempt}).", delay, attempt);
                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping.
        }
        finally
        {
            ResetSession("Transport stopped.");
            _events.Writer.TryComplete();
        }
    }

    private void ResetSession(string reason)
    {
        var wasConnected = _connected;
        _connected = false;
        _channel = null;
        _awaitingCreatedRoom = false;
        _scheduler.Clear();

        if (wasConnected)
        {
            _logger.LogWarning("Disconnected from Bancho: {Reason}", reason);
            Emit(new TransportDisconnected(reason));
        }
    }

    private async Task RunSessionAsync(Action onRegistered, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(_options.ConnectTimeout);
            await client.ConnectAsync(_options.Host, _options.Port, connect.Token).ConfigureAwait(false);
        }

        var stream = client.GetStream();
        await using var _ = stream.ConfigureAwait(false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        using var writeGate = new SemaphoreSlim(1);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task WriteAsync(string line, CancellationToken token)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
            await writeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            }
            finally
            {
                writeGate.Release();
            }
        }

        await WriteAsync($"PASS {IrcMessage.Clean(_options.Password)}", session.Token).ConfigureAwait(false);
        await WriteAsync($"NICK {_ircNick}", session.Token).ConfigureAwait(false);
        await WriteAsync($"USER {_ircNick} 0 * :osu-rotate", session.Token).ConfigureAwait(false);

        var channelReader = new BanchoReader(_time, _options.RefereeListWindow);
        var privateReader = new BanchoReader(_time, _options.RefereeListWindow, isPrivateMessages: true);
        Task? pump = null;
        Task<string?>? pendingRead = null;
        var lastReceived = _time.GetUtcNow();

        try
        {
            while (true)
            {
                pendingRead ??= reader.ReadLineAsync(session.Token).AsTask();

                string? line;
                try
                {
                    line = await pendingRead.WaitAsync(_options.IdlePingAfter, _time, session.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Keeps the same pending read, because abandoning one could lose part of a line.
                    if (_time.GetUtcNow() - lastReceived >= _options.DeadAfter)
                    {
                        throw new TimeoutException("Bancho went silent.");
                    }

                    await WriteAsync("PING :osu-rotate", session.Token).ConfigureAwait(false);
                    continue;
                }

                pendingRead = null;
                if (line is null)
                {
                    throw new IOException("Bancho closed the connection.");
                }

                lastReceived = _time.GetUtcNow();
                if (IrcMessage.Parse(line) is not { } message)
                {
                    continue;
                }

                if (BanchoLogin.IsRejection(message))
                {
                    throw new BanchoAuthenticationException();
                }

                switch (message.Command)
                {
                    case "PING":
                        // Bypasses the scheduler: it is not chat, and a late reply gets the client dropped.
                        await WriteAsync($"PONG :{message.Parameters.FirstOrDefault()}", session.Token).ConfigureAwait(false);
                        break;

                    // Channels can only be joined after the MOTD ends, so that is what counts as connected.
                    case "376" or "422" when pump is null:
                        onRegistered();
                        _connected = true;
                        pump = _scheduler.PumpAsync(WriteAsync, session.Token);
                        _logger.LogInformation("Connected to Bancho.");
                        Emit(new TransportConnected());
                        break;

                    default:
                        HandleSafely(message, channelReader, privateReader);
                        break;
                }

                if (pump is { IsFaulted: true })
                {
                    await pump.ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await session.CancelAsync().ConfigureAwait(false);
            if (pump is not null)
            {
                try
                {
                    await pump.ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
                {
                    // The session is over either way.
                }
            }
        }
    }

    // One bad line must not end the session.
    private void HandleSafely(IrcMessage message, BanchoReader channelReader, BanchoReader privateReader)
    {
        try
        {
            Handle(message, channelReader, privateReader);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException or IndexOutOfRangeException)
        {
            _logger.LogError(exception, "Could not handle a {Command} message from Bancho.", message.Command);
        }
    }

    private void Handle(IrcMessage message, BanchoReader channelReader, BanchoReader privateReader)
    {
        switch (message.Command)
        {
            case "JOIN" when IsSelf(message.Nick) && message.Parameters.Count > 0:
                HandleSelfJoin(message.Parameters[0]);
                break;

            case "PART" when IsSelf(message.Nick) && message.Parameters.Count > 0 && message.Parameters[0] == _channel:
                var closed = RoomId(_channel!);
                _channel = null;
                Emit(new RoomClosed(closed));
                break;

            case "PRIVMSG" when message.Parameters.Count >= 2:
                HandlePrivmsg(message.Nick ?? string.Empty, message.Parameters[0], message.Parameters[1], channelReader, privateReader);
                break;

            default:
                if (JoinErrors.Contains(message.Command) && message.Parameters.Count >= 2 && message.Parameters[1] == _channel)
                {
                    var failed = RoomId(_channel!);
                    _channel = null;
                    Emit(new RoomJoinFailed(failed, message.Parameters[^1]));
                }

                break;
        }
    }

    private void HandleSelfJoin(string channel)
    {
        if (!channel.StartsWith(ChannelPrefix, StringComparison.Ordinal))
        {
            return;
        }

        if (_awaitingCreatedRoom)
        {
            _awaitingCreatedRoom = false;
            _channel = channel;
        }

        if (channel == _channel)
        {
            Emit(new RoomJoined(RoomId(channel)));
        }
    }

    private void HandlePrivmsg(string from, string target, string text, BanchoReader channelReader, BanchoReader privateReader)
    {
        var isPrivate = string.Equals(target, _ircNick, StringComparison.OrdinalIgnoreCase);
        if (!isPrivate && target != _channel)
        {
            return;
        }

        if (from == BanchoBot)
        {
            var parsed = (isPrivate ? privateReader : channelReader).Read(text);
            if (parsed is RoomCreated created)
            {
                HandleRoomCreated(created);
            }
            else if (parsed is not null)
            {
                Emit(parsed);
            }
            else
            {
                _logger.LogDebug("BanchoBot line not treated as an event: {Text}", text);
            }

            return;
        }

        if (isPrivate)
        {
            Emit(new PrivateMessageReceived(from, text));
        }
        else if (text.StartsWith(ActionPrefix, StringComparison.Ordinal))
        {
            Emit(new ChatReceived(from, text[ActionPrefix.Length..].TrimEnd('\u0001'), IsAction: true));
        }
        else
        {
            Emit(new ChatReceived(from, text, IsAction: false));
        }
    }

    // Bancho's reply and JOIN can arrive in either order, and the room ID comes from whichever is first.
    private void HandleRoomCreated(RoomCreated created)
    {
        var channel = BanchoCommandFormatter.Channel(created.RoomId);
        if (_awaitingCreatedRoom)
        {
            _awaitingCreatedRoom = false;
            _channel = channel;
        }

        if (channel == _channel)
        {
            Emit(created);
        }
    }

    private bool IsSelf(string? nick) => string.Equals(nick, _ircNick, StringComparison.OrdinalIgnoreCase);

    private static long RoomId(string channel) => long.Parse(channel.AsSpan(ChannelPrefix.Length), CultureInfo.InvariantCulture);

    private void Emit(LobbyEvent lobbyEvent) => _events.Writer.TryWrite(lobbyEvent);

    private sealed class BanchoAuthenticationException : Exception;
}
