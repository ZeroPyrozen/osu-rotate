using Microsoft.Extensions.Logging;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Web.Bot;

namespace OsuRotate.Web.Tests;

// Phase 6, issue 05: the trace reaches the journal at Information, without chat text.
public class LobbyTraceLoggingTests
{
    [Fact]
    public void Inputs_and_commands_are_logged_as_trace_lines()
    {
        var logger = new ListLogger(LogLevel.Information);
        var diagnostics = new LoggingLobbyDiagnostics(logger);

        diagnostics.Received(new BanchoEvent(new ChatReceived("alice", "!skip my secret words", IsAction: false)));
        diagnostics.Received(new BanchoEvent(new CountdownTick(5)));
        diagnostics.Sending(new TransferHost("bob"));

        Assert.Equal(["Information Lobby: chat from alice (!skip)", "Information Bot: !mp host bob"], logger.Lines);
    }

    [Fact]
    public void Nothing_is_logged_when_information_is_off()
    {
        var logger = new ListLogger(LogLevel.Warning);
        var diagnostics = new LoggingLobbyDiagnostics(logger);

        diagnostics.Received(new SkipHost());
        diagnostics.Sending(new AbortMatch());

        Assert.Empty(logger.Lines);
    }

    private sealed class ListLogger(LogLevel minimum) : ILogger<LoggingLobbyDiagnostics>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Lines.Add($"{logLevel} {formatter(state, exception)}");
            }
        }
    }
}
