using System.CommandLine;
using Jason.Cli;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests;

/// <summary>
/// A word that looks like an option and is not one of the verb's is a usage error that names it, on every verb — never
/// a value a positional argument takes. A misspelt <c>--summary</c> read as a campaign id came back from the runtime as
/// "no campaign with id '--sumary'": refused, and refused for the wrong reason.
/// </summary>
public class UnknownOptionTests
{
    [Fact]
    public async Task A_misspelt_option_before_its_value_is_named_and_nothing_is_sent()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "list-contacts", "--sumary", "waiting");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("'--sumary'", cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, cli.Requests);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("summary")]
    [InlineData("activity")]
    [InlineData("activity-by-period")]
    [InlineData("list-contacts")]
    [InlineData("audience")]
    public async Task An_unknown_option_where_a_campaign_id_goes_is_not_read_as_the_id(string verb)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", verb, "--bogus-option");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("'--bogus-option'", cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, cli.Requests);
    }

    /// <summary>
    /// After a named campaign as well: a list of ids is not a place an option goes to hide. The sentence says how a value
    /// that does begin with a dash is given, since a provider's identifier may.
    /// </summary>
    [Fact]
    public async Task An_unknown_option_after_a_campaign_id_is_not_read_as_another()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "summary", "cmp_A", "-x");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("'-x'", cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "'-x' is not an option of 'jason campaign summary', or give it after -- to use it as a value.",
            cli.Error.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(0, cli.Requests);
    }

    /// <summary>
    /// Every verb this program has, read from the program itself, so a verb added tomorrow is held by this row the day it
    /// arrives. The parse is refused before any action runs, so no verb here reaches a runtime, a file or the machine.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryVerb))]
    public async Task Every_verb_names_an_unknown_option_as_a_usage_error(string verb)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync([.. verb.Split(' '), "--bogus-option"]);

        Assert.True(exit == ExitCodes.Usage, $"'jason {verb} --bogus-option' exited {exit}, saying: {cli.Error}");
        Assert.Contains("'--bogus-option'", cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, cli.Requests);
    }

    /// <summary>A value that does start with a dash still has a way in: after <c>--</c>, everything is a value.</summary>
    [Fact]
    public async Task After_a_double_dash_a_dash_is_a_value()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "get", "--", "-odd");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignGet, "{\"campaign_id\":\"-odd\"}");
    }

    /// <summary>And an option's own value may start with one: only what a positional argument took is held to this.</summary>
    [Fact]
    public async Task An_option_s_value_may_start_with_a_dash()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "update", "cmp_A", "--name", "-dash-");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignUpdate, "{\"campaign_id\":\"cmp_A\",\"name\":\"-dash-\"}");
    }

    public static TheoryData<string> EveryVerb()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var verbs = new TheoryData<string>();
        foreach (var path in Leaves(root, []))
        {
            verbs.Add(string.Join(' ', path));
        }

        return verbs;
    }

    private static IEnumerable<string[]> Leaves(Command command, string[] path)
    {
        if (command.Subcommands.Count == 0)
        {
            yield return path;
            yield break;
        }

        foreach (var sub in command.Subcommands)
        {
            foreach (var leaf in Leaves(sub, [.. path, sub.Name]))
            {
                yield return leaf;
            }
        }
    }
}
