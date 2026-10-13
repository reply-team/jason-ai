// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The grammar reply-cli 0.11.0 documents for its eleven work commands: how many ids each takes, which options take a
/// value, and which are switches. An option outside a command's own list is commander's usage error, exit 2.
/// </summary>
internal static class Commands
{
    private static readonly string[] ContactOptions =
        ["--contact-id", "--email", "--first-name", "--last-name", "--company", "--title", "--contact"];

    private static readonly Dictionary<string, (int Operands, string[] Values, string[] Switches)> Grammar =
        new(StringComparer.Ordinal)
        {
            ["sequence get"] = (1, [], []),
            ["sequence contacts"] = (1, ["--limit", "--offset"], []),
            ["sequence stats"] = (1, ["--preset", "--from", "--to"], []),
            ["sequence add-contact"] = (1, [.. ContactOptions, "--start-step"], ["--ignore-step-delay"]),
            ["sequence remove-contact"] = (1, ContactOptions, []),
            ["sequence pause"] = (1, [], []),
            ["sequence start"] = (1, ["--expect-contacts"], []),
            ["contact-list add-contact"] = (1, ContactOptions, ["--refuse-opted-out"]),
            ["contact opt-out"] = (0, ContactOptions, []),
            ["inbox list"] = (0, ["--contact-id", "--sequence-id", "--source", "--since", "--limit", "--offset"], []),
            ["inbox get"] = (1, ["--limit", "--offset"], []),
        };

    public static bool IsKnown(string command) => Grammar.ContainsKey(command);

    /// <summary>Whether the command line names stdin as the contact, which is the only thing it ever reads from there.</summary>
    public static bool ReadsStdin(IReadOnlyList<string> rest)
    {
        for (var at = 0; at < rest.Count - 1; at++)
        {
            if (rest[at] == "--contact" && rest[at + 1] == "-")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The ids and options of one command line, or the usage error commander would print for it.</summary>
    public static Parsed Parse(string command, IReadOnlyList<string> rest)
    {
        var (count, values, switches) = Grammar[command];
        var operands = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var on = new HashSet<string>(StringComparer.Ordinal);

        for (var at = 0; at < rest.Count; at++)
        {
            var token = rest[at];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                operands.Add(token);
                continue;
            }

            if (switches.Contains(token))
            {
                on.Add(token);
                continue;
            }

            if (!values.Contains(token))
            {
                return Parsed.Usage($"unknown option '{token}'");
            }

            if (at + 1 >= rest.Count)
            {
                return Parsed.Usage($"option '{token}' argument missing");
            }

            options[token] = rest[++at];
        }

        if (operands.Count != count)
        {
            return Parsed.Usage(operands.Count < count ? "missing required argument" : $"too many arguments for '{command}'");
        }

        return new Parsed(operands, options, on, null);
    }
}

/// <summary>One parsed command line: its ids, its options, its switches — or the usage error instead.</summary>
internal sealed record Parsed(
    IReadOnlyList<string> Operands,
    IReadOnlyDictionary<string, string> Options,
    IReadOnlySet<string> Switches,
    string? UsageError)
{
    public static Parsed Usage(string message) =>
        new([], new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), message);

    public string? Option(string name) => Options.TryGetValue(name, out var value) ? value : null;

    public bool Has(string name) => Switches.Contains(name);
}
