using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OsuRotate.Bancho.Tests;

// A small stand-in for Bancho that speaks real IRC over a loopback socket.
internal sealed class BanchoSimulator : IAsyncDisposable
{
    private const string Server = ":cho.ppy.sh";
    private const string Bot = ":BanchoBot!cho@ppy.sh";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<(DateTimeOffset At, string Line)> _received = new();
    private readonly List<(string Name, int Id)> _players = [];
    private readonly HashSet<long> _rooms = [];
    private readonly Lock _gate = new();
    private TcpClient? _client;
    private StreamWriter? _writer;
    private Task? _acceptLoop;
    private long _nextRoomId = 1000;
    private int _connections;

    public string Nick { get; init; } = "creator";
    public string Password { get; init; } = "secret";

    // While set, registration stops after 001, before the MOTD ends.
    public TaskCompletionSource? MotdGate { get; set; }
    public bool AnswerPings { get; set; } = true;
    public bool CreatedReplyBeforeJoin { get; set; }
    public bool RejectWithNumeric { get; set; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int Connections => Volatile.Read(ref _connections);
    public IReadOnlyList<(DateTimeOffset At, string Line)> Received => [.. _received];
    public long LastRoomId => Interlocked.Read(ref _nextRoomId) - 1;
    public string LastChannel => $"#mp_{LastRoomId}";

    public void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public void AddRoom(long id)
    {
        lock (_gate)
        {
            _rooms.Add(id);
        }
    }

    public void AddPlayer(string name, int id)
    {
        int slot;
        lock (_gate)
        {
            _players.Add((name, id));
            slot = _players.Count;
        }

        FromBot(LastChannel, $"{name} joined in slot {slot}.");
    }

    public void RemovePlayer(string name)
    {
        lock (_gate)
        {
            _players.RemoveAll(p => p.Name == name);
        }

        FromBot(LastChannel, $"{name} left the game.");
    }

    public void FromBot(string target, string text) => Send($"{Bot} PRIVMSG {target} :{text}");

    public void Chat(string nick, string target, string text) => Send($":{nick}!cho@ppy.sh PRIVMSG {target} :{text}");

    public void Send(string line)
    {
        lock (_gate)
        {
            try
            {
                _writer?.Write(line + "\r\n");
                _writer?.Flush();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // The client went away.
            }
        }
    }

    public void DropClient()
    {
        lock (_gate)
        {
            _client?.Close();
            _client = null;
            _writer = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        DropClient();
        if (_acceptLoop is not null)
        {
            await _acceptLoop;
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _connections);
                lock (_gate)
                {
                    _client = client;
                    _writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false));
                }

                _ = ServeAsync(client);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream(), new UTF8Encoding(false));
            string? password = null;
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                _received.Enqueue((DateTimeOffset.UtcNow, line));
                if (line.StartsWith("PASS ", StringComparison.Ordinal))
                {
                    password = line[5..];
                }
                else if (line.StartsWith("USER ", StringComparison.Ordinal))
                {
                    await RegisterAsync(password);
                }
                else if (line.StartsWith("PING", StringComparison.Ordinal))
                {
                    if (AnswerPings)
                    {
                        Send($"{Server} PONG cho.ppy.sh :osu-rotate");
                    }
                }
                else if (line.StartsWith("JOIN ", StringComparison.Ordinal))
                {
                    Join(line[5..]);
                }
                else if (line.StartsWith("PRIVMSG ", StringComparison.Ordinal))
                {
                    var separator = line.IndexOf(" :", StringComparison.Ordinal);
                    HandleMessage(line[8..separator], line[(separator + 2)..]);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // The client went away.
        }
    }

    private async Task RegisterAsync(string? password)
    {
        if (password != Password)
        {
            if (RejectWithNumeric)
            {
                Send($"{Server} 464 {Nick} :Bad authentication token.");
                return;
            }

            // What real Bancho sent in a live probe: MOTD lines with no 001, then a closed connection.
            Send($"{Server} 372 {Nick} :Welcome to osu!bancho.");
            Send($"{Server} 372 {Nick} :-");
            Send($"{Server} 372 {Nick} :- You are required to authenticate before accessing this service.");
            Send($"{Server} 372 {Nick} :- Please click the following link to receive your password:");
            Send($"{Server} 372 {Nick} :- https://osu.ppy.sh/home/account/edit#legacy-api");
            Send($"{Server} 372 {Nick} :-");
            DropClient();
            return;
        }

        Send($"{Server} 001 {Nick} :Welcome to the osu!Bancho.");
        if (MotdGate is { } gate)
        {
            await gate.Task;
        }

        Send($"{Server} 375 {Nick} :-");
        Send($"{Server} 372 {Nick} :- welcome");
        Send($"{Server} 376 {Nick} :-");
    }

    private void Join(string channel)
    {
        var known = false;
        if (long.TryParse(channel.AsSpan(4), out var id))
        {
            lock (_gate)
            {
                known = _rooms.Contains(id);
            }
        }

        Send(known ? $":{Nick}!cho@ppy.sh JOIN :{channel}" : $"{Server} 403 {Nick} {channel} :No such channel {channel}");
    }

    private void HandleMessage(string target, string text)
    {
        if (target == "BanchoBot" && text.StartsWith("!mp make ", StringComparison.Ordinal))
        {
            MakeRoom(text[9..]);
        }
        else if (target == "BanchoBot" && text.StartsWith("!stat ", StringComparison.Ordinal))
        {
            FromBot(Nick, $"Stats for ({text[6..]})[https://osu.ppy.sh/u/5] is Afk:");
            FromBot(Nick, "Score:    1,000 (#10)");
            FromBot(Nick, "Plays:    5 (lv2)");
            FromBot(Nick, "Accuracy: 90.5%");
        }
        else if (text == "!mp settings")
        {
            SendSettings(target);
        }
        else if (text == "!mp close")
        {
            FromBot(target, "Closed the match");
            Send($":{Nick}!cho@ppy.sh PART :{target}");
        }
        else if (text.StartsWith("!mp host ", StringComparison.Ordinal))
        {
            FromBot(target, $"{text[9..]} became the host.");
            FromBot(target, $"Changed match host to {text[9..]}");
        }
        else if (text == "!mp listrefs")
        {
            FromBot(target, "Match referees:");
            FromBot(target, Nick);
        }
        else if (text == "!mp abort")
        {
            FromBot(target, "Aborted the match");
        }
        else if (text.StartsWith("!mp invite ", StringComparison.Ordinal))
        {
            FromBot(target, $"Invited {text[11..]} to the room");
        }
    }

    private void MakeRoom(string title)
    {
        var id = Interlocked.Increment(ref _nextRoomId) - 1;
        AddRoom(id);

        var channel = $"#mp_{id}";
        var created = $"Created the tournament match https://osu.ppy.sh/mp/{id} {title}";
        if (CreatedReplyBeforeJoin)
        {
            FromBot(Nick, created);
            Send($":{Nick}!cho@ppy.sh JOIN :{channel}");
        }
        else
        {
            Send($":{Nick}!cho@ppy.sh JOIN :{channel}");
            FromBot(Nick, created);
        }
    }

    private void SendSettings(string channel)
    {
        List<(string Name, int Id)> players;
        lock (_gate)
        {
            players = [.. _players];
        }

        FromBot(channel, $"Room name: test room, History: https://osu.ppy.sh/mp/{channel[4..]}");
        FromBot(channel, "Beatmap: https://osu.ppy.sh/b/100 artist - title [diff]");
        FromBot(channel, "Team mode: HeadToHead, Win condition: Score");
        FromBot(channel, "Active mods: Freemod");
        FromBot(channel, $"Players: {players.Count}");
        for (var i = 0; i < players.Count; i++)
        {
            FromBot(channel, $"Slot {i + 1}  Not Ready https://osu.ppy.sh/u/{players[i].Id} {players[i].Name,-15} ");
        }
    }
}
