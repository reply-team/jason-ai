using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's two commands on sequences by name, which <c>campaign.create</c> runs, answered as reply-cli 0.13.0
/// documents them: <c>sequence list --name -</c>, every sequence whose whole name is the one on stdin ignoring case,
/// archived ones included, oldest first; and <c>sequence create --name -</c>, a draft of that name in Reply's <c>new</c>
/// state with one task step, the account's default schedule and mailbox attached though none was asked for, and a
/// <c>created</c> moment written with two fractional digits and an offset. The name travels on stdin and nowhere else.
/// </summary>
public class ReplyStubSequencesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] List => ["--json", "-q", "sequence", "list", "--name", "-"];

    private static string[] Create => ["--json", "-q", "sequence", "create", "--name", "-"];

    private static string Named(string name) => new JsonObject { ["name"] = name }.ToJsonString();

    [Fact]
    public async Task The_list_answers_every_sequence_of_the_whole_name_ignoring_case_oldest_first_archived_ones_included()
    {
        using var account = new ReplyAccount()
            .WithSequence(7, "Q3 LatAm founders", "active", false, 11)
            .WithSequence(8, "q3 latam FOUNDERS", "paused", true, 21)
            .WithSequence(9, "Q3 LatAm founders (copy)", "new", false, 31)
            .WithSequence(10, "Q4 Nordics", "new", false, 41)
            .CreatedAt(7, new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero))
            .CreatedAt(8, new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));

        var (exit, stdout, stderr) = await account.RunAsync(List, Named("Q3 LATAM founders"), Ct);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        var items = JsonNode.Parse(stdout)!["items"]!.AsArray();
        Assert.Equal([8, 7], items.Select(item => item!["id"]!.GetValue<int>()));
        var oldest = items[0]!.AsObject();
        Assert.Equal(["created_at", "id", "is_archived", "name", "status"], oldest.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal(
            ("q3 latam FOUNDERS", "paused", true, "2026-09-01T09:00:00.00+00:00"),
            ((string?)oldest["name"], (string?)oldest["status"], (bool)oldest["is_archived"]!, (string?)oldest["created_at"]));

        var call = Assert.Single(account.Calls);
        Assert.Equal("""{"name":"Q3 LATAM founders"}""", call.Body);
        Assert.DoesNotContain("LATAM", string.Join(' ', call.Args), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_nobody_holds_is_an_empty_list()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3 LatAm founders", "active", false, 11);

        var (exit, stdout, _) = await account.RunAsync(List, Named("Q3 LatAm"), Ct);

        Assert.Equal(0, exit);
        Assert.Equal("""{"items":[]}""", JsonNode.Parse(stdout)!.ToJsonString());
    }

    /// <summary>The name is read from stdin only; inline, blank, or beside another field, it is a usage error that never repeats it.</summary>
    [Theory]
    [InlineData("--name", """{"name":"Q4 Nordics"}""", null)]
    [InlineData("--name", "-", """{"name":"   "}""")]
    [InlineData("--name", "-", """{"name":"Q4 Nordics","steps":[]}""")]
    [InlineData("--name", "-", """{"name": Q4 Nordics}""")]
    public async Task A_name_not_given_on_stdin_as_its_one_field_is_refused_without_repeating_it(string flag, string value, string? stdin)
    {
        using var account = new ReplyAccount();

        foreach (var verb in new[] { "list", "create" })
        {
            var (exit, stdout, stderr) = await account.RunAsync(["--json", "-q", "sequence", verb, flag, value], stdin, Ct);

            Assert.Equal(2, exit);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal("usage.name", (string?)JsonNode.Parse(stderr)!["error"]!["code"]);
            Assert.DoesNotContain("Nordics", stderr, StringComparison.Ordinal);
        }

        Assert.Empty(account.Sequences);
    }

    [Fact]
    public async Task A_create_makes_a_draft_with_one_task_step_and_the_default_schedule_and_mailbox()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3 LatAm founders", "active", false, 11);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var (exit, stdout, stderr) = await account.RunAsync(Create, Named("Q4 Nordics"), Ct);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal("""{"sequence_id":2001,"steps_added":1,"status":"new","is_archived":false}""", JsonNode.Parse(stdout)!.ToJsonString());

        var made = account.Sequences.OfType<JsonObject>().Single(sequence => (int)sequence["id"]! == 2001);
        Assert.Equal("Q4 Nordics", (string?)made["name"]);
        Assert.NotNull(made["scheduleId"]);
        Assert.Single(made["emailAccounts"]!.AsArray());
        var step = Assert.Single(made["steps"]!.AsArray())!;
        Assert.Equal(("task", "toDo"), ((string?)step["type"], (string?)step["actionType"]));

        // Two fractional digits and an offset, as a live account wrote it.
        var created = (string)made["created"]!;
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{2}\+00:00$"), created);
        Assert.InRange(DateTimeOffset.Parse(created, CultureInfo.InvariantCulture), before, DateTimeOffset.UtcNow.AddSeconds(1));
    }

    /// <summary>Reply allows two sequences of one name, and the command does not look first: a list before it is the caller's.</summary>
    [Fact]
    public async Task A_create_of_a_name_the_account_holds_already_makes_a_second()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q4 Nordics", "new", false, 11);

        var (exit, _, _) = await account.RunAsync(Create, Named("Q4 Nordics"), Ct);

        Assert.Equal(0, exit);
        Assert.Equal(2, account.Sequences.Count(sequence => (string?)sequence!["name"] == "Q4 Nordics"));
    }

    /// <summary>
    /// A create whose answer is lost does everything it would have done, and then ends as reply-cli ends a write whose
    /// outcome it could not see: nothing on stdout, <c>outcome.unknown</c> on stderr, exit 3 — and no identifier, because
    /// only the lost answer would have carried the new sequence's.
    /// </summary>
    [Fact]
    public async Task A_create_told_to_lose_its_outcome_makes_the_sequence_and_exits_three()
    {
        using var account = new ReplyAccount();
        account.LosesTheOutcome("sequence create");

        var (exit, stdout, stderr) = await account.RunAsync(Create, Named("Q4 Nordics"), Ct);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, stdout);
        var error = JsonNode.Parse(stderr)!["error"]!;
        Assert.Equal("outcome.unknown", (string?)error["code"]);
        Assert.Null(error["ids"]);
        Assert.Single(account.Sequences);

        // Spent once: the list after it finds what it made.
        var (again, listed, _) = await account.RunAsync(List, Named("q4 nordics"), Ct);
        Assert.Equal(0, again);
        Assert.Equal([2001], JsonNode.Parse(listed)!["items"]!.AsArray().Select(item => (int)item!["id"]!));
    }
}
