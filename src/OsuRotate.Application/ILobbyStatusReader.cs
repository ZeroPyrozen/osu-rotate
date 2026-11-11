using OsuRotate.Domain;

namespace OsuRotate.Application;

public interface ILobbyStatusReader
{
    LobbyStatus Current { get; }
}
