using OsuRotate.Domain;

namespace OsuRotate.Application;

// Where the actor reports what went wrong without stopping. The host logs these; Application has no
// logging dependency of its own.
public interface ILobbyDiagnostics
{
    void InputFailed(LobbyInput input, Exception exception);

    void SendFailed(LobbyCommand command, Exception exception);

    void LookupFailed(int beatmapId, Exception exception);

    void EventsEnded(Exception? exception);

    // The lobby went on without saving; the next change tries again.
    void SaveFailed(Exception exception);

    // The trace: every input the lobby takes (timer ticks aside) and every command it sends, in order.
    // LobbyTrace turns them into journal lines without chat text.
    void Received(LobbyInput input);

    void Sending(LobbyCommand command);
}

public sealed class NoLobbyDiagnostics : ILobbyDiagnostics
{
    public void Received(LobbyInput input)
    {
    }

    public void Sending(LobbyCommand command)
    {
    }

    public static NoLobbyDiagnostics Instance { get; } = new();

    public void InputFailed(LobbyInput input, Exception exception)
    {
    }

    public void SendFailed(LobbyCommand command, Exception exception)
    {
    }

    public void LookupFailed(int beatmapId, Exception exception)
    {
    }

    public void EventsEnded(Exception? exception)
    {
    }

    public void SaveFailed(Exception exception)
    {
    }
}
