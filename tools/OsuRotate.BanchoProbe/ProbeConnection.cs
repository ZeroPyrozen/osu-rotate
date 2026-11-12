using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;

namespace OsuRotate.BanchoProbe;

// A deliberately low-level IRC session. The probe needs to do things the real transport refuses
// to, such as sending before the channel is rejoined.
internal sealed partial class ProbeConnection : IAsyncDisposable
{
    private readonly TcpClient _client = new();
    private readonly SemaphoreSlim _writeGate = new(1);
    private readonly Channel<IrcMessage> _inbox = Channel.CreateUnbounded<IrcMessage>();
    private readonly CancellationTokenSource _stop = new();
    private readonly ProbeOptions _options;
    private readonly ProbeReport _report;
    private readonly BanchoReader _channelReader = new(TimeProvider.System, TimeSpan.FromSeconds(1));
    private readonly BanchoReader _privateReader = new(TimeProvider.System, TimeSpan.FromSeconds(1), isPrivateMessages: true);
    private readonly List<DateTimeOffset> _serverTraffic = [];
    private readonly List<LobbyEvent> _parsed = [];
    private readonly Lock _gate = new();
    private NetworkStream? _stream;
    private Task? _readLoop;
    private DateTimeOffset _lastChatSent = DateTimeOffset.MinValue;
    private int _serverPings;

    private ProbeConnection(ProbeOptions options, ProbeReport report)
    {
        _options = options;
        _report = report;
        IrcNick = PlayerNames.ToIrcName(options.Nick);
    }

    public string IrcNick { get; }

    public bool IsClosed { get; private set; }

    public int ServerPings => Volatile.Read(ref _serverPings);

    public IReadOnlyList<DateTimeOffset> ServerTraffic
    {
        get
        {
            lock (_gate)
            {
                return [.. _serverTraffic];
            }
        }
    }

    public IReadOnlyList<LobbyEvent> Parsed
    {
        get
        {
            lock (_gate)
            {
                return [.. _parsed];
            }
        }
    }

    public static async Task<ProbeConnection> OpenAsync(ProbeOptions options, ProbeReport report, CancellationToken cancellationToken)
    {
        var connection = new ProbeConnection(options, report);
        await connection._client.ConnectAsync(options.Host, options.Port, cancellationToken);
        connection._stream = connection._client.GetStream();
        connection._readLoop = connection.ReadLoopAsync();

        report.Log(">>", "PASS ********");
        await connection.WriteAsync($"PASS {IrcMessage.Clean(options.Password)}", cancellationToken);
        await connection.SendRawAsync($"NICK {connection.IrcNick}", cancellationToken);
        await connection.SendRawAsync($"USER {connection.IrcNick} 0 * :osu-rotate probe", cancellationToken);
        return connection;
    }

    public async Task SendRawAsync(string line, CancellationToken cancellationToken)
    {
        _report.Log(">>", line);
        await WriteAsync(line, cancellationToken);
    }

    public async Task SendChatAsync(string target, string text, CancellationToken cancellationToken)
    {
        var wait = _lastChatSent + _options.SendSpacing - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }

        _lastChatSent = DateTimeOffset.UtcNow;
        await SendRawAsync(IrcMessage.Privmsg(target, text), cancellationToken);
    }

    // Reads forward, discarding what does not match. Returns null on timeout or a closed connection.
    public async Task<IrcMessage?> WaitForAsync(Func<IrcMessage, bool> match, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await foreach (var message in _inbox.Reader.ReadAllAsync(limit.Token))
            {
                if (match(message))
                {
                    return message;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out.
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _client.Dispose();
        if (_readLoop is not null)
        {
            await _readLoop;
        }

        _writeGate.Dispose();
        _stop.Dispose();
    }

    private async Task WriteAsync(string line, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _stream!.WriteAsync(bytes, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            using var reader = new StreamReader(_stream!, new UTF8Encoding(false));
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                _report.Log("<<", line);
                lock (_gate)
                {
                    _serverTraffic.Add(DateTimeOffset.UtcNow);
                }

                if (IrcMessage.Parse(line) is not { } message)
                {
                    continue;
                }

                if (message.Command == "PING")
                {
                    Interlocked.Increment(ref _serverPings);
                    await SendRawAsync($"PONG :{message.Parameters.FirstOrDefault()}", _stop.Token);
                    continue;
                }

                Observe(message);
                _inbox.Writer.TryWrite(message);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The session ended. IsClosed tells the probe.
        }
        finally
        {
            IsClosed = true;
            _inbox.Writer.TryComplete();
        }
    }

    // Runs every BanchoBot line through the real parser, which is the point of a live run.
    private void Observe(IrcMessage message)
    {
        if (message.Command != "PRIVMSG" || message.Nick != "BanchoBot" || message.Parameters.Count < 2)
        {
            return;
        }

        var text = message.Parameters[1];
        var isPrivate = string.Equals(message.Parameters[0], IrcNick, StringComparison.OrdinalIgnoreCase);
        var parsed = (isPrivate ? _privateReader : _channelReader).Read(text);
        if (parsed is not null)
        {
            lock (_gate)
            {
                _parsed.Add(parsed);
            }
        }
        else if (!PartOfMultiLineReply().IsMatch(text))
        {
            _report.AddUnrecognized(text);
        }
    }

    [GeneratedRegex(@"^(Room name:|Beatmap:|Team mode:|Active mods:|Players:|Slot \d+|Stats for |Score:|Plays:|Accuracy:)")]
    private static partial Regex PartOfMultiLineReply();
}
