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
    private static readonly string[] Classes = ["transient", "permanent", "validation", "ambiguous", "blocked"];

    /// <summary>
    /// Every operation the package's manifest says it performs, read from the manifest itself: a list written here was
    /// seven operations long while the package performed eleven, and the four writes it left out were held to nothing.
    /// </summary>
    public static TheoryData<string> Performed() => [.. ReplyErrorTable.Performed()];

    // Critical: the Reply error table and the contracts agree.
    public static TheoryData<string> CriticalPerformed() =>
        CriticalRows.Mark(Performed(), Family.R1, data => data is ["campaign.get"]);

    [Fact]
    public void The_table_is_held_for_every_operation_the_package_performs()
    {
        Assert.Equal(
            OperationCatalog.All.Select(contract => contract.Id).Order(StringComparer.Ordinal),
            ReplyErrorTable.Performed().Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CriticalPerformed))]
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
    [MemberData(nameof(Performed))]
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

    /// <summary>
    /// A row's note is the failure's message, and the message is all of a failure that reaches the work item's last
    /// error: the details are stripped from it and flattened into the attempt's trace. So a note that sent its reader to
    /// "the details" would send a person reading <c>jason workitem get</c> somewhere they cannot see. Where a note needs
    /// what only the details hold, it says the attempt's trace.
    /// </summary>
    [Fact]
    public void No_message_sends_its_reader_to_details_a_work_item_never_shows()
    {
        foreach (var (where, key, row) in ReplyErrorTable.AllRows())
        {
            Assert.False(
                Text(row, "note").Contains("details", StringComparison.OrdinalIgnoreCase),
                $"{where} row '{key}' sends its reader to the details, which never reach a work item's last error.");
        }
    }

    /// <summary>
    /// The README's failure tables are the same notes written for a person, and a remedy that sent its reader to "the
    /// details" would send them where <c>jason workitem get</c> never shows. The README says "the trace" for what the
    /// details carry, so every "What to do about it" cell is held to that the way the table's notes are.
    /// </summary>
    [Fact]
    public void No_remedy_in_the_readme_sends_its_reader_to_details_a_work_item_never_shows()
    {
        const string header = "| When | Code | Class | What to do about it |";
        var lines = File.ReadAllLines(Path.Combine(ReplyPlugins.PackageSource, "README.md"));
        var remedies = new List<(int Line, string Cell)>();

        for (var at = 0; at < lines.Length; at++)
        {
            if (lines[at] != header)
            {
                continue;
            }

            for (at += 2; at < lines.Length && lines[at].StartsWith('|'); at++)
            {
                var cells = lines[at].Split('|');
                Assert.True(cells.Length == 6, $"README line {at + 1} is not a row of four cells.");
                remedies.Add((at + 1, cells[4]));
            }
        }

        // The reader has to reach the column it holds, or it would pass by holding nothing.
        Assert.Contains(remedies, remedy => remedy.Cell.Contains("the trace", StringComparison.OrdinalIgnoreCase));

        foreach (var (line, cell) in remedies)
        {
            Assert.False(
                cell.Contains("details", StringComparison.OrdinalIgnoreCase),
                $"README line {line} sends its reader to the details, which never reach a work item's last error.");
        }
    }

    [Fact]
    [Critical(Family.W5, "read vs write after a lost answer")]
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

        // The split between shared rows and per-operation rows is not tidiness. The documents do not declare
        // the same codes: only the writes about a person take a channel value, only two of them find a person suppressed, and the list add
        // and the two conversation reads declare no missing campaign, so a single shared row naming any of these
        // codes would name a code the runtime refuses.
        Assert.DoesNotContain("invalid_channel_value", shared);
        Assert.DoesNotContain("campaign_not_found", shared);
        Assert.DoesNotContain("suppressed", shared);
    }

    /// <summary>
    /// The one permanent ending a read has for an answer too large to read whole belongs to the reads alone: each
    /// declares it, each names the setting that bounds what a plugin may read from a program, and no shared row
    /// carries it — the writes do not declare the code, and a cut answer to a write may still have written.
    /// </summary>
    [Fact]
    public void Every_read_says_its_answer_was_too_large_and_names_the_setting_that_bounds_it()
    {
        var reads = ReplyErrorTable.Performed()
            .Select(OperationCatalog.Find)
            .Where(contract => contract!.Reach.Value == "read")
            .Select(contract => contract!.Id)
            .ToList();
        Assert.Contains("campaign.get", reads);

        foreach (var read in reads)
        {
            var row = Assert.Single(ReplyErrorTable.Rows(read), entry => entry.Key == "answer_too_large_on_a_read").Row;
            Assert.Equal("provider_answer_too_large", Text(row, "code"));
            Assert.Equal("permanent", Text(row, "class"));
            Assert.Contains("Plugins:Exec:OutputBytes", Text(row, "note"), StringComparison.Ordinal);
        }

        Assert.DoesNotContain("provider_answer_too_large", ReplyErrorTable.Shared.Select(row => Text(row.Value!.AsObject(), "code")));
    }

    /// <summary>
    /// The remedy an operator reads when a profile is not signed in has to be one they can type. Windows PowerShell's
    /// default execution policy refuses the script npm installs beside the command, so there the command is
    /// <c>reply.cmd</c>; PowerShell 7 and Command Prompt take the name as written.
    /// </summary>
    [Fact]
    public void The_sign_in_remedy_says_what_windows_powershell_takes()
    {
        var note = Text(ReplyErrorTable.Shared["unauthorized"]!.AsObject(), "note");

        Assert.Contains("`reply.cmd auth login`", note, StringComparison.Ordinal);
        Assert.Contains("PowerShell 7 and Command Prompt take `reply auth login` as written", note, StringComparison.Ordinal);
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

    /// <summary>
    /// An input without the person's own fields comes only from a runtime older than 0.14.0, whose documents projected
    /// the identifier and the channel alone, so it is refused in a code those documents already declare rather than in one
    /// of its own: a runtime that knew a new code would never compose such an input, and one that does not know it would
    /// refuse it as a protocol failure. The row is `invalid_channel_value`, and it says that it is the older runtime's case.
    /// </summary>
    [Fact]
    public void An_input_without_the_person_s_fields_is_the_older_runtime_s_case_of_invalid_channel_value()
    {
        foreach (var operation in new[] { "message.send", "social_invitation.send", "contact.update" })
        {
            var row = Assert.Single(ReplyErrorTable.Rows(operation), entry => entry.Key == "person_not_projected").Row;

            Assert.True(
                (Text(row, "code"), Text(row, "class")) == ("invalid_channel_value", "validation"),
                $"{operation} reports an input without the person's fields as {Text(row, "code")} ({Text(row, "class")}).");
            Assert.Contains("older than 0.14.0", Text(row, "when"), StringComparison.Ordinal);
            Assert.Contains("Update Jason to 0.14.0 or newer", Text(row, "note"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_command_the_package_runs_is_marked_a_read_or_a_write()
    {
        var commands = ReplyErrorTable.Commands;
        Assert.NotEmpty(commands);

        foreach (var (command, marked) in commands)
        {
            var kind = Text(marked!.AsObject(), "kind");
            Assert.True(kind is "read" or "write", $"'{command}' is marked '{kind}', which decides nothing.");
        }

        // The mark is per command: the exact lookup only reads, and of the two commands on sequences by name the list
        // reads and the create writes. The read of one contact by its id reads, and its delete writes: a lost delete
        // may have happened.
        Assert.Equal("write", Text(commands["sequence add-contact"]!.AsObject(), "kind"));
        Assert.Equal("read", Text(commands["contact find"]!.AsObject(), "kind"));
        Assert.Equal("read", Text(commands["contact get"]!.AsObject(), "kind"));
        Assert.Equal("write", Text(commands["contact delete"]!.AsObject(), "kind"));
        Assert.Equal("read", Text(commands["sequence list"]!.AsObject(), "kind"));
        Assert.Equal("write", Text(commands["sequence create"]!.AsObject(), "kind"));
    }

    /// <summary>
    /// The package runs the command made for each request and never the CLI's raw call, which none of the CLI's own
    /// guards — the read before a write, the retries, the one answer for an outcome it could not see — would reach. So
    /// the table that marks every command it runs holds no raw call at all.
    /// </summary>
    [Fact]
    public void No_command_the_package_runs_is_the_raw_call()
    {
        var raw = ReplyErrorTable.Commands
            .Select(command => command.Key)
            .Where(command => command == "api" || command.StartsWith("api ", StringComparison.Ordinal))
            .ToList();

        Assert.True(raw.Count == 0, $"The package runs the raw call: {string.Join(", ", raw)}.");
    }

    /// <summary>
    /// A CLI code mapped to a row the operation does not have would end in the package's own defect message rather
    /// than in the canonical code the operation declares, so every mapping is held to the rows it names.
    /// </summary>
    [Fact]
    public void Every_code_the_table_maps_lands_on_a_row_that_exists()
    {
        foreach (var (code, row) in ReplyErrorTable.Codes["shared"]!.AsObject())
        {
            Assert.True(
                ReplyErrorTable.Shared.ContainsKey(row!.GetValue<string>()),
                $"shared code '{code}' maps to '{row}', which is no shared row.");
        }

        foreach (var (operation, mapping) in ReplyErrorTable.Codes["per_operation"]!.AsObject())
        {
            Assert.Contains(operation, ReplyErrorTable.Performed());
            var rows = ReplyErrorTable.Rows(operation).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var (code, row) in mapping!.AsObject())
            {
                Assert.True(rows.Contains(row!.GetValue<string>()), $"{operation} maps '{code}' to '{row}', which it has no row for.");
            }
        }
    }

    /// <summary>
    /// Every operation declares the provider block, so the table has its row; but reply-cli publishes no code that says
    /// Reply refused a call for money, an allowance, a key or a channel and did nothing for it, so no code maps to the row
    /// and nothing reaches it. Mapping one of today's codes there would assert that Reply did nothing where the CLI never
    /// said so. The README names the row as the place the switch lands.
    /// </summary>
    [Fact]
    public void The_block_has_a_row_that_nothing_reaches_until_the_cli_says_so()
    {
        var row = ReplyErrorTable.Shared["account_or_sender_blocked"]!.AsObject();
        Assert.Equal(("provider_blocked", "blocked"), (Text(row, "code"), Text(row, "class")));

        var mapped = ReplyErrorTable.Codes["shared"]!.AsObject().Select(entry => entry.Value!.GetValue<string>())
            .Concat(ReplyErrorTable.Codes["per_operation"]!.AsObject().SelectMany(operation => operation.Value!.AsObject().Select(entry => entry.Value!.GetValue<string>())));
        Assert.DoesNotContain("account_or_sender_blocked", mapped);

        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        Assert.Contains("| `provider_blocked` | blocked |", readme, StringComparison.Ordinal);
        Assert.Contains("`account_or_sender_blocked`", readme, StringComparison.Ordinal);
    }

    private static string Text(JsonObject row, string member) => row[member]?.GetValue<string>() ?? string.Empty;
}

/// <summary>
/// The package's own table, read out of the package's own file. <c>COMMANDS</c>, <c>CODES</c> and <c>ROWS</c> are written as
/// strict JSON object literals precisely so this reader can exist: one source for the table, no JSON twin beside
/// it to fall out of step, and a test that fails the moment the package names something the contracts do not.
/// </summary>
internal static class ReplyErrorTable
{
    /// <summary>Every command the package runs, and whether it is a read or a write.</summary>
    public static JsonObject Commands => Table("COMMANDS");

    /// <summary>Which row each of reply-cli's error codes is: shared, and per operation.</summary>
    public static JsonObject Codes => Table("CODES");

    /// <summary>The operations the package's manifest says it performs, read from its one <c>operations:</c> line.</summary>
    public static IReadOnlyList<string> Performed()
    {
        var manifest = File.ReadAllLines(Path.Combine(ReplyPlugins.PackageSource, "plugin.yaml"));
        var line = Assert.Single(manifest, entry => entry.StartsWith("operations: [", StringComparison.Ordinal));
        return [.. line["operations: [".Length..].TrimEnd(']').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    }

    /// <summary>The rows every operation the package performs shares.</summary>
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
