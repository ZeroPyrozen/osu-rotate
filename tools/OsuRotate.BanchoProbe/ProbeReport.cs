using System.Globalization;
using System.Text;

namespace OsuRotate.BanchoProbe;

internal enum ProbeOutcome
{
    Pass,
    Fail,
    Info,
    Skipped,
}

internal sealed record ProbeResult(string Name, ProbeOutcome Outcome, string Detail);

internal sealed class ProbeReport
{
    private readonly Lock _gate = new();
    private readonly List<ProbeResult> _results = [];
    private readonly List<string> _transcript = [];
    private readonly HashSet<string> _unrecognized = [];

    public IReadOnlyList<ProbeResult> Results
    {
        get
        {
            lock (_gate)
            {
                return [.. _results];
            }
        }
    }

    public IReadOnlyCollection<string> Unrecognized
    {
        get
        {
            lock (_gate)
            {
                return [.. _unrecognized];
            }
        }
    }

    public void Add(string name, ProbeOutcome outcome, string detail)
    {
        lock (_gate)
        {
            _results.Add(new ProbeResult(name, outcome, detail));
        }
    }

    public void AddUnrecognized(string line)
    {
        lock (_gate)
        {
            _unrecognized.Add(line);
        }
    }

    public void Log(string direction, string line)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            _transcript.Add($"{stamp} {direction} {line}");
        }
    }

    public string ToMarkdown()
    {
        lock (_gate)
        {
            var text = new StringBuilder();
            text.AppendLine("# Bancho live probe").AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Run at {DateTimeOffset.UtcNow:u}").AppendLine();
            text.AppendLine("| Check | Outcome | Detail |").AppendLine("| --- | --- | --- |");
            foreach (var result in _results)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {result.Name} | {result.Outcome} | {result.Detail.Replace("|", "\\|", StringComparison.Ordinal)} |");
            }

            text.AppendLine().AppendLine("## BanchoBot lines the parser did not recognize").AppendLine();
            text.AppendLine(_unrecognized.Count == 0 ? "None." : string.Join(Environment.NewLine, _unrecognized.Select(line => $"- `{line}`")));
            text.AppendLine().AppendLine("## Transcript").AppendLine().AppendLine("```");
            foreach (var line in _transcript)
            {
                text.AppendLine(line);
            }

            return text.AppendLine("```").ToString();
        }
    }
}
