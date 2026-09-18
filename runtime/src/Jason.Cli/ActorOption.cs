using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Contracts.Execution;

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

    /// <summary>
    /// Turns the option text into the <c>actor</c> object of a request body. Absent text means no claim of the
    /// caller's own — but a CLI running inside a launched executor is that attempt whether or not it says so, and
    /// the launcher puts the attempt's id in the environment for exactly this reason. Traceability that depended
    /// on an agent remembering a flag would be lost the first time one forgot.
    /// </summary>
    public static JsonObject? Parse(string? text) =>
        Parse(text, Environment.GetEnvironmentVariable(ExecutionEnvironment.AttemptIdVariable));

    /// <summary>The same decision with the environment handed in, so it can be read and tested without one.</summary>
    public static JsonObject? Parse(string? text, string? attemptFromEnvironment)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            // The claim is still verified against the attempt this runtime knows, so a variable set by hand buys
            // nothing that was not already true of anyone who could set it.
            return string.IsNullOrWhiteSpace(attemptFromEnvironment)
                ? null
                : new JsonObject { ["type"] = "attempt", ["id"] = attemptFromEnvironment.Trim() };
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
