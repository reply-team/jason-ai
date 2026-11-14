using System.CommandLine;
using System.Text.Json.Nodes;

namespace Jason.Cli;

/// <summary>
/// The global <c>--actor &lt;type&gt;[:&lt;id&gt;]</c> claim, so a skill running as a role journals under that role and
/// a running executor journals under its attempt. The CLI only parses the text: claims are verified by the
/// runtime, and the one rule enforced here is that <c>system</c> belongs to the runtime's own writes and
/// cannot be claimed from outside. A write that claims nobody is made as the person this account is — except the verbs
/// that take no default actor, which name who acts or are refused: somebody's own word (an approval or a rejection, a
/// standing approval set or unset, an answer, a preference set or unset, a block's clear, a resume, a report and a
/// subscription's rewind), and the acts the runtime takes from any caller but the CLI will not put this account's name on
/// by itself (a suppression lifted, a route set or unset, a role's execution profile set). Whatever it claims, a CLI
/// the environment says runs inside an attempt names that attempt on every request beside it, in the Jason-Attempt
/// header (see <see cref="Http.RuntimeClient"/>).
/// </summary>
public static class ActorOption
{
    public const string Name = "--actor";

    /// <summary>
    /// The help a verb that takes no default actor carries: approving, rejecting, setting or taking back a standing
    /// approval, answering, setting or taking back a preference, clearing a block and rewinding a subscription are a
    /// person's own word, which an account's name is not.
    /// </summary>
    public const string NoDefault = "The CLI's default actor does not apply here: name the person with --actor human:<Name>.";

    /// <summary>A resume's help: whoever is named ends the wait, a person or not.</summary>
    public const string NoDefaultForResume = "The CLI's default actor does not apply here: name who ends it, e.g. --actor human:<Name>.";

    /// <summary>
    /// The help of an act the CLI will not put this account's name on by itself: lifting a suppression, setting or
    /// unsetting a route, granting a plugin something or taking a grant back, and setting a role's execution profile.
    /// </summary>
    public const string NoDefaultForAnAct = "The CLI's default actor does not apply here: it will not put this account's name on this act by itself; name who does it, e.g. --actor human:<Name>.";

    /// <summary>A report's help: the reporter is named, a person or a role, and never taken from the machine.</summary>
    public const string NoDefaultForReport = "The CLI's default actor does not apply here: name who reports it with --actor human:<Name> or --actor role:<id>.";

    /// <summary>
    /// The help of the five verbs that are a person's own word in the runtime's eyes: approving, rejecting, setting or
    /// taking back a standing approval, and answering. The runtime refuses them a request that names an attempt.
    /// </summary>
    public const string RefusedInsideARun = "From inside a run (the environment names an attempt) this is refused: it is a person's word.";

    /// <summary>
    /// The help of the verbs that change Jason itself, granting a plugin something and taking a grant back: the runtime
    /// takes them from any actor outside a run and refuses them a request that names an attempt.
    /// </summary>
    public const string ChangesJasonInsideARun = "From inside a run (the environment names an attempt) this is refused: nothing inside a run changes Jason itself.";

    public static Option<string?> Create() => new(Name)
    {
        Description = "Who performs the operation: human, human:<id>, role:<id> or attempt:att_... for a running executor. "
            + "Without it, a running executor is its attempt, and anything else is written as human:<this account's name>, "
            + "the name the paired browser writes as; where that name cannot be read, as a person who gave no name. "
            + "Approving, rejecting, setting or unsetting a standing approval, answering, setting or unsetting a preference, "
            + "clearing a block, resuming a work item, rewinding a subscription, submitting a report, "
            + "lifting a suppression, setting or unsetting a route, granting a plugin something or taking a grant back, "
            + "and setting a role's execution profile take no default. "
            + "Inside a run every request also names its attempt in the Jason-Attempt header, whatever actor is named, "
            + "and approving, rejecting, setting or unsetting a standing approval and answering are refused there, "
            + "and so are granting or taking back a plugin's grant and setting or unsetting a route for every campaign, "
            + "since nothing inside a run changes Jason itself.",
        Recursive = true,
    };

    /// <summary>
    /// The actor a write carries when none was claimed: the person this account is, by the name the operating system
    /// gives it — the one the runtime's browser door issues its token to, so one person carries one name in a timeline
    /// whichever of the two they wrote from. A claim of the caller's own, or of an attempt the environment names, comes
    /// first; where the name cannot be read, the write names nobody, as it did before.
    /// </summary>
    public static JsonObject? Default(JsonObject? actor, string? accountName) =>
        actor ?? (string.IsNullOrWhiteSpace(accountName) ? null : new JsonObject { ["type"] = "human", ["id"] = accountName });

    /// <summary>
    /// Refuses a verb that takes no default actor and names nobody, before anything is sent, in one sentence that names the
    /// form. For a person's own word the runtime refuses it too, but only after the request, and in words that cannot say
    /// what the CLI's default was; for an act the runtime takes from any caller, the CLI alone asks who does it.
    /// <paramref name="how"/> is the form the sentence ends with: a person by default, whoever ends a wait for a resume,
    /// a person or a role for a report, whoever does it for an act.
    /// </summary>
    public static JsonObject Named(JsonObject body, string verb, string why, string how = $"write {Name} human:<Name>")
    {
        ArgumentNullException.ThrowIfNull(body);
        return body["actor"] is not null
            ? body
            : throw new UsageException($"{verb} needs {Name}: {why}, and the CLI's default actor does not apply to it; {how}.");
    }

    /// <summary>
    /// Turns the option text into the <c>actor</c> object of a request body. Absent text means no claim of the
    /// caller's own — but a CLI running inside a launched executor is that attempt whether or not it says so, and
    /// the launcher puts the attempt's id in the environment for exactly this reason. Traceability that depended
    /// on an agent remembering a flag would be lost the first time one forgot. A verb that requires an actor, such
    /// as <c>workitem resume</c>, is satisfied by that attempt in the same way. The attempt is handed in, read through
    /// <see cref="CliEnvironment.AttemptFromEnvironment"/> as the header is, so nothing here reads the process.
    /// </summary>
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

        if (type is "plugin")
        {
            throw new UsageException($"{Name} 'plugin' is set by the runtime on a plugin's own host.call and is never claimed from a command line; claim 'human' or 'role:<id>'.");
        }

        if (type is not "human" and not "role" and not "attempt")
        {
            throw new UsageException($"{Name} must be 'human', 'human:<id>', 'role:<id>' or 'attempt:att_...'.");
        }

        if (separator >= 0 && string.IsNullOrWhiteSpace(id))
        {
            throw new UsageException($"{Name} '{type}:' names no id; write '{type}:<id>' or just '{type}'.");
        }

        // An attempt is a specific run: the claim is meaningless without the attempt it names.
        if (type is "attempt" && id is null)
        {
            throw new UsageException($"{Name} 'attempt' names no attempt; write 'attempt:att_...'.");
        }

        var actor = new JsonObject { ["type"] = type };
        if (id is not null)
        {
            actor["id"] = id;
        }

        return actor;
    }
}
