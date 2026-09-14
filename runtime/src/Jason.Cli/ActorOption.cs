using System.CommandLine;
using System.Text.Json.Nodes;

namespace Jason.Cli;

/// <summary>
/// The global <c>--actor &lt;type&gt;[:&lt;id&gt;]</c> claim, so a skill running as a role journals under that role and
/// a running executor journals under its attempt. The CLI only parses the text: claims are verified by the
/// runtime, and the one rule enforced here is that <c>system</c> belongs to the runtime's own writes and
/// cannot be claimed from outside.
/// </summary>
public static class ActorOption
{
    public const string Name = "--actor";

    public static Option<string?> Create() => new(Name)
    {
        Description = "Who performs the operation: human, human:<id>, role:<id> or attempt:att_… for a running executor.",
        Recursive = true,
    };

    /// <summary>Turns the option text into the <c>actor</c> object of a request body; absent text means no claim.</summary>
    public static JsonObject? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var separator = text.IndexOf(':');
        var type = separator < 0 ? text : text[..separator];
        var id = separator < 0 ? null : text[(separator + 1)..];

        if (type is "system")
        {
            throw new UsageException($"{Name} 'system' is reserved for the runtime's own writes; claim 'human' or 'role:<id>'.");
        }

        if (type is not "human" and not "role" and not "attempt")
        {
            throw new UsageException($"{Name} must be 'human', 'human:<id>', 'role:<id>' or 'attempt:att_…'.");
        }

        if (separator >= 0 && string.IsNullOrWhiteSpace(id))
        {
            throw new UsageException($"{Name} '{type}:' names no id; write '{type}:<id>' or just '{type}'.");
        }

        // An attempt is a specific run: the claim is meaningless without the attempt it names.
        if (type is "attempt" && id is null)
        {
            throw new UsageException($"{Name} 'attempt' names no attempt; write 'attempt:att_…'.");
        }

        var actor = new JsonObject { ["type"] = type };
        if (id is not null)
        {
            actor["id"] = id;
        }

        return actor;
    }
}
