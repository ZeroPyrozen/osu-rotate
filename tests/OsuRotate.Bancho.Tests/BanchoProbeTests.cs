using System.Net;
using System.Text;
using OsuRotate.BanchoProbe;

namespace OsuRotate.Bancho.Tests;

public sealed class BanchoProbeTests : IAsyncLifetime
{
    private const string Password = "secret-irc-password";
    private const string ClientSecret = "secret-oauth-value";
    private const string ApiToken = "secret-api-token";

    private readonly BanchoSimulator _server = new() { Password = Password };

    public ValueTask InitializeAsync()
    {
        _server.Start();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    private sealed class ScriptedUi(Action<ProbeStep, string> onInstruct) : IProbeUi
    {
        public void Info(string text)
        {
        }

        public void Instruct(ProbeStep step, string text) => onInstruct(step, text);
    }

    private sealed class FakeOsu : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath == "/oauth/token"
                ? $$"""{"access_token":"{{ApiToken}}"}"""
                : """{"match":{},"events":[],"users":[],"latest_event_id":1,"current_game_id":77}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private ProbeOptions Options(string password = Password, bool keepRoom = false, long? checkRoom = null, TimeSpan? joinWait = null, bool api = true) => new()
    {
        Host = "127.0.0.1",
        Port = _server.Port,
        Nick = "creator",
        Password = password,
        ClientId = api ? "1" : null,
        ClientSecret = api ? ClientSecret : null,
        KeepRoom = keepRoom,
        CheckRoom = checkRoom,
        ServerReplyWait = TimeSpan.FromSeconds(5),
        JoinWait = joinWait ?? TimeSpan.FromSeconds(5),
        ChatWait = TimeSpan.FromMilliseconds(400),
        MatchWait = TimeSpan.FromSeconds(5),
        CoexistWait = TimeSpan.FromMilliseconds(50),
        AbortAfter = TimeSpan.FromMilliseconds(20),
        AfterMotdWait = TimeSpan.FromMilliseconds(50),
        IdleDuration = TimeSpan.FromMilliseconds(200),
        DeadAfter = TimeSpan.FromSeconds(30),
        SendSpacing = TimeSpan.Zero,
    };

    // Plays the part of a person following the probe's instructions.
    private void Player(ProbeStep step, string text, bool echoChat)
    {
        switch (step)
        {
            case ProbeStep.JoinRoom:
                _server.AddPlayer("creator", 42);
                break;
            case ProbeStep.TypeInChat when echoChat:
                _server.Chat("creator", _server.LastChannel, text[(text.LastIndexOf(' ') + 1)..]);
                break;
            case ProbeStep.StartMatch:
                _server.FromBot(_server.LastChannel, "The match has started!");
                break;
            case ProbeStep.LeaveRoom:
                _server.RemovePlayer("creator");
                break;
        }
    }

    private async Task<ProbeReport> Run(ProbeOptions options, Action<ProbeStep, string> ui, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient(new FakeOsu());
        return await new ProbeRunner(options, new ScriptedUi(ui), http).RunAsync(cancellationToken);
    }

    private static ProbeResult Result(ProbeReport report, string name) => Assert.Single(report.Results, r => r.Name == name);

    private bool RoomWasClosed => _server.Received.Any(r => r.Line.EndsWith(":!mp close", StringComparison.Ordinal));

    [Fact]
    public async Task A_full_run_passes_and_closes_the_room()
    {
        var report = await Run(Options(), (step, text) => Player(step, text, echoChat: true));

        foreach (var name in new[] { "Registration", "Create room", "Empty room settings", "Referee list", "Game and IRC on one account", "Room settings with a player", "Match flow", "Match history API", "Room survives a referee disconnect", "Idle connection", "Close room" })
        {
            Assert.Equal(ProbeOutcome.Pass, Result(report, name).Outcome);
        }

        Assert.StartsWith("YES", Result(report, "In-game chat reaches IRC").Detail, StringComparison.Ordinal);
        Assert.Contains("77", Result(report, "API shows a running match").Detail, StringComparison.Ordinal);
        Assert.Empty(report.Unrecognized);
        Assert.True(RoomWasClosed);
    }

    [Fact]
    public async Task Reports_when_in_game_chat_does_not_reach_IRC()
    {
        var report = await Run(Options(), (step, text) => Player(step, text, echoChat: false));

        Assert.StartsWith("NO", Result(report, "In-game chat reaches IRC").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secrets_never_appear_in_the_report()
    {
        var report = await Run(Options(), (step, text) => Player(step, text, echoChat: true));

        var markdown = report.ToMarkdown();

        Assert.DoesNotContain(Password, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiToken, markdown, StringComparison.Ordinal);
        Assert.Contains("PASS ********", markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_wrong_password_fails_cleanly_without_creating_a_room(bool rejectWithNumeric)
    {
        _server.RejectWithNumeric = rejectWithNumeric;

        var report = await Run(Options(password: "wrong"), (_, _) => { });

        var registration = Result(report, "Registration");
        Assert.Equal(ProbeOutcome.Fail, registration.Outcome);
        Assert.Contains("rejected", registration.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Received, r => r.Line.Contains("!mp make", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skips_player_checks_and_still_closes_when_nobody_joins()
    {
        var report = await Run(Options(joinWait: TimeSpan.FromMilliseconds(100)), (_, _) => { });

        Assert.Equal(ProbeOutcome.Skipped, Result(report, "Game and IRC on one account").Outcome);
        Assert.Equal(ProbeOutcome.Skipped, Result(report, "Match flow").Outcome);
        Assert.True(RoomWasClosed);
    }

    [Fact]
    public async Task Stopping_early_still_closes_the_room()
    {
        using var stop = new CancellationTokenSource();

        var report = await Run(Options(joinWait: TimeSpan.FromSeconds(30)), (step, _) =>
        {
            if (step == ProbeStep.JoinRoom)
            {
                stop.CancelAfter(50);
            }
        }, stop.Token);

        Assert.Equal(ProbeOutcome.Info, Result(report, "Run").Outcome);
        Assert.True(RoomWasClosed);
    }

    [Fact]
    public async Task Keeps_the_room_open_when_asked()
    {
        var report = await Run(Options(keepRoom: true, joinWait: TimeSpan.FromMilliseconds(100)), (_, _) => { });

        Assert.Equal(ProbeOutcome.Info, Result(report, "Room left open").Outcome);
        Assert.False(RoomWasClosed);
    }

    [Fact]
    public async Task Checks_a_kept_room_and_then_closes_it()
    {
        _server.AddRoom(900);

        var report = await Run(Options(checkRoom: 900), (_, _) => { });

        Assert.Equal(ProbeOutcome.Pass, Result(report, "Kept room still open").Outcome);
        Assert.Contains(_server.Received, r => r.Line == "PRIVMSG #mp_900 :!mp close");
    }

    [Fact]
    public async Task Reports_a_kept_room_that_is_gone()
    {
        var report = await Run(Options(checkRoom: 404), (_, _) => { });

        Assert.Equal(ProbeOutcome.Fail, Result(report, "Kept room still open").Outcome);
        Assert.False(RoomWasClosed);
    }

    [Fact]
    public async Task Api_checks_are_skipped_without_an_oauth_client()
    {
        var report = await Run(Options(api: false), (step, text) => Player(step, text, echoChat: true));

        Assert.Equal(ProbeOutcome.Skipped, Result(report, "Match history API").Outcome);
    }

    [Fact]
    public async Task Lists_BanchoBot_lines_the_parser_does_not_know()
    {
        var report = await Run(Options(), (step, text) =>
        {
            if (step == ProbeStep.JoinRoom)
            {
                _server.FromBot(_server.LastChannel, "A brand new kind of message");
            }

            Player(step, text, echoChat: true);
        });

        Assert.Contains("A brand new kind of message", report.Unrecognized);
    }
}
