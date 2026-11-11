namespace OsuRotate.Domain;

// A share of the room, with a floor, decides a vote: 5 players at 50% with a minimum of 2 need 3.
public sealed record VoteRule(double Share, int Minimum);

// Ported from osu-ahr's VoteCounter. Only voters may vote, each once. A vote that passed stays passed
// until Clear, so late votes cannot trigger the action twice.
public sealed class VoteCounter(VoteRule rule)
{
    private readonly Dictionary<Player, bool> _voters = [];

    public int Required => (int)Math.Ceiling(Math.Max(_voters.Count * rule.Share, rule.Minimum));
    public int Count => _voters.Values.Count(voted => voted);
    public bool Passed { get; private set; }

    public bool Vote(Player player)
    {
        if (!_voters.TryGetValue(player, out var voted) || voted)
        {
            return false;
        }

        _voters[player] = true;
        Check();
        return true;
    }

    public void AddVoter(Player player) => _voters.TryAdd(player, false);

    public void RemoveVoter(Player player)
    {
        _voters.Remove(player);
        Check();
    }

    // Forgets the votes but keeps the voters.
    public void Clear()
    {
        foreach (var player in _voters.Keys)
        {
            _voters[player] = false;
        }

        Passed = false;
    }

    public void SetVoters(IEnumerable<Player> players)
    {
        _voters.Clear();
        foreach (var player in players)
        {
            _voters[player] = false;
        }

        Passed = false;
    }

    public override string ToString() => $"{Count} / {Required}";

    private void Check()
    {
        if (Count > 0 && Count >= Required)
        {
            Passed = true;
        }
    }
}
