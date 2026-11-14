using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// The names this program answers to, read from its own help pages the way a person reads them: the top-level names
/// from <c>jason --help</c>, and a noun's verbs from <c>jason &lt;noun&gt; --help</c>. Read from the program rather than
/// typed here, so a guard that refuses a verb in some text refuses the verbs that exist, including the next one added.
/// </summary>
internal static class ProgramVerbs
{
    /// <summary>The names listed under "Commands:" on the help page of <paramref name="path"/>.</summary>
    public static async Task<IReadOnlyList<string>> NamesAsync(params string[] path)
    {
        var lines = (await HelpAsync(path)).Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim().Equals("Commands:", StringComparison.Ordinal));
        var names = new List<string>();
        for (var index = start + 1; start >= 0 && index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            // The next section's heading, which is the one thing at column zero.
            if (!char.IsWhiteSpace(line[0]))
            {
                break;
            }

            // A row is indented to the name column; anything indented further is a wrapped description.
            if (line.Length - line.TrimStart().Length != 2)
            {
                continue;
            }

            names.Add(line.Trim().Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries)[0]);
        }

        return names;
    }

    /// <summary>
    /// Whether <c>jason &lt;noun&gt; &lt;verb&gt;</c> reads the Runtime API and changes nothing, decided from what the
    /// code already says rather than from a list typed here. It is an API operation — <c>noun.verb</c>, a hyphen read as
    /// an underscore, is one of <see cref="Operations"/> — and its own help page offers neither <c>--reason</c>, which
    /// every request that changes campaign state carries, nor <c>--attempt</c>, the fencing token of an executor's
    /// operation, which changes an attempt. A verb with no operation behind it — the runtime's process, an update, a
    /// deployment of skills — is about the installation, not a read of campaign state.
    /// </summary>
    public static async Task<bool> IsReadAsync(string noun, string verb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noun);
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);

        var operation = $"{noun}.{verb.Replace('-', '_')}";
        if (!OperationNames().Contains(operation, StringComparer.Ordinal))
        {
            return false;
        }

        var help = await HelpAsync(noun, verb);
        return !help.Contains("--reason", StringComparison.Ordinal) && !help.Contains("--attempt", StringComparison.Ordinal);
    }

    /// <summary>Every Runtime API operation name, read from <see cref="Operations"/>.</summary>
    private static IReadOnlyList<string> OperationNames() =>
    [
        .. typeof(Operations)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!),
    ];

    /// <summary>The help page of <paramref name="path"/>, line endings normalised.</summary>
    private static async Task<string> HelpAsync(params string[] path)
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Root);
        var output = new StringWriter();
        var exit = await CliApp.RunAsync(
            [.. path, "--help"],
            new CliEnvironment(output, new StringWriter(), new JasonPaths(tree.Root, 1)),
            TestContext.Current.CancellationToken);

        var text = output.ToString();
        Assert.True(exit == 0, $"'jason {string.Join(' ', path)} --help' exited {exit}, so its page cannot be read:{Environment.NewLine}{text}");
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>Every noun and verb pair the program answers to, as "noun verb".</summary>
    public static async Task<IReadOnlyList<string>> PairsAsync()
    {
        var pairs = new List<string>();
        foreach (var noun in await NamesAsync())
        {
            foreach (var verb in await NamesAsync(noun))
            {
                pairs.Add($"{noun} {verb}");
            }
        }

        return pairs;
    }
}
