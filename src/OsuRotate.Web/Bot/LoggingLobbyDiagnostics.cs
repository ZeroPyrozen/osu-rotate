using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Web.Bot;

// The actor's failures and its trace, in the journal. Failures name types and ids; the trace comes
// from LobbyTrace, which leaves out chat text and the room password.
internal sealed partial class LoggingLobbyDiagnostics(ILogger<LoggingLobbyDiagnostics> logger) : ILobbyDiagnostics
{
    public void InputFailed(LobbyInput input, Exception exception) => LogInputFailed(logger, input.GetType().Name, exception);

    public void SendFailed(LobbyCommand command, Exception exception) => LogSendFailed(logger, command.GetType().Name, exception);

    public void LookupFailed(int beatmapId, Exception exception) => LogLookupFailed(logger, beatmapId, exception);

    public void EventsEnded(Exception? exception)
    {
        if (exception is null)
        {
            LogEventsEnded(logger);
        }
        else
        {
            LogEventsFailed(logger, exception);
        }
    }

    public void SaveFailed(Exception exception) => LogSaveFailed(logger, exception);

    public void Received(LobbyInput input)
    {
        if (logger.IsEnabled(LogLevel.Information) && LobbyTrace.Describe(input) is { } line)
        {
            LogReceived(logger, line);
        }
    }

    public void Sending(LobbyCommand command)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            LogSending(logger, LobbyTrace.Describe(command));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Lobby: {Line}")]
    private static partial void LogReceived(ILogger logger, string line);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bot: {Line}")]
    private static partial void LogSending(ILogger logger, string line);

    [LoggerMessage(Level = LogLevel.Error, Message = "The lobby could not handle {Input}; it carries on.")]
    private static partial void LogInputFailed(ILogger logger, string input, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not send {Command} to Bancho.")]
    private static partial void LogSendFailed(ILogger logger, string command, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Looking up beatmap {BeatmapId} failed; the map is let through.")]
    private static partial void LogLookupFailed(ILogger logger, int beatmapId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Bancho connection has stopped for good (for example, wrong IRC credentials).")]
    private static partial void LogEventsEnded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Bancho connection failed and has stopped.")]
    private static partial void LogEventsFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the lobby failed; it carries on and tries again with the next change.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
