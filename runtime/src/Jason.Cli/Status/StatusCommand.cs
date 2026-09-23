using System.CommandLine;
using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Human;
using Jason.Contracts.Json;

namespace Jason.Cli.Status;

/// <summary>
/// <c>jason status</c>: can this installation start work? One question, one answer per check, and a boolean an
/// agent branches on.
/// </summary>
/// <remarks>
/// <para>
/// The one command in this CLI that is not one-to-one with an API operation, and it cannot be: half of what it
/// reports is not the runtime's to know — an executable on PATH, another vendor's CLI, files in a folder in
/// the operator's home directory. No API operation can answer "am I ready to work?", because the runtime is
/// not the authority on the machine it runs on.
/// </para>
/// <para>
/// It exits 0 when every required check passed and 1 when one did not, and <b>never 3</b>. Exit 3 means "I
/// could not ask the runtime", and this is the verb whose whole job is to answer when the runtime cannot be
/// asked. The exception is published where the exit-code table lives and in this command's own help, and the
/// body carries <c>ready</c> so that a caller reading it never has to know about any of this.
/// </para>
/// </remarks>
public static class StatusCommand
{
    /// <summary>
    /// What a check is allowed to be. Printed in help because an exit code means nothing until "required" is
    /// defined, and because a person reading a failure needs to know whether it is one.
    /// </summary>
    private const string Partition =
        """
        Required — a failure exits 1:   the runtime answers · migrations are applied · the plugin registry is
                                        alive (zero plugins is alive) · role skills are present, named after
                                        their directories and within the runtime's live cap · 'jason' resolves
                                        on PATH
        Optional — absent never fails:  a provider plugin is installed · a route is configured · a binding
                                        names an account · a provider CLI you name answers · the skill packs
                                        are deployed to an agent harness · autostart is registered

        The provider CLI is only checked when you name it: --provider-cli <program> runs '<program> --version'
        with a ten-second bound and reports whether it answered. Which provider you use is yours, so this build
        knows no vendor's command by heart.

        Exit codes: 0 when every required check passed, 1 when one did not. Never 3: "the runtime did not
        answer" is this command's answer rather than a reason it has none.
        """;

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var command = new Command("status", "Whether this installation can start work.")
        {
            Description = "Whether this installation can start work." + Environment.NewLine + Environment.NewLine + Partition,
        };

        var human = VerbOptions.Human();
        var providerCli = new Option<string?>("--provider-cli")
        {
            Description = "A provider CLI to check for, run as '<program> --version'. Named by you: this build knows no vendor's command.",
        };

        command.Options.Add(human);
        command.Options.Add(providerCli);
        command.SetAction((parse, cancellationToken) => RunAsync(env, parse.GetValue(human), parse.GetValue(providerCli), cancellationToken));
        return command;
    }

    public static async Task<int> RunAsync(CliEnvironment env, bool human, string? providerCli, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var report = await StatusChecks.ComposeAsync(env, providerCli, cancellationToken).ConfigureAwait(false);

        if (human)
        {
            Render(env.Out, report);
        }
        else
        {
            env.Out.WriteLine(JsonSerializer.Serialize(report, JasonJson.Options));
        }

        return report.Ready ? ExitCodes.Success : ExitCodes.ApiError;
    }

    private static void Render(TextWriter output, StatusReport report)
    {
        output.WriteLine(report.Ready ? "Ready." : "Not ready.");
        output.WriteLine();

        var table = new HumanTable("CHECK", "", "STATE", "WHAT WAS SEEN");
        foreach (var check in report.Checks)
        {
            table.Row(check.Name, check.Required ? "required" : "optional", Word(check.State), check.Fact);
        }

        output.WriteLine(table.Render());

        // Each line once, in the order it was first called for. Four checks that all want the runtime
        // started want it started once, and a list that printed `jason runtime start` four times was the
        // first thing a new installation said to whoever had just installed it.
        var repairs = report.Checks
            .Where(check => check.State != CheckState.Ok && check.Fix is not null)
            .Select(check => check.Fix!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (repairs.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine("To repair:");
        foreach (var repair in repairs)
        {
            output.WriteLine($"  {repair}");
        }
    }

    private static string Word(CheckState state) => JsonNamingPolicy.SnakeCaseLower.ConvertName(state.ToString());
}
