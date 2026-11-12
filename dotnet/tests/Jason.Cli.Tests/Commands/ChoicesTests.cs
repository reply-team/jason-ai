using System.CommandLine;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Commands;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// A value outside an option's list is refused by the CLI before any request, naming the option and the values it takes
/// in the CLI's words. The runtime's own refusal of the same body named the request type its JSON binding could not
/// build, blamed the JSON, and listed nothing, and an agent lost a turn reading it.
/// </summary>
public class ChoicesTests
{
    [Fact]
    public async Task A_state_outside_the_list_is_refused_in_the_cli_s_words_before_any_request()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "list-contacts", "cmp_A", "--state", "failed");

        Assert.Equal(ExitCodes.Usage, exit);
        var error = cli.Error.ToString();
        Assert.Contains("--state takes enrolled, paused, finished or excluded; 'failed' is not one of them.", error, StringComparison.Ordinal);
        AssertInTheCliSWords(error);
        Assert.Equal(0, cli.Requests);
    }

    [Fact]
    public async Task A_work_item_status_outside_the_list_names_every_status()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "list", "--status", "failed", "--status", "bogus");

        Assert.Equal(ExitCodes.Usage, exit);
        var error = cli.Error.ToString();
        Assert.Contains(
            "--status takes created, awaiting_approval, scheduled, processing, succeeded, failed, cancelled, expired or waiting; 'bogus' is not one of them.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("'failed'", error, StringComparison.Ordinal);
        AssertInTheCliSWords(error);
        Assert.Equal(0, cli.Requests);
    }

    /// <summary>
    /// The API's JSON reads some other spellings of a word too, so what the CLI takes is what the runtime reads — no less,
    /// or a request the runtime would serve is refused, and no more. The value goes as typed.
    /// </summary>
    [Fact]
    public async Task A_spelling_the_api_reads_is_taken_and_sent_as_typed()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "list", "--status", "Active");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignList, "{\"status\":\"Active\"}");
    }

    [Theory]
    [InlineData("awaiting_approval")]
    [InlineData("AwaitingApproval")]
    [InlineData("AWAITING_APPROVAL")]
    [InlineData("awaiting-approval")]
    [InlineData("1")]
    [InlineData("")]
    public void The_cli_takes_a_spelling_exactly_when_the_api_s_json_reads_it(string spelling)
    {
        bool read;
        try
        {
            JsonSerializer.Deserialize<WorkItemStatus>(JsonSerializer.Serialize(spelling), JasonJson.Options);
            read = true;
        }
        catch (JsonException)
        {
            read = false;
        }

        Assert.Equal(read, Choice<WorkItemStatus>.Reads(spelling));
    }

    /// <summary>
    /// The kind half of <c>decision raise --reference kind:id</c> is an API enum's word as well, held by the same words
    /// though it is not an option of its own: a kind the API's JSON cannot read is refused before anything is sent, and
    /// the help names every kind.
    /// </summary>
    [Fact]
    public async Task A_reference_kind_outside_the_list_is_refused_in_the_cli_s_words_before_any_request()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("decision", "raise", "wi_1", "--attempt", "att_1", "--question", "q", "--reference", "journal:jrn_1");

        Assert.Equal(ExitCodes.Usage, exit);
        var error = cli.Error.ToString();
        Assert.Contains(
            $"--reference takes a kind of {Choices.Spoken(Words<DecisionReferenceKind>())}; 'journal' is not one of them.",
            error,
            StringComparison.Ordinal);
        AssertInTheCliSWords(error);
        Assert.Equal(0, cli.Requests);
    }

    [Fact]
    public void The_help_of_reference_names_every_kind()
    {
        using var paths = new TempPaths();
        var raise = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths))
            .Subcommands.Single(command => command.Name == "decision")
            .Subcommands.Single(command => command.Name == "raise");
        var help = raise.Options.Single(option => option.Name == "--reference").Description ?? string.Empty;

        Assert.All(Words<DecisionReferenceKind>(), word => Assert.Matches($@"(?<![a-z_]){word}(?![a-z_])", help));
    }

    /// <summary>Each option whose value is an API enum's, refusing a word outside it before anything is sent.</summary>
    [Theory]
    [MemberData(nameof(Held))]
    public async Task Each_enum_valued_option_refuses_a_word_outside_its_enum(string verb, string option, string[] words)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync([.. verb.Split(' '), option, "not_a_word"]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains($"{option} takes {Choices.Spoken(words)}; 'not_a_word' is not one of them.", cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, cli.Requests);
    }

    /// <summary>
    /// The list above is exactly the options the program holds to an enum, read from the program: one held without a row
    /// here, or a row whose option takes any text, is a difference between this file and the code. An enum-valued option
    /// nobody held is in neither, and this row does not find it. An option the API takes as text — a profile's host and
    /// tools — is not held: the runtime judges those words and refuses them in its own.
    /// </summary>
    [Fact]
    public void The_held_options_are_exactly_the_listed_ones()
    {
        var expected = Held().Select(row => $"{row.Data.Item1} {row.Data.Item2}: {string.Join(", ", row.Data.Item3)}").Order(StringComparer.Ordinal);

        Assert.Equal(expected, Program().Select(held => $"{held.Verb} {held.Option.Name}: {string.Join(", ", held.Words)}").Order(StringComparer.Ordinal));
    }

    /// <summary>And the help of each one names every word it takes, so a person reading it is never refused a word it offered.</summary>
    [Fact]
    public void Every_held_option_s_help_names_every_word_it_takes()
    {
        foreach (var (verb, option, words) in Program())
        {
            foreach (var word in words)
            {
                Assert.True(
                    System.Text.RegularExpressions.Regex.IsMatch(option.Description ?? string.Empty, $@"(?<![a-z_]){word}(?![a-z_])"),
                    $"The help of 'jason {verb} {option.Name}' does not name '{word}': {option.Description}");
            }
        }
    }

    public static TheoryData<string, string, string[]> Held() => new()
    {
        { "approval list", "--status", Words<ApprovalStatus>() },
        { "block list", "--status", Words<ProviderBlockStatus>() },
        { "campaign activity-by-period", "--count", Words<CampaignActivityFilter>() },
        { "campaign activity-by-period", "--period", Words<ActivityPeriod>() },
        { "campaign list", "--status", Words<CampaignStatus>() },
        { "campaign list-contacts", "--activity", Words<CampaignActivityFilter>() },
        { "campaign list-contacts", "--state", Words<MembershipState>() },
        { "campaign list-contacts", "--summary", Words<CampaignSummaryFilter>() },
        { "contact list", "--order", Words<ContactListOrder>() },
        { "contact list", "--summary", Words<CampaignSummaryFilter>() },
        { "decision list", "--status", Words<DecisionStatus>() },
        { "reply record", "--reaction", Words<ReplyReaction>() },
        { "workitem complete", "--status", Words<CompletionStatus>() },
        { "workitem count", "--kind", Words<WorkItemKind>() },
        { "workitem count", "--order", Words<WorkItemListOrder>() },
        { "workitem count", "--status", Words<WorkItemStatus>() },
        { "workitem create", "--kind", Words<WorkItemKind>() },
        { "workitem list", "--kind", Words<WorkItemKind>() },
        { "workitem list", "--order", Words<WorkItemListOrder>() },
        { "workitem list", "--status", Words<WorkItemStatus>() },
    };

    /// <summary>An enum's words as the API's JSON writes them, read here independently of the code under test.</summary>
    private static string[] Words<TEnum>()
        where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => JsonSerializer.Serialize(value, JasonJson.Options).Trim('"'))];

    private static List<(string Verb, Option Option, IReadOnlyList<string> Words)> Program()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var held = new List<(string, Option, IReadOnlyList<string>)>();
        Walk(root, string.Empty, held);
        return held;
    }

    private static void Walk(Command command, string path, List<(string, Option, IReadOnlyList<string>)> held)
    {
        foreach (var option in command.Options)
        {
            if (Choices.Of(option) is { } words)
            {
                held.Add((path, option, words));
            }
        }

        foreach (var sub in command.Subcommands)
        {
            Walk(sub, path.Length == 0 ? sub.Name : $"{path} {sub.Name}", held);
        }
    }

    private static void AssertInTheCliSWords(string error)
    {
        Assert.DoesNotContain("Jason.Contracts", error, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON", error, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be converted", error, StringComparison.Ordinal);
    }
}
