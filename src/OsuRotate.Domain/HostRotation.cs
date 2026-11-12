namespace OsuRotate.Domain;

// Who hosts next. Ported from osu-ahr's AutoHostSelector, whose tests define these rules:
// - The front of the queue is the host. Newcomers join at the back.
// - The queue rotates when a match starts and the new front becomes host when it finishes, so a
//   host who leaves mid-match does not disturb the order.
// - A match aborted before anyone finished does not count: the same host keeps the turn.
// - If the previous host picked the map and then left, the next host's first start doesn't rotate.
// - A manual host transfer by players is undone, and the transferring host loses the turn.
public sealed class HostRotation(Room room, DenyList denyList, Action<LobbyCommand> send)
{
    private readonly List<Player> _queue = [];
    private Player? _mapChanger;

    public IReadOnlyList<Player> Queue => _queue;
    public bool NeedsRotate { get; private set; } = true;

    public void OnPlayerJoined(Player player)
    {
        if (denyList.Contains(player))
        {
            return;
        }

        _queue.Add(player);
        if (_queue.Count == 1)
        {
            ChangeHost();
        }
    }

    public void OnPlayerLeft(Player player, bool fromRoomState)
    {
        _queue.Remove(player);
        if (room.IsMatching || fromRoomState || _queue.Count == 0)
        {
            return;
        }

        if (room.Host is null && room.PendingHost is null && !room.IsHostCleared)
        {
            ChangeHost();
        }
    }

    public void OnHostChanged(Player newHost)
    {
        if (room.IsMatching)
        {
            return;
        }

        if (_queue.FirstOrDefault() != newHost)
        {
            // No pending transfer means a player handed host over by hand.
            if (room.PendingHost is null)
            {
                Rotate();
            }

            ChangeHost();
        }

        if (_mapChanger is not null && _mapChanger != newHost)
        {
            NeedsRotate = false;
        }
    }

    public void OnBeatmapChanging()
    {
        if (_queue.FirstOrDefault() != room.Host)
        {
            // A host who kept the turn after an abort tried to change the map.
            ChangeHost();
            NeedsRotate = false;
        }
        else
        {
            NeedsRotate = true;
            _mapChanger = room.Host;
        }
    }

    public void OnMatchStarted()
    {
        if (room.PendingHost is null && NeedsRotate)
        {
            Rotate();
        }
    }

    public void OnMatchFinished()
    {
        NeedsRotate = true;
        _mapChanger = null;
        ChangeHost();
    }

    public void OnMatchAborted(int playersFinished)
    {
        if (playersFinished > 0)
        {
            OnMatchFinished();
        }
        else if (room.Host is not null)
        {
            NeedsRotate = false;
        }
        else
        {
            ChangeHost();
        }
    }

    // Rebuilds the queue from !mp settings: kept as it is when a few players came and went,
    // rebuilt in slot order otherwise.
    public void OnRoomStateApplied(RoomState state, RoomChanges changes)
    {
        if (room.Host is null)
        {
            _queue.Clear();
        }

        if (_queue.Count == 0 || room.Host is null || !_queue.Contains(room.Host))
        {
            OrderBySlot(state);
        }
        else
        {
            var updated = _queue.Concat(changes.Joined).Where(p => !changes.Left.Contains(p) && !denyList.Contains(p)).ToList();
            if (IsValid(updated))
            {
                Replace(updated);
            }
            else
            {
                OrderBySlot(state);
            }
        }

        if (room.Host is not null && !denyList.Contains(room.Host))
        {
            SkipTo(room.Host);
        }
        else
        {
            ChangeHost();
        }
    }

    public void OnDenied(Player player)
    {
        if (_queue.Remove(player) && room.Host == player)
        {
            ChangeHost();
        }
    }

    public void OnAllowed(Player player)
    {
        if (room.Contains(player) && !_queue.Contains(player))
        {
            OnPlayerJoined(player);
        }
    }

    public void Skip()
    {
        Rotate();
        ChangeHost();
    }

    public bool SkipTo(Player target)
    {
        if (!_queue.Contains(target))
        {
            return false;
        }

        while (_queue[0] != target)
        {
            Rotate();
        }

        ChangeHost();
        return true;
    }

    // After a restart: the saved order for players still here, then anyone new in slot order. The host
    // only moves when asked; during a match it moves when the match finishes.
    public void Restore(IReadOnlyList<Player> order, RoomState state, bool changeHost)
    {
        var queue = order.Where(p => room.Contains(p) && !denyList.Contains(p)).Distinct().ToList();
        queue.AddRange(state.Players
            .OrderBy(p => p.Slot)
            .Select(p => room.GetOrAdd(p.Username))
            .Where(p => room.Contains(p) && !denyList.Contains(p) && !queue.Contains(p)));
        Replace(queue);
        NeedsRotate = true;
        if (changeHost)
        {
            ChangeHost();
        }
    }

    // Listed players go first in the given order, everyone else keeps their relative order.
    public bool Reorder(IReadOnlyList<Player> order)
    {
        var updated = order.Where(p => room.Contains(p) && !denyList.Contains(p)).Distinct().ToList();
        if (updated.Count == 0)
        {
            return false;
        }

        updated.AddRange(_queue.Where(p => !updated.Contains(p)));
        if (!IsValid(updated))
        {
            return false;
        }

        Replace(updated);
        ChangeHost();
        return true;
    }

    private void OrderBySlot(RoomState state) =>
        Replace(state.Players.OrderBy(p => p.Slot).Select(p => room.GetOrAdd(p.Username)).Where(p => !denyList.Contains(p)).ToList());

    private bool IsValid(List<Player> queue) => queue.All(p => room.Contains(p) && !denyList.Contains(p));

    private void Replace(List<Player> queue)
    {
        _queue.Clear();
        _queue.AddRange(queue);
    }

    private void Rotate()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        var current = _queue[0];
        _queue.RemoveAt(0);
        _queue.Add(current);
    }

    private void ChangeHost()
    {
        if (_queue.Count == 0)
        {
            if (room.Host is not null)
            {
                send(new ClearHost());
            }

            return;
        }

        var next = _queue[0];
        if (next != room.Host)
        {
            room.PendingHost = next;
            send(new TransferHost(next.Name, next.OsuUserId));
        }
    }
}
