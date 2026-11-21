using System.CommandLine;
using Jason.Contracts.Execution;

namespace Jason.Cli;

/// <summary>
/// The commands the CLI refuses under <c>JASON_CONFINED</c> (<see cref="Confinement"/>): a person's own word and the
/// acts that change this installation. The desktop sets the variable on every chat turn that can only read and propose:
/// Plan only on any host, and every Codex turn outside Full access (Ask first, and Auto-edit, which runs as it), so what an
/// agent runs there is refused here, locally, before any request; no other chat turn sets it, and there the turn's mode
/// decides, with the agent acting on the person's word. This table is also the command
/// half of <c>desktop/chat-deny-list.txt</c>, which <c>DesktopDenyListTests</c> writes and checks from it, so the two
/// cannot drift apart.
/// </summary>
/// <remarks>
/// A higher bar, not a boundary: the runtime cannot tell a person from a program that holds the token, and a caller that
/// clears the variable, or runs a program that speaks to the runtime itself, is not stopped. What this removes is the
/// easy way, a script the agent writes that calls a verb with <c>--actor human:&lt;Name&gt;</c>. An attempt id in the
/// environment is likewise a claim and not a proof, and buys no exception (<see cref="Refuses"/>).
/// </remarks>
public static class ConfinedVerbs
{
    /// <summary>The kinds of entry, which the deny list's file groups under one comment each.</summary>
    public enum Kind
    {
        /// <summary>A person's own word: the verbs that take no default actor in the person's name.</summary>
        PersonsWord,

        /// <summary>Verbs that change the installation, follow it without end, or carry it off the machine.</summary>
        Installation,

        /// <summary>Verbs that change who is reached or what runs, and take no default actor.</summary>
        Acts,

        /// <summary>
        /// Verbs the chat refuses beyond the CLI's no-default set: adding a role, linking a project's folder, starting a
        /// campaign or resuming one, which is what lets its queued work run, and archiving one, which no verb undoes: it
        /// cancels its open work but a search's stop, with each item's approval whether still asked or already given,
        /// stops its running attempts once the archive is committed, and cancels its pending questions.
        /// </summary>
        ChatsOwn,
    }

    /// <summary>Every refused command, a command covering the commands under it, in the order the deny list prints them.</summary>
    public static IReadOnlyList<(Kind Kind, string Command)> Table { get; } =
    [
        (Kind.PersonsWord, "approval approve"),
        (Kind.PersonsWord, "approval reject"),
        (Kind.PersonsWord, "approval set-standing"),
        (Kind.PersonsWord, "approval unset-standing"),
        (Kind.PersonsWord, "decision answer"),
        (Kind.PersonsWord, "preference set"),
        (Kind.PersonsWord, "preference unset"),
        (Kind.PersonsWord, "campaign set-send-mode"),
        (Kind.PersonsWord, "block clear"),
        (Kind.PersonsWord, "workitem resume"),
        (Kind.PersonsWord, "workitem review"),
        (Kind.PersonsWord, "report submit"),
        (Kind.PersonsWord, "subscription rewind"),
        (Kind.Installation, "runtime"),
        (Kind.Installation, "plugin install"),
        (Kind.Installation, "plugin reload"),
        (Kind.Installation, "profile"),
        (Kind.Installation, "skills"),
        (Kind.Installation, "update"),
        (Kind.Installation, "uninstall"),
        (Kind.Installation, "install"),
        (Kind.Installation, "ui"),
        (Kind.Installation, "session"),
        (Kind.Installation, "journal watch"),
        (Kind.Installation, "export"),
        (Kind.Installation, "import"),
        (Kind.Acts, "suppression remove"),
        (Kind.Acts, "route set"),
        (Kind.Acts, "route unset"),
        (Kind.Acts, "plugin grant"),
        (Kind.Acts, "plugin revoke"),
        (Kind.Acts, "role set-profile"),
        (Kind.Acts, "notification enable"),
        (Kind.Acts, "notification disable"),
        (Kind.ChatsOwn, "role add"),
        (Kind.ChatsOwn, "project link"),
        (Kind.ChatsOwn, "campaign start"),
        (Kind.ChatsOwn, "campaign archive"),
    ];

    /// <summary>The one sentence a refused command is answered with, on standard error.</summary>
    public const string Sentence =
        "Under JASON_CONFINED this is refused: it is a person's word or changes this installation; the person does it in Jason's window or a terminal.";

    /// <summary>
    /// What a refused command's <c>--help</c> says, true for every caller: that the variable refuses it, before any request.
    /// An attempt id in the environment is no exception under the variable: it is a claim, not a proof.
    /// </summary>
    public const string Help =
        "Under JASON_CONFINED, which Jason Desktop sets on every chat turn that can only read and propose, it is refused before any request. "
        + "A higher bar, not a boundary: the runtime cannot tell a person from a program that holds the token, "
        + "and an attempt id in the environment is a claim, not a proof, so it changes nothing here.";

    /// <summary>
    /// The entries of a person's own word that a role runs itself, whose help therefore never says the act is the
    /// person's: <c>workitem review</c>, which the critic sends from its own run, <c>report submit</c>, whose reporter may
    /// be a role, and <c>workitem resume</c>, which ends a wait on the word of whoever is named, a person or not.
    /// </summary>
    public static IReadOnlyList<string> RunByARole { get; } = ["workitem review", "report submit", "workitem resume"];

    /// <summary>
    /// What the help of a person's own word says before <see cref="Help"/>, on every entry of that kind but those a role
    /// runs itself (<see cref="RunByARole"/>).
    /// </summary>
    public const string PersonsWordHelp =
        "The act is the person's: an agent runs it when the person said so, and it is recorded as theirs.";

    /// <summary>The entry that covers <paramref name="verb"/> (<c>runtime</c> covers <c>runtime stop</c>), or null.</summary>
    public static string? Covering(string verb) =>
        Table.Select(entry => entry.Command).FirstOrDefault(entry => verb == entry || verb.StartsWith(entry + " ", StringComparison.Ordinal));

    /// <summary>
    /// Whether <paramref name="verb"/> is refused for a call whose environment is <paramref name="env"/>: the variable is
    /// set and an entry covers the verb, whatever else the environment says. An attempt id in the environment is a claim and
    /// not a proof, so it buys no exception: a role attempt never carries the variable (the runtime drops it from the role
    /// work it launches), and a chat turn whose agent wrote an attempt id in front would otherwise have a way round the bar.
    /// </summary>
    public static bool Refuses(CliEnvironment env, string verb)
    {
        ArgumentNullException.ThrowIfNull(env);
        return env.Confined() && Covering(verb) is not null;
    }

    /// <summary>Appends the rule to the description of every command the table covers, so its <c>--help</c> states it.</summary>
    internal static void Describe(Command root)
    {
        Walk(root, string.Empty);

        static void Walk(Command command, string verb)
        {
            foreach (var sub in command.Subcommands)
            {
                var path = verb.Length == 0 ? sub.Name : verb + " " + sub.Name;
                if (Covering(path) is { } covering)
                {
                    var personsOwn = Table.Single(entry => entry.Command == covering).Kind == Kind.PersonsWord && !RunByARole.Contains(covering);
                    var help = personsOwn ? PersonsWordHelp + " " + Help : Help;
                    sub.Description = string.IsNullOrEmpty(sub.Description) ? help : sub.Description + " " + help;
                }

                Walk(sub, path);
            }
        }
    }
}
