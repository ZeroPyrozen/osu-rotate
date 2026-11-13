namespace OsuRotate.Domain;

// The lobby keeper, ported from osu-ahr's LobbyKeeper: holds the room's team and score mode, size,
// mods, password and title to the settings, and puts them back when they drift. Drift shows up in
// !mp settings (asked for after each match), or in slots players join and leave.
// osu-ahr's host-kick counterattack needs match history and comes with phase 4.
public sealed partial class LobbyEngine
{
    private readonly SlotKeeper _slotKeeper;

    private RoomKeeping Keeping => _settings.Keeping;

    private void KeeperOnRoomEntered()
    {
        FixTitle();
        FixModeAndSize();
        FixMods();
        FixPassword();
    }

    private void KeeperOnPlayerJoined(int slot)
    {
        if (_slotKeeper.Join(slot, _time.GetUtcNow()))
        {
            FixModeAndSize();
        }
    }

    private void KeeperOnPlayerLeft(int slot) => _slotKeeper.Leave(slot, _time.GetUtcNow());

    private void KeeperOnPlayerMoved(int from, int to)
    {
        if (_slotKeeper.Move(from, to, _time.GetUtcNow()))
        {
            FixModeAndSize();
        }
    }

    private void KeeperOnMatchFinished()
    {
        // Bancho does not say when the password changes, so it is set again after every match.
        FixPassword();
        if (_slotKeeper.HasUnusedSlot(_time.GetUtcNow()))
        {
            FixModeAndSize();
        }

        if (Keeping.AnyChecked)
        {
            RequestRoomState();
        }
    }

    private void KeeperOnRoomState(RoomState state)
    {
        var slotsDrifted = _slotKeeper.Apply(state.Players.Select(p => p.Slot), _time.GetUtcNow());
        if (ModeDrifted(state) || slotsDrifted)
        {
            FixModeAndSize();
        }

        if (Keeping.Mods is { } mods && RoomModes.ParseMods(state.ActiveMods) != mods)
        {
            FixMods();
        }

        if (Keeping.Title is { } title && state.Name != title)
        {
            FixTitle();
        }
    }

    private bool ModeDrifted(RoomState state) =>
        Keeping.Mode is { } mode
        && !(RoomModes.TryParseTeamMode(state.TeamMode, out var team) && team == mode.Team
            && RoomModes.TryParseScoreMode(state.WinCondition, out var score) && score == mode.Score);

    private void ChangeKeeping(string command, string argument)
    {
        var before = Keeping;
        if (KeepCommand.Apply(before, command, argument) is not { } change)
        {
            return;
        }

        UpdateSettings(_settings with { Keeping = change.After });

        // No longer keeping a password: open the room rather than leave the old one behind.
        if (before.Password is { Length: > 0 } && change.After.Password is null)
        {
            _output.Add(new SetPassword(null));
        }

        switch (change.Fix)
        {
            case KeepFix.ModeAndSize:
                FixModeAndSize();
                break;
            case KeepFix.Mods:
                FixMods();
                break;
            case KeepFix.Password:
                FixPassword();
                break;
            case KeepFix.Title:
                FixTitle();
                break;
        }

        Announce(change.Message);
    }

    private void FixModeAndSize()
    {
        int? size = Keeping.Size > 0 ? Keeping.Size : null;
        if (Keeping.Mode is { } mode)
        {
            _output.Add(new SetTeamAndScoreMode((int)mode.Team, (int)mode.Score, size));
        }
        else if (size is { } kept)
        {
            _output.Add(new SetSize(kept));
        }

        _slotKeeper.Reset(_time.GetUtcNow());
    }

    private void FixMods()
    {
        if (Keeping.Mods is { } mods)
        {
            _output.Add(new SetMods(mods.ToMpValue()));
        }
    }

    // !mp make rooms start with a random password from Bancho. Unless the rules keep one, the new
    // room is opened, as osu-ahr did; "not kept" still leaves a password alone after that, and a
    // room the bot rejoins is never changed here.
    private void KeeperOnRoomCreated()
    {
        if (Keeping.Password is null)
        {
            _output.Add(new SetPassword(null));
        }
    }

    private void FixPassword()
    {
        if (Keeping.Password is { } password)
        {
            _output.Add(new SetPassword(password.Length > 0 ? password : null));
        }
    }

    private void FixTitle()
    {
        if (Keeping.Title is { } title)
        {
            _output.Add(new SetRoomName(title));
        }
    }
}
