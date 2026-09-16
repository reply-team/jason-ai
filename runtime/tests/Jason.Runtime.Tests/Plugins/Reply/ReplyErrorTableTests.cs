using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The package's error table, held to the documents it claims to implement. The table is data in the package's
/// own <c>modules/errors.js</c> and is read from there, so there is one source and nothing to drift: a code the
/// table names that the operation does not declare is a code the runtime would refuse, and a code named under a
/// class the document does not give it is worse — a permanent failure called transient is retried into the same
/// wall, and an ambiguous one called permanent strands work the provider may already have done.
/// </summary>
public class ReplyErrorTableTests
{
    private static readonly string[] Classes = ["transient", "permanent", "validation", "ambiguous"];

    [Theory]
    [InlineData("campaign.get")]
    [InlineData("list_membership.add")]
    [InlineData("campaign.enroll")]
    public void Every_code_the_table_names_is_one_the_operation_declares(string operation)
    {
        var contract = OperationCatalog.Find(operation);
        Assert.NotNull(contract);

        var rows = ReplyErrorTable.Rows(operation);
        Assert.NotEmpty(rows);

        foreach (var (key, row) in rows)
        {
            var code = Text(row, "code");
            var declared = Assert.Single(contract.FailureCodes, failure => failure.Code == code);
            Assert.True(
                Enum.TryParse<FailureClass>(Text(row, "class"), ignoreCase: true, out var mapped),
                $"row '{key}' of {operation} reports class '{Text(row, "class")}', which is not a failure class.");
            Assert.Equal(declared.Class, mapped);
        }
    }

    [Theory]
    [InlineData("campaign.get")]
    [InlineData("list_membership.add")]
    [InlineData("campaign.enroll")]
    public void Every_code_the_operation_declares_has_a_row_that_says_when_it_is_reported(string operation)
    {
        var contract = OperationCatalog.Find(operation);
        Assert.NotNull(contract);
        var named = ReplyErrorTable.Rows(operation).Select(row => Text(row.Row, "code")).ToHashSet(StringComparer.Ordinal);

        // The other direction of the same rule: a document declares what a caller may have to handle, so a code
        // with no row is a situation this package would meet and have no word for.
        foreach (var declared in contract.FailureCodes)
        {
            Assert.Contains(declared.Code, named);
        }
    }

    [Fact]
    public void Every_row_says_when_it_applies_what_it_reports_and_what_an_operator_should_do()
    {
        var rows = ReplyErrorTable.AllRows();
        Assert.NotEmpty(rows);

        foreach (var (where, key, row) in rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(Text(row, "when")), $"{where} row '{key}' does not say when it applies.");
            Assert.False(string.IsNullOrWhiteSpace(Text(row, "note")), $"{where} row '{key}' says nothing an operator could act on.");
            Assert.Matches("^[a-z][a-z0-9_]*$", Text(row, "code"));
            Assert.Contains(Text(row, "class"), Classes);
        }
    }

    [Fact]
    public void Every_call_the_package_makes_is_marked_a_read_or_a_write()
    {
        var calls = ReplyErrorTable.Calls;
        Assert.NotEmpty(calls);

        foreach (var (call, marked) in calls)
        {
            var kind = Text(marked!.AsObject(), "kind");
            Assert.True(kind is "read" or "write", $"'{call}' is marked '{kind}', which decides nothing.");
        }

        // The mark is per call and not per verb, which is the whole reason it is written down: an import is a
        // POST that creates people, and a search would be a POST that changes nothing.
        Assert.Equal("write", Text(calls["POST /v3/contacts/import"]!.AsObject(), "kind"));
        Assert.Equal("read", Text(calls["GET /v3/sequences/{id}"]!.AsObject(), "kind"));
    }

    [Fact]
    public void A_lost_answer_is_ambiguous_on_a_write_and_only_transient_on_a_read()
    {
        var shared = ReplyErrorTable.Shared;

        // The one decision the read/write mark exists for. A write whose answer never came back may already have
        // created a person or sent them an email, and with consumption pricing a repeat is a real charge; a read
        // that was lost cost nothing and can simply be asked again.
        var write = shared["answer_lost_on_a_write"]!.AsObject();
        Assert.Equal("provider_answer_lost", Text(write, "code"));
        Assert.Equal("ambiguous", Text(write, "class"));

        var read = shared["answer_lost_on_a_read"]!.AsObject();
        Assert.Equal("provider_unavailable", Text(read, "code"));
        Assert.Equal("transient", Text(read, "class"));
    }

    [Fact]
    public void A_code_one_operation_declares_and_another_does_not_is_never_shared()
    {
        var shared = ReplyErrorTable.Shared.Select(row => Text(row.Value!.AsObject(), "code")).ToList();

        // The split between shared rows and per-operation rows is not tidiness. The three documents do not
        // declare the same codes: only the two writes take a channel value, and only two of them read a
        // campaign, so a single shared row naming either code would name a code the runtime refuses.
        Assert.DoesNotContain("invalid_channel_value", shared);
        Assert.DoesNotContain("campaign_not_found", shared);
        Assert.DoesNotContain("suppressed", shared);
    }

    [Fact]
    public void The_two_writes_map_limit_reached_and_name_the_cap_they_could_not_map()
    {
        foreach (var operation in new[] { "list_membership.add", "campaign.enroll" })
        {
            var row = Assert.Single(ReplyErrorTable.Rows(operation), entry => Text(entry.Row, "code") == "limit_reached");

            // Silence would have been the easy way out: the check that every named code is declared says nothing
            // about a code nobody named. So the row maps the cap Reply publishes a code for, and says plainly
            // that the other one has none rather than guessing a refusal into this row.
            Assert.Contains("publishes no code of its own", Text(row.Row, "note"), StringComparison.Ordinal);
        }

        // And `campaign.get` never names it, because reading a campaign reaches no cap and its document does not
        // declare the code at all.
        Assert.DoesNotContain(ReplyErrorTable.Rows("campaign.get"), entry => Text(entry.Row, "code") == "limit_reached");
    }

    private static string Text(JsonObject row, string member) => row[member]?.GetValue<string>() ?? string.Empty;
}

