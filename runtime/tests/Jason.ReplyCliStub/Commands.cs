// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The grammar reply-cli documents, as of the release <see cref="Jason.ReplyCliStub.ReplyCliRelease"/> names, for the
/// twenty-eight work commands the official package runs, and for <c>auth whoami</c>, the one command it runs that is not
/// a work command: how many ids each takes, which options take a value, and which are switches. An option outside a
/// command's own list is commander's usage error, exit 2. <c>contact update</c> takes its id as an optional argument, or
/// the contact by the contact options instead.
/// </summary>
internal static class Commands
{
    private static readonly string[] ContactOptions =
        ["--contact-id", "--email", "--first-name", "--last-name", "--company", "--title", "--contact"];

    /// <summary>The count of a command whose one id may be left out: <c>contact update [contact-id]</c>.</summary>
    private const int OptionalId = -1;

    private static readonly Dictionary<string, (int Operands, string[] Values, string[] Switches)> Grammar =
        new(StringComparer.Ordinal)
        {
            ["sequence list"] = (0, ["--name"], []),
            ["sequence create"] = (0, ["--name"], []),
            ["sequence get"] = (1, [], []),
            ["sequence contacts"] = (1, ["--limit", "--offset"], []),
            ["sequence stats"] = (1, ["--preset", "--from", "--to"], []),
            ["sequence add-contact"] = (1, [.. ContactOptions, "--start-step"], ["--ignore-step-delay"]),
            ["sequence remove-contact"] = (1, ContactOptions, []),
            ["sequence pause"] = (1, [], []),
            ["sequence start"] = (1, ["--expect-contacts"], []),
            ["contact-list add-contact"] = (1, ContactOptions, ["--refuse-opted-out"]),
            ["contact find"] = (0, ["--contact"], []),
            ["contact get"] = (1, [], []),
            ["contact update"] = (OptionalId, [.. ContactOptions, "--fields"], []),
            ["contact delete"] = (1, [], []),
            ["contact opt-out"] = (0, ContactOptions, []),
            ["inbox list"] = (0, ["--contact-id", "--sequence-id", "--source", "--channel", "--category-id", "--since", "--limit", "--offset"], []),
            ["inbox get"] = (1, ["--limit", "--offset"], []),
            ["inbox reply"] = (1, ["--message"], []),
            ["inbox categories"] = (0, [], []),
            ["inbox categorize"] = (1, ["--category-id"], ["--clear"]),
            ["inbox mark-read"] = (1, [], []),
            ["inbox mark-unread"] = (1, [], []),
            ["contact linkedin-connect"] = (0, [.. ContactOptions, "--linkedin-account-id", "--message"], []),
            ["contact send-email"] = (0, [.. ContactOptions, "--email-account-id", "--message"], []),
            ["contact linkedin-message"] = (0, [.. ContactOptions, "--linkedin-account-id", "--message"], []),
            ["contact linkedin-inmail"] = (0, [.. ContactOptions, "--linkedin-account-id", "--message"], []),
            ["contact linkedin-voice"] = (0, [.. ContactOptions, "--linkedin-account-id", "--message"], []),
            ["account list"] = (0, ["--channel"], []),
            ["auth whoami"] = (0, [], []),
        };

    /// <summary>The options that name stdin as their value, which are the only things a command ever reads from there.</summary>
    private static readonly string[] StdinOptions = ["--contact", "--message", "--name", "--fields"];

    public static bool IsKnown(string command) => Grammar.ContainsKey(command);

    /// <summary>Whether the command line names stdin as the contact, the message, the name or the fields, the four things ever read from there.</summary>
    public static bool ReadsStdin(IReadOnlyList<string> rest)
    {
        for (var at = 0; at < rest.Count - 1; at++)
        {
            if (StdinOptions.Contains(rest[at]) && rest[at + 1] == "-")
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

        if (count == OptionalId ? operands.Count > 1 : operands.Count != count)
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
