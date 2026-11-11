using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Infrastructure;

// Stands in until the lobby actor exists. The bot does not connect to Bancho yet.
internal sealed class IdleLobbyStatusReader : ILobbyStatusReader
{
    public LobbyStatus Current => LobbyStatus.Idle;
}
