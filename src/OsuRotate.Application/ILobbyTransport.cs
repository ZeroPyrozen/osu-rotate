using OsuRotate.Domain;

namespace OsuRotate.Application;

public sealed record TransportCapabilities(bool CanSetHost, bool HasChat);

public interface ILobbyTransport
{
    TransportCapabilities Capabilities { get; }

    // Single consumer. Enumerating runs the connection; ending the enumeration stops it.
    IAsyncEnumerable<LobbyEvent> Events(CancellationToken cancellationToken);

    ValueTask SendAsync(LobbyCommand command, CancellationToken cancellationToken);
}
