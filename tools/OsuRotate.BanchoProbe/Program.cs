using System.Globalization;
using System.Text;
using OsuRotate.BanchoProbe;

var keepRoom = args.Contains("--keep-room");
var skipMatch = args.Contains("--skip-match");
var checkRoom = OptionValue(args, "--check-room") is { } room ? long.Parse(room, CultureInfo.InvariantCulture) : (long?)null;
var idleMinutes = OptionValue(args, "--idle-minutes") is { } minutes ? double.Parse(minutes, CultureInfo.InvariantCulture) : 6;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("osu-rotate Bancho probe");
Console.WriteLine("Checks how real Bancho behaves, using a test room that is closed at the end.");
Console.WriteLine();

var nick = Environment.GetEnvironmentVariable("OSU_ROTATE_IRC_NICK") ?? Prompt("osu! username: ");
var password = (Environment.GetEnvironmentVariable("OSU_ROTATE_IRC_PASSWORD") ?? PromptHidden("IRC password (from osu.ppy.sh/home/account/edit#legacy-api, not your account password): ")).Trim();
var clientId = Environment.GetEnvironmentVariable("OSU_ROTATE_CLIENT_ID") ?? Prompt("osu! OAuth client ID (press Enter to skip the API checks): ");
var clientSecret = Environment.GetEnvironmentVariable("OSU_ROTATE_CLIENT_SECRET") ?? (string.IsNullOrWhiteSpace(clientId) ? null : PromptHidden("osu! OAuth client secret: "));

var options = new ProbeOptions
{
    Host = OptionValue(args, "--host") ?? "irc.ppy.sh",
    Port = OptionValue(args, "--port") is { } port ? int.Parse(port, CultureInfo.InvariantCulture) : 6667,
    Nick = nick,
    Password = password,
    ClientId = clientId,
    ClientSecret = clientSecret,
    KeepRoom = keepRoom,
    SkipMatch = skipMatch,
    CheckRoom = checkRoom,
    IdleDuration = TimeSpan.FromMinutes(idleMinutes),
};

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("Stopping and closing the test room...");
    stop.Cancel();
};

using var http = new HttpClient();
var report = await new ProbeRunner(options, new ConsoleUi(), http).RunAsync(stop.Token);

Console.WriteLine();
foreach (var result in report.Results)
{
    Console.WriteLine($"[{result.Outcome,-7}] {result.Name}: {result.Detail}");
}

Console.WriteLine();
Console.WriteLine(report.Unrecognized.Count == 0 ? "The parser recognized every BanchoBot line." : $"The parser did not recognize {report.Unrecognized.Count} BanchoBot line(s). They are listed in the report.");

var folder = Path.Combine(RepositoryRoot(), "artifacts", "probe");
Directory.CreateDirectory(folder);
var path = Path.Combine(folder, $"probe-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.md");
await File.WriteAllTextAsync(path, report.ToMarkdown());
Console.WriteLine($"Full report: {path}");

static string? OptionValue(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

static string Prompt(string label)
{
    Console.Write(label);
    return Console.ReadLine()?.Trim() ?? string.Empty;
}

static string PromptHidden(string label)
{
    Console.Write(label);
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? string.Empty;
    }

    var text = new StringBuilder();
    while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            if (text.Length > 0)
            {
                text.Length--;
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            text.Append(key.KeyChar);
        }
    }

    Console.WriteLine();
    return text.ToString();
}

static string RepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OsuRotate.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? Directory.GetCurrentDirectory();
}

internal sealed class ConsoleUi : IProbeUi
{
    public void Info(string text) => Console.WriteLine(text);

    public void Instruct(ProbeStep step, string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($">>> YOUR TURN: {text}");
        Console.ResetColor();
    }
}
