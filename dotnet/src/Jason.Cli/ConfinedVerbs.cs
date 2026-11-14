using System.CommandLine;
using Jason.Contracts.Execution;

namespace Jason.Cli;

/// <summary>
/// The commands the CLI refuses under <c>JASON_CONFINED</c> (<see cref="Confinement"/>): a person's own word and the
/// acts that change this installation. The desktop sets the variable on every chat turn, so what an agent in the chat
/// runs is refused here, locally, before any request; the person gives these in Jason's window or in a terminal, which
/// never carries the variable. This table is also the command half of <c>desktop/chat-deny-list.txt</c>, which
/// <c>DesktopDenyListTests</c> writes and checks from it, so the two cannot drift apart.
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

        /// <summary>Verbs the chat refuses beyond the CLI's no-default set.</summary>
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
        (Kind.ChatsOwn, "role add"),
        (Kind.ChatsOwn, "project link"),
    ];

    /// <summary>The one sentence a refused command is answered with, on standard error.</summary>
    public const string Sentence =
        "Under JASON_CONFINED this is refused: it is a person's word or changes this installation; the person does it in Jason's window or a terminal.";

    /// <summary>What a refused command's <c>--help</c> says. An attempt id in the environment is no exception: it is a claim, not a proof.</summary>
    public const string Help =
        "Under JASON_CONFINED (set on every chat turn of the desktop) this is refused before any request: it is not for an agent "
        + "to do, and the person does it in Jason's window or a terminal. A higher bar, not a boundary: "
        + "the runtime cannot tell a person from a program that holds the token, "
        + "and an attempt id in the environment is a claim, not a proof, so it changes nothing here.";

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
                if (Covering(path) is not null)
                {
                    sub.Description = string.IsNullOrEmpty(sub.Description) ? Help : sub.Description + " " + Help;
                }

                Walk(sub, path);
            }
        }
    }
}