/// <summary>
/// The package's own table, read out of the package's own file. <c>CALLS</c> and <c>ROWS</c> are written as
/// strict JSON object literals precisely so this reader can exist: one source for the table, no JSON twin beside
/// it to fall out of step, and a test that fails the moment the package names something the contracts do not.
/// </summary>
internal static class ReplyErrorTable
{
    public static JsonObject Calls => Table("CALLS");

    /// <summary>The rows every one of the three operations shares.</summary>
    public static JsonObject Shared => Table("ROWS")["shared"]!.AsObject();

    /// <summary>The rows one operation declares and the others do not.</summary>
    public static JsonObject Own(string operation) => Table("ROWS")["per_operation"]![operation]!.AsObject();

    /// <summary>Every row that applies to one operation: the shared ones, and then its own.</summary>
    public static IReadOnlyList<(string Key, JsonObject Row)> Rows(string operation) =>
    [
        .. Shared.Select(row => (row.Key, row.Value!.AsObject())),
        .. Own(operation).Select(row => (row.Key, row.Value!.AsObject())),
    ];

    /// <summary>Every row in the table, each said to be shared or to belong to one operation.</summary>
    public static IReadOnlyList<(string Where, string Key, JsonObject Row)> AllRows()
    {
        var rows = new List<(string, string, JsonObject)>();
        foreach (var row in Shared)
        {
            rows.Add(("shared", row.Key, row.Value!.AsObject()));
        }

        foreach (var operation in Table("ROWS")["per_operation"]!.AsObject())
        {
            foreach (var row in operation.Value!.AsObject())
            {
                rows.Add((operation.Key, row.Key, row.Value!.AsObject()));
            }
        }

        return rows;
    }

    /// <summary>
    /// One table, parsed out of the module: everything between the assignment and the closing brace that starts
    /// a line of its own. That shape is a rule the file states about itself, so the reader can be this small.
    /// </summary>
    private static JsonObject Table(string name)
    {
        var module = ReplyPlugins.ErrorTableModule;
        Assert.True(File.Exists(module), $"the package's error table is not beside the tests at '{module}'.");
        var text = File.ReadAllText(module);

        var marker = $"export const {name} = ";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{module}' declares no {name}.");

        var open = start + marker.Length;
        var end = text.IndexOf("\n};", open, StringComparison.Ordinal);
        Assert.True(end > open, $"{name} in '{module}' does not end with a closing brace on a line of its own.");

        var parsed = JsonNode.Parse(text[open..(end + 2)]);
        Assert.NotNull(parsed);
        return parsed.AsObject();
    }
}
