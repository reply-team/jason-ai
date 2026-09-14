using System.Text.Json.Nodes;
using Jason.Cli;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The real CLI against the real runtime in one process: every verb composes its body, posts it over the
/// loopback API through the descriptor the runtime published, and prints what came back. What is asserted here
/// is what an agent driving Jason from a shell actually sees — the JSON on stdout and the exit code.
/// </summary>
public class CliAgainstRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_campaign_is_created_described_filled_with_contacts_and_archived_through_the_cli()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var created = await OkAsync(fixture, "campaign", "create", "--name", "LatAm", "--context", "{\"icp\":\"founders\"}");
        var campaign = (string)created["id"]!;
        Assert.StartsWith("cmp_", campaign, StringComparison.Ordinal);
        Assert.Equal("draft", (string?)created["status"]);
        Assert.Equal("founders", (string?)created["context"]!["icp"]);

        var described = await OkAsync(fixture, "--actor", "role:planner", "campaign", "update-context", campaign, "--set", "{\"tone\":\"direct\"}");
        Assert.Equal("direct", (string?)described["context"]!["tone"]);
        Assert.Equal("founders", (string?)described["context"]!["icp"]);

        // The chronicle carries both changes, and the second one remembers who asked for it.
        var entries = (await OkAsync(fixture, "journal", "list", "--campaign", campaign))["items"]!.AsArray();
        var creation = Single(entries, "campaign_created");
        Assert.Equal("name", (string?)creation["key"]);
        Assert.Equal("LatAm", (string?)creation["new"]);
        Assert.Equal("human", (string?)creation["actor"]!["type"]);
        var contextChange = Single(entries, "context_updated");
        Assert.Equal("tone", (string?)contextChange["key"]);
        Assert.Equal("direct", (string?)contextChange["new"]);
        Assert.Equal("role", (string?)contextChange["actor"]!["type"]);
        Assert.Equal("planner", (string?)contextChange["actor"]!["id"]);

        // A contact the import will have to recognize rather than duplicate. The address is stored normalized.
        var ana = await OkAsync(fixture, "contact", "create", "--email", "A@B.co", "--first-name", "Ana");
        Assert.Equal("a@b.co", (string?)ana["channels"]!.AsArray()[0]!["value"]);

        var file = Path.Combine(fixture.Paths.Root, "contacts.json");
        await File.WriteAllTextAsync(
            file,
            """
            [
              { "first_name": "Ana", "channels": [{ "channel": "email", "value": "a@b.co" }] },
              { "first_name": "Ben", "channels": [{ "channel": "email", "value": "ben@example.test" }] },
              { "first_name": "Cleo", "channels": [{ "channel": "email", "value": "cleo at example" }] }
            ]
            """,
            Ct);

        var batch = await OkAsync(fixture, "campaign", "add-contacts", campaign, "--file", file, "--match-by", "email");
        Assert.Equal(2, (int)batch["summary"]!["added"]!);
        Assert.Equal(0, (int)batch["summary"]!["already_member"]!);
        Assert.Equal(1, (int)batch["summary"]!["rejected"]!);
        var items = batch["items"]!.AsArray();
        Assert.Equal(3, items.Count);
        Assert.Equal("added", (string?)items[0]!["status"]);
        Assert.Equal((string?)ana["id"], (string?)items[0]!["contact_id"]);
        Assert.False((bool)items[0]!["contact_created"]!);
        Assert.Equal("added", (string?)items[1]!["status"]);
        Assert.True((bool)items[1]!["contact_created"]!);
        Assert.Equal("rejected", (string?)items[2]!["status"]);
        Assert.Equal("validation_failed", (string?)items[2]!["error"]!["code"]);
        Assert.Equal("contacts[2].channels[0].value", (string?)items[2]!["error"]!["details"]!.AsArray()[0]!["field"]);

        var members = await OkAsync(fixture, "campaign", "list-contacts", campaign);
        Assert.Equal(2, members["items"]!.AsArray().Count);

        // The lifecycle. Asking twice for the state a campaign is already in is a retry, not an error.
        Assert.Equal("active", (string?)(await OkAsync(fixture, "campaign", "start", campaign))["status"]);
        Assert.Equal("paused", (string?)(await OkAsync(fixture, "campaign", "pause", campaign))["status"]);
        Assert.Equal("paused", (string?)(await OkAsync(fixture, "campaign", "pause", campaign))["status"]);
        Assert.Equal("archived", (string?)(await OkAsync(fixture, "campaign", "archive", campaign, "--reason", "the quarter is over"))["status"]);

        var refused = await ErrorAsync(fixture, "campaign", "start", campaign);
        Assert.Equal("campaign_archived", (string?)refused["error"]!["code"]);

        // An archived campaign leaves the working list and is found only when asked for by status.
        var open = await OkAsync(fixture, "campaign", "list");
        Assert.DoesNotContain(open["items"]!.AsArray(), item => (string?)item!["id"] == campaign);
        var archived = await OkAsync(fixture, "campaign", "list", "--status", "archived");
        Assert.Contains(archived["items"]!.AsArray(), item => (string?)item!["id"] == campaign);

        // Listing by channel normalizes the value the same way storing it did.
        var found = await OkAsync(fixture, "contact", "list", "--channel", "email", "--value", "A@B.CO");
        Assert.Equal((string?)ana["id"], (string?)Assert.Single(found["items"]!.AsArray())!["id"]);
    }

    [Fact]
    public async Task Suppressing_a_value_and_lifting_it_again_go_through_the_cli()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var added = await OkAsync(fixture, "suppression", "add", "--channel", "email", "--value", "A@B.co");
        Assert.StartsWith("sup_", (string)added["id"]!, StringComparison.Ordinal);
        Assert.Equal("a@b.co", (string?)added["value"]);

        var listed = await OkAsync(fixture, "suppression", "list", "--channel", "email");
        Assert.Equal("a@b.co", (string?)Assert.Single(listed["items"]!.AsArray())!["value"]);

        var removed = await OkAsync(fixture, "suppression", "remove", "--channel", "email", "--value", "a@b.co", "--reason", "typo");
        Assert.True((bool)removed["removed"]!);

        var again = await OkAsync(fixture, "suppression", "remove", "--channel", "email", "--value", "a@b.co", "--reason", "typo");
        Assert.False((bool)again["removed"]!);
        Assert.Empty((await OkAsync(fixture, "suppression", "list"))["items"]!.AsArray());
    }

    [Fact]
    public async Task An_api_error_is_printed_verbatim_with_exit_code_one()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var blank = await ErrorAsync(fixture, "campaign", "get", " ");
        Assert.Equal("validation_failed", (string?)blank["error"]!["code"]);
        Assert.Equal("campaign_id", (string?)blank["error"]!["details"]!.AsArray()[0]!["field"]);

        var unknown = await ErrorAsync(fixture, "campaign", "get", "cmp_nothing");
        Assert.Equal("campaign_not_found", (string?)unknown["error"]!["code"]);
    }

    private static JsonObject Single(JsonArray entries, string kind) =>
        Assert.Single(entries, entry => (string?)entry!["kind"] == kind)!.AsObject();

    private static async Task<JsonObject> OkAsync(RuntimeApiFixture fixture, params string[] args)
    {
        var (exit, output, error) = await RunAsync(fixture, args);
        Assert.Equal(string.Empty, error);
        Assert.Equal(ExitCodes.Success, exit);
        return JsonNode.Parse(output)!.AsObject();
    }

    private static async Task<JsonObject> ErrorAsync(RuntimeApiFixture fixture, params string[] args)
    {
        var (exit, output, _) = await RunAsync(fixture, args);
        Assert.Equal(ExitCodes.ApiError, exit);
        return JsonNode.Parse(output)!.AsObject();
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(RuntimeApiFixture fixture, string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(args, new CliEnvironment(output, error, fixture.Paths), Ct);
        return (exit, output.ToString().Trim(), error.ToString().Trim());
    }
}
