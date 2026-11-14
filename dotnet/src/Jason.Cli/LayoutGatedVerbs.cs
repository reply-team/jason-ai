using System.CommandLine;
using Jason.Cli.Skills;
using Jason.Contracts.Discovery;

namespace Jason.Cli;

/// <summary>
/// The commands that touch a part of the data directory that moves between layouts — the plugins, the role skills, a
/// custom UI, the settings — or start a runtime that would, refused where the data directory's stamp names a layout newer
/// than this build runs (<c>layout_newer</c>) or cannot be read (<c>layout_unreadable</c>): once, for every verb, locally,
/// before anything is read, written, stopped or asked (<see cref="LayoutStamp"/>).
/// </summary>
/// <remarks>
/// <c>status</c>, <c>doctor</c> and <c>logs</c> are not here, and never will be: they are how such an installation is
/// diagnosed, so they run on whatever the stamp says, and the first two report it.
/// </remarks>
public static class LayoutGatedVerbs
{
    /// <summary>Every refused command, with the flag that makes it one where only a use of it touches a moving part.</summary>
    public static IReadOnlyList<(string Command, string? Flag)> Table { get; } =
    [
        ("runtime start", null),
        ("runtime restart", null),
        ("plugin install", null),
        ("plugin reload", null),
        ("skills install", null),
        ("skills update", null),
        ("ui install", null),
        ("update apply", null),
        ("update rollback", null),
        ("export", null),
        ("import", null),
        ("uninstall", "--purge-data"),
        ("session open", "--setup"),
    ];

    /// <summary>What a refused command's <c>--help</c> says.</summary>
    public const string Help =
        "Refused before anything is read or written where the data directory's layout.json names a layout newer than this "
        + "jason runs (layout_newer) or cannot be read (layout_unreadable).";

    /// <summary>
    /// The refusal of the command <paramref name="parseResult"/> names, in <paramref name="env"/>; null where it is not one
    /// of the table's or the stamp refuses nothing. Help is not a run of the verb.
    /// </summary>
    public static (string Code, string Message)? Refusal(CliEnvironment env, ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(parseResult);
        if (parseResult.Action is System.CommandLine.Help.HelpAction)
        {
            return null;
        }

        var verb = CliApp.Verb(parseResult);
        var entry = Table.FirstOrDefault(entry => entry.Command == verb);
        if (entry.Command is null || (entry.Flag is { } flag && !Given(parseResult, flag)))
        {
            return null;
        }

        var current = SkillsSource.BuildVersion(env);
        var refusal = LayoutStamp.Refusal(env.Paths, current);

        // "Update this jason" is what these two were asked to do, and they cannot: a build that does not know the layout,
        // or cannot read the stamp, cannot follow it. They name what does, the install line, and keep the code.
        if (refusal is { } refused && verb is "update apply" or "update rollback")
        {
            var stamp = env.Paths.Stamp!;
            return refused.Code == LayoutStamp.NewerCode
                ? (LayoutStamp.NewerCode, UpdateNewerSentence(stamp.WrittenBy!, current))
                : (LayoutStamp.UnreadableCode, UpdateUnreadableSentence(stamp.Path, stamp.Problem!));
        }

        return refusal;
    }

    /// <summary>The sentence <c>update apply</c> and <c>update rollback</c> refuse a stamp they cannot read with.</summary>
    public static string UpdateUnreadableSentence(string path, string problem) =>
        $"This data directory's layout stamp at {path} cannot be read ({problem}), so this jason cannot tell how the directory is laid out, and its update cannot either: install the newest Jason by running the install line again, or use the Jason that set it up.";

    /// <summary>The sentence <c>update apply</c> and <c>update rollback</c> refuse a newer layout with.</summary>
    public static string UpdateNewerSentence(string writtenBy, string current) =>
        $"This data directory was set up by Jason {writtenBy}, newer than this jason ({current}), and this jason's update cannot follow it: install Jason {writtenBy} or newer by running the install line again, or use the one that set it up.";

    /// <summary>Says the refusal as every local refusal is said: the envelope on stdout, or the sentence on stderr under <c>--human</c>.</summary>
    public static int Refuse(CliEnvironment env, ParseResult parseResult, (string Code, string Message) refusal)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(parseResult);
        if (Given(parseResult, "--human"))
        {
            env.Error.WriteLine(refusal.Message);
        }
        else
        {
            env.Out.WriteLine(CliErrors.Serialize(refusal.Code, refusal.Message, retryable: false));
        }

        return ExitCodes.ApiError;
    }

    /// <summary>Appends the rule to the description of every command the table names, so its <c>--help</c> states it.</summary>
    internal static void Describe(Command root)
    {
        foreach (var (command, flag) in Table)
        {
            var found = command.Split(' ').Aggregate<string, Command?>(root, (parent, name) => parent?.Subcommands.FirstOrDefault(sub => sub.Name == name));
            if (found is null)
            {
                continue;
            }

            var rule = flag is null ? Help : $"With {flag}: {char.ToLowerInvariant(Help[0])}{Help[1..]}";
            found.Description = string.IsNullOrEmpty(found.Description) ? rule : found.Description + " " + rule;
        }
    }

    private static bool Given(ParseResult parseResult, string name) =>
        parseResult.CommandResult.Command.Options.OfType<Option<bool>>().FirstOrDefault(option => option.Name == name) is { } option
        && parseResult.GetValue(option);
}
