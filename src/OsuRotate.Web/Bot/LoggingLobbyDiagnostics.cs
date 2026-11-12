using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Web.Bot;

// The actor's failures, in the journal. Messages name types and ids, never chat text or secrets.
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
