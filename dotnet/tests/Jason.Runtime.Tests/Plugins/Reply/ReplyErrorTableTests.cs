using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Events;
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
public partial class ReplyErrorTableTests
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
            ReplyPlugins.PublishedItPerforms.Order(StringComparer.Ordinal),
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
            Assert.True(
                Enum.TryParse<FailureClass>(Text(row, "class"), ignoreCase: true, out var mapped),
                $"row '{key}' of {operation} reports class '{Text(row, "class")}', which is not a failure class.");

            // A code reserved beside every operation is declared by none, and passes under the one class it is reserved with.
            if (UnjudgedOutcomeCodes.Reserved.TryGetValue(code, out var reserved))
            {
                Assert.True(UnjudgedOutcomeCodes.Reserves(mapped, code), $"row '{key}' of {operation} reports {code} as {mapped}, and it is reserved as {reserved}.");
                continue;
            }

            var declared = Assert.Single(contract.FailureCodes, failure => failure.Code == code);
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

    /// <summary>
    /// "Every 4xx is final" read literally would end the two answers in that range that sent nothing and are repeated
    /// safely. The README says which answers of Reply's at 400 to 499 are final and why, names both exceptions with the
    /// reason each is safe, and the sends' own rows for both stay transient.
    /// </summary>
    [Fact]
    public void The_readme_names_the_two_answers_at_400_to_499_that_are_repeated_and_why()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings(" ");
        var lines = File.ReadAllLines(Path.Combine(ReplyPlugins.PackageSource, "README.md"));

        Assert.Contains("about the send it took or refused is final, since Reply's guard released the key", readme, StringComparison.Ordinal);
        Assert.Contains("`idempotency_key.in_use` (409) is the one where the key is still held", readme, StringComparison.Ordinal);
        Assert.Contains("A 429 (`rate_limited`) is refused before the guard runs, so nothing was taken and no key was touched", readme, StringComparison.Ordinal);
        var held = lines.Where(line => line.StartsWith('|') && line.Contains("(`idempotency_key.in_use`)", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, held.Count);
        Assert.All(held, line => Assert.Equal(" transient ", line.Split('|')[3]));
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
    /// refuse it as a protocol failure. The row is `invalid_channel_value`, and it says that it is the older runtime's case;
    /// its repair names 0.22.0, the first runtime that reads this package's manifest at all.
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
            Assert.Contains("Update Jason to 0.22.0 or newer", Text(row, "note"), StringComparison.Ordinal);
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
            Assert.Contains(operation, ReplyErrorTable.Performed().Concat(ReplyErrorTable.Answered()));
            var rows = ReplyErrorTable.Rows(operation).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var (code, row) in mapping!.AsObject())
            {
                Assert.True(rows.Contains(row!.GetValue<string>()), $"{operation} maps '{code}' to '{row}', which it has no row for.");
            }
        }
    }

    /// <summary>
    /// The runtime's own reader asks the package for the account's events under the events contract, which publishes four
    /// codes of its own and none of the operations'. So every row an events read can end in is the read's own, naming a
    /// code that contract declares under its class, and every code it declares has a row. The shared rows a read never
    /// reaches — a write's lost answer, a sequence still activating, a block — are the only ones it leaves to the shared
    /// table, and those would end in a code the reader refuses as an answer outside the contract.
    /// </summary>
    [Fact]
    public void Every_row_an_events_read_can_end_in_names_a_code_the_events_contract_declares()
    {
        Assert.Equal(["events.read"], ReplyErrorTable.Answered());
        var contract = EventsCatalog.Find("events.read")!;
        var own = ReplyErrorTable.Own("events.read");

        foreach (var (key, row) in own)
        {
            var declared = Assert.Single(contract.FailureCodes, failure => failure.Code == Text(row!.AsObject(), "code"));
            Assert.Equal(declared.Class.ToString(), Text(row!.AsObject(), "class"), ignoreCase: true);
            Assert.False(string.IsNullOrWhiteSpace(key));
        }

        var unreached = ReplyErrorTable.Shared.Select(row => row.Key).Where(key => !own.ContainsKey(key)).Order(StringComparer.Ordinal);
        Assert.Equal(["account_or_sender_blocked", "answer_lost_on_a_write", "reply_busy"], unreached);
        Assert.All(contract.FailureCodes, failure => Assert.Contains(failure.Code, own.Select(row => Text(row.Value!.AsObject(), "code"))));
        Assert.Equal("read", Text(ReplyErrorTable.Commands["inbox changes"]!.AsObject(), "kind"));
    }

    /// <summary>
    /// A row of class <c>blocked</c> is the only kind that says what is blocked, and says it in the block contract's own
    /// words: the scope and the reason travel from the row, so a row that named a scope or a reason the contract does not
    /// publish would make every failure through it one the runtime refuses. The shared row carries none: every operation
    /// declares the block, so the table has the row, but the only refusals reply-cli has a code for that say Reply did
    /// nothing for a reason outside the work are a sending account's, and those land on the sends' own rows. Nothing maps
    /// to the shared row, and the README names it as the place a code for the account's money, plan or key lands once
    /// the CLI publishes one.
    /// </summary>
    [Fact]
    public void Every_row_of_class_blocked_says_what_is_blocked_in_the_contract_s_words()
    {
        var properties = ProviderBlockContract.Published.Schema["properties"]!;
        var scopes = properties["scope"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var reasons = properties["reason"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var blocked = 0;

        foreach (var (where, key, row) in ReplyErrorTable.AllRows())
        {
            if (Text(row, "class") != "blocked" || where == "shared")
            {
                Assert.True(row["block"] is null, $"{where} row '{key}' of class '{Text(row, "class")}' carries a block: {row["block"]?.ToJsonString()}.");
                continue;
            }

            blocked++;
            var block = Assert.IsType<JsonObject>(row["block"]);
            Assert.True(
                block.Select(member => member.Key).Order(StringComparer.Ordinal).SequenceEqual(["reason", "scope"]),
                $"{where} row '{key}' says {block.ToJsonString()}; a row says the scope and the reason, and the rest comes from the answer.");
            Assert.True(scopes.Contains(Text(block, "scope")), $"{where} row '{key}' names the scope '{Text(block, "scope")}', which the contract does not publish.");
            Assert.True(reasons.Contains(Text(block, "reason")), $"{where} row '{key}' names the reason '{Text(block, "reason")}', which the contract does not publish.");

            // A command that ran from no sending account has no sender to name, so a sender's block names the permanent
            // row such a command ends in instead.
            if (Text(block, "scope") == "sender")
            {
                var unnamed = row["unnamed_sender"]?.GetValue<string>();
                Assert.True(
                    unnamed is not null && ReplyErrorTable.Own(where)[unnamed] is JsonObject fallback && Text(fallback, "class") == "permanent",
                    $"{where} row '{key}' names no permanent row for a command that ran from no sending account: '{unnamed}'.");
            }
        }

        Assert.True(blocked == 4, $"the table has {blocked} rows that say a block; the two sends have two each.");

        var shared = ReplyErrorTable.Shared["account_or_sender_blocked"]!.AsObject();
        Assert.Equal(("provider_blocked", "blocked"), (Text(shared, "code"), Text(shared, "class")));

        var mapped = ReplyErrorTable.Codes["shared"]!.AsObject().Select(entry => entry.Value!.GetValue<string>())
            .Concat(ReplyErrorTable.Codes["per_operation"]!.AsObject().SelectMany(operation => operation.Value!.AsObject().Select(entry => entry.Value!.GetValue<string>())));
        Assert.DoesNotContain("account_or_sender_blocked", mapped);

        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        Assert.Contains("| `provider_blocked` | blocked |", readme, StringComparison.Ordinal);
        Assert.Contains("`account_or_sender_blocked`", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two sends report a sending account Reply refused for its own state as that sender's block: turned off or
    /// disconnected is its channel, a spent daily or weekly allowance its limit. What a format needs and the account
    /// lacks — Sales Navigator for an InMail, a voice profile for a voice message — is about the format, since the same
    /// account still sends a plain message, and InMail credits keep their own word; neither holds anything.
    /// </summary>
    [Theory]
    [InlineData("social_invitation.send")]
    [InlineData("message.send")]
    public void The_sends_say_the_sender_s_block_for_the_codes_reply_cli_has_for_one(string operation)
    {
        var codes = ReplyErrorTable.Codes["per_operation"]![operation]!.AsObject();
        var rows = ReplyErrorTable.Own(operation);

        Assert.Equal(
            ["sender_blocked_channel", "sender_blocked_channel", "sender_blocked_limit"],
            new[] { "linkedin.account_inactive", "linkedin.invalid_cookie", "linkedin.limit_reached" }.Select(code => (string?)codes[code]));
        Assert.Equal("""{"scope":"sender","reason":"channel"}""", rows["sender_blocked_channel"]!["block"]!.ToJsonString());
        Assert.Equal("""{"scope":"sender","reason":"limit"}""", rows["sender_blocked_limit"]!["block"]!.ToJsonString());

        if (operation == "message.send")
        {
            Assert.Equal(
                ["sender_unavailable", "sender_unavailable", "sender_unavailable", "no_inmail_credits"],
                new[] { "linkedin.sales_navigator_required", "linkedin.sales_navigator_disconnected", "linkedin.voice_profile_missing", "linkedin.no_inmail_credits" }
                    .Select(code => (string?)codes[code]));
        }

        // The account Reply's own list shows turned off or disconnected is refused by name before the send, from send.js.
        var send = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "modules", "send.js"));
        Assert.Contains("row: \"sender_blocked_channel\"", send, StringComparison.Ordinal);
        Assert.DoesNotContain("row: \"sender_unavailable\"", send, StringComparison.Ordinal);
    }

    /// <summary>
    /// The README's table for each send says the same as the error table about the block: every reply-cli code the table
    /// maps to a row of class <c>blocked</c> is named in a README row whose code is <c>provider_blocked</c>, and in no
    /// other row of that table, so a person reading why their invitation is held reads what the package does.
    /// </summary>
    [Theory]
    [InlineData("social_invitation.send")]
    [InlineData("message.send")]
    public void The_readme_says_the_sender_s_block_where_the_table_does(string operation)
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var heading = $"\n### `{operation}`\n";
        var start = readme.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The README has no section for {operation}.");
        var end = readme.IndexOf("\n#", start + heading.Length, StringComparison.Ordinal);
        var rows = readme[start..(end < 0 ? readme.Length : end)].Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && !line.StartsWith("| When ", StringComparison.Ordinal))
            .Select(line => line.Split('|'))
            .ToList();
        Assert.NotEmpty(rows);

        var own = ReplyErrorTable.Own(operation);
        var blocking = ReplyErrorTable.Codes["per_operation"]![operation]!.AsObject()
            .Where(entry => own[entry.Value!.GetValue<string>()] is JsonObject row && Text(row, "class") == "blocked")
            .Select(entry => entry.Key)
            .ToList();
        Assert.NotEmpty(blocking);

        foreach (var code in blocking)
        {
            var naming = rows.Where(columns => columns[1].Contains($"`{code}`", StringComparison.Ordinal)).ToList();
            Assert.True(naming.Count > 0, $"The README's {operation} table names '{code}' nowhere.");
            Assert.All(
                naming,
                columns => Assert.True(
                    columns[2].Trim() == "`provider_blocked`" && columns[3].Trim() == "blocked",
                    $"The README's {operation} table names '{code}' under {columns[2].Trim()} {columns[3].Trim()}; the error table makes it the sender's block."));
        }
    }

    /// <summary>
    /// A 404 with no code of Reply's own is told where the table maps it: in the README's table of endings every operation
    /// can meet, under the code and the class the row gives it, on every command but those that cannot pass that 404
    /// through, since they read a 404 as a thing not found, and with what a person does about it: Reply's API changed
    /// under this package version, so update the package and, if the ending stays, report it.
    /// </summary>
    [Theory]
    [InlineData("Endings every operation can meet", "status 404 and no code of Reply's own")]
    [InlineData("Endings every operation can meet", "which reply-cli passes through as it is on every command but")]
    public void The_readme_says_what_a_404_without_reply_s_code_ends_in(string section, string when)
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var heading = $"\n### {section}\n";
        var start = readme.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The README has no section '{section}'.");
        var end = readme.IndexOf("\n#", start + heading.Length, StringComparison.Ordinal);
        var row = Assert.Single(
            readme[start..(end < 0 ? readme.Length : end)].Split('\n'),
            line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains(when, StringComparison.Ordinal));
        var columns = row.Split('|');

        var shared = ReplyErrorTable.Shared["refusal_this_version_has_no_word_for"]!.AsObject();
        Assert.Equal(($"`{Text(shared, "code")}`", Text(shared, "class")), (columns[2].Trim(), columns[3].Trim()));
        Assert.Equal(("`provider_call_failed`", "permanent"), (columns[2].Trim(), columns[3].Trim()));
        Assert.Contains("Reply's API changed under this package version", columns[1], StringComparison.Ordinal);
        Assert.Contains("`jason update apply`", columns[4], StringComparison.Ordinal);
        Assert.Contains("report it", columns[4], StringComparison.Ordinal);
    }

    /// <summary>
    /// The commands a 404 with no code of Reply's own is told against are written twice: in the README's row of endings
    /// every operation can meet, and in the "when" of the row it lands on. Both name the same commands, in the package's
    /// own spelling. 0.14.1's row named three of the README's eight, and the row above, which reads the README alone,
    /// could not see it.
    /// </summary>
    [Fact]
    public void The_row_a_404_without_reply_s_code_lands_on_names_the_commands_the_readme_names()
    {
        var commands = ReplyErrorTable.Commands.Select(command => command.Key).ToHashSet(StringComparer.Ordinal);

        var told = CommandsNamed(BareNotFoundInTheReadme(), commands);
        Assert.NotEmpty(told);
        Assert.Equal(told, CommandsNamed(BareNotFoundInTheRow(), commands));
    }

    /// <summary>
    /// Both texts say which commands a 404 with no code of Reply's own cannot come from, and that every other command
    /// can meet one, rather than listing the commands it can: a list of those left out <c>inbox list</c> and
    /// <c>inbox changes</c> in both, which the row above, comparing the texts only with each other, could not see. The
    /// commands left out are held here from reply-cli 0.18.0's source: those whose every call reads a 404 as the thing
    /// asked for not being found (<c>contact-record.ts</c> lines 44 and 221, <c>inbox.ts</c> 148 and 158,
    /// <c>inbox-reply.ts</c> 76 and 141, <c>sequence-read.ts</c> 28, 44, 59, 89 and 133, <c>sequence-state.ts</c> 11), and
    /// <c>auth whoami</c>, which answers through the CLI's own client and never as <c>reply.refused</c>.
    /// </summary>
    [Fact]
    public void A_404_without_reply_s_code_is_written_for_every_command_but_those_that_read_a_404_as_missing()
    {
        var commands = ReplyErrorTable.Commands.Select(command => command.Key).ToHashSet(StringComparer.Ordinal);
        string[] never =
        [
            .. new[]
            {
                "auth whoami", "contact delete", "contact get", "inbox get", "inbox reply",
                "sequence contacts", "sequence get", "sequence pause", "sequence start", "sequence stats",
            }.Order(StringComparer.Ordinal),
        ];
        Assert.All(never, command => Assert.Contains(command, commands));

        foreach (var text in new[] { BareNotFoundInTheReadme(), BareNotFoundInTheRow() })
        {
            var start = text.IndexOf("on every command but ", StringComparison.Ordinal);
            Assert.True(start >= 0, text);
            Assert.DoesNotContain("only on", text, StringComparison.Ordinal);
            Assert.Equal(never, CommandsNamed(text[start..], commands));
        }
    }

    /// <summary>
    /// A LinkedIn send over the account's allowance is told from a busy Reply by Reply's code, so both sends' tables name
    /// both of Reply's codes beside reply-cli's word for them, and the shape that names the send in the state
    /// <c>failed</c>: in the README, which also says a 429 under no such code is <c>rate_limited</c>, and in the "when" of
    /// the row the code lands on.
    /// </summary>
    [Theory]
    [InlineData("social_invitation.send")]
    [InlineData("message.send")]
    public void Both_sends_name_reply_s_codes_for_an_allowance_spent(string operation)
    {
        string[] named = ["`linkedin.limit_reached`", "`directLinkedIn.dailyLimitReached`", "`directLinkedIn.weeklyLimitReached`", "the state `failed`"];
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf($"### `{operation}`", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The README has no section for {operation}.");
        var section = readme[start..readme.IndexOf("\n### ", start + 1, StringComparison.Ordinal)];
        var row = Assert.Single(section.Split('\n'), line => line.StartsWith("| The LinkedIn account's daily or weekly allowance is spent", StringComparison.Ordinal));
        var when = Text(ReplyErrorTable.Own(operation)["sender_blocked_limit"]!.AsObject(), "when");

        Assert.All(named, word => Assert.Contains(word, row, StringComparison.Ordinal));
        Assert.Contains("a 429 under no such code is `rate_limited`", row, StringComparison.Ordinal);
        Assert.All(named, word => Assert.Contains(word, when, StringComparison.Ordinal));
    }

    /// <summary>
    /// A code reply-cli gives for a refusal Reply makes as it times deferred LinkedIn sends itself is told with the status
    /// and the code Reply gives it: in the README's table for the operation, in one row under the code and the class the
    /// table's row gives it, and in the "when" of that row. A reader who meets either code in a trace finds it in both.
    /// </summary>
    [Theory]
    [InlineData("message.cancel", "direct_send.already_sending", "directSend.alreadySending", "409")]
    [InlineData("message.cancel", "direct_send.cancel_not_confirmed", "directSend.cancelNotConfirmed", "503")]
    public void A_refusal_reply_cli_names_for_reply_s_scheduler_is_told_with_its_status_where_it_lands(
        string operation, string cliCode, string replyCode, string status)
    {
        var key = Assert.Single(ReplyErrorTable.Codes["per_operation"]![operation]!.AsObject(), entry => entry.Key == cliCode);
        var row = ReplyErrorTable.Own(operation)[key.Value!.GetValue<string>()]!.AsObject();
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf($"\n### `{operation}`\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The README has no section for {operation}.");
        var end = readme.IndexOf("\n#", start + 1, StringComparison.Ordinal);
        var columns = Assert.Single(
            readme[start..(end < 0 ? readme.Length : end)].Split('\n'),
            line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains($"`{cliCode}`", StringComparison.Ordinal)).Split('|');

        Assert.Equal(($"`{Text(row, "code")}`", Text(row, "class")), (columns[2].Trim(), columns[3].Trim()));
        Assert.Contains($"`{cliCode}`, Reply's code `{replyCode}` at {status}", columns[1], StringComparison.Ordinal);
        Assert.Contains($"`{cliCode}`, Reply's code `{replyCode}` at {status}", Text(row, "when"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A schedule Reply cannot time a send by is one code of reply-cli's whichever route Reply refused it on, so it is one
    /// row of <c>message.send</c> for the email and the LinkedIn message alike: no key reads Reply's own code, and the
    /// README's row and the row's "when" name the CLI's code with both of Reply's and the status.
    /// </summary>
    [Theory]
    [InlineData("schedule.not_found", "scheduleNotFound", "404")]
    [InlineData("schedule.no_slot", "scheduleHasNoSlot", "409")]
    [InlineData("contact.time_zone_missing", "contactTimeZoneMissing", "409")]
    public void A_schedule_reply_cannot_time_a_send_by_is_one_row_for_the_email_and_the_linkedin_message(string cliCode, string replyCode, string status)
    {
        var keys = ReplyErrorTable.Codes["per_operation"]!["message.send"]!.AsObject();
        Assert.DoesNotContain(keys, entry => entry.Key.Contains(replyCode, StringComparison.Ordinal));
        var key = Assert.Single(keys, entry => entry.Key == cliCode);
        var row = ReplyErrorTable.Own("message.send")[key.Value!.GetValue<string>()]!.AsObject();
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("\n### `message.send`\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no section for message.send.");
        var end = readme.IndexOf("\n#", start + 1, StringComparison.Ordinal);
        var columns = Assert.Single(
            readme[start..(end < 0 ? readme.Length : end)].Split('\n'),
            line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains($"`{cliCode}`", StringComparison.Ordinal)).Split('|');
        var named = $"`{cliCode}`, Reply's code `contact.{replyCode}` on an email or `directLinkedIn.{replyCode}` on a LinkedIn message, at {status}";

        Assert.Equal(($"`{Text(row, "code")}`", Text(row, "class")), (columns[2].Trim(), columns[3].Trim()));
        Assert.Contains(named, columns[1], StringComparison.Ordinal);
        Assert.Contains(named, Text(row, "when"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A 404 with no code of Reply's own is read by the operation's own key for it before any other: a repeat of a send by
    /// the key the send's request names a repeat with, then the operation's own 404 key, then the shared one, and only
    /// then the CLI's code. The shared table holds no 404 key, so a 404 an operation has no key for is the CLI's
    /// <c>reply.refused</c>, <c>provider_call_failed</c>, permanent; a repeat of either send whose write meets one is
    /// <c>send_not_settled</c>, and the change feed's is <c>events_unsupported</c>. No 404 key lands on a capability not
    /// offered.
    /// </summary>
    [Fact]
    public void A_404_without_reply_s_code_is_read_by_the_operation_s_own_key_before_the_shared_one()
    {
        var module = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "modules", "errors.js")).ReplaceLineEndings("\n");
        var start = module.IndexOf("export function rowOf(", StringComparison.Ordinal);
        Assert.True(start >= 0, "errors.js has no rowOf.");
        var body = module[start..module.IndexOf("\n}\n", start, StringComparison.Ordinal)];
        int[] order =
        [
            body.IndexOf("has(own, \"repeat:\" + unknown)", StringComparison.Ordinal),
            body.IndexOf("has(own, unknown)", StringComparison.Ordinal),
            body.IndexOf("has(CODES.shared, unknown)", StringComparison.Ordinal),
            body.IndexOf("has(own, code)", StringComparison.Ordinal),
            body.IndexOf("has(CODES.shared, code)", StringComparison.Ordinal),
        ];
        Assert.All(order, at => Assert.True(at >= 0, body));
        Assert.Equal(order.Order(), order);

        Assert.DoesNotContain(ReplyErrorTable.Codes["shared"]!.AsObject(), entry => entry.Key.EndsWith(":404", StringComparison.Ordinal));
        Assert.Equal("refusal_this_version_has_no_word_for", (string?)ReplyErrorTable.Codes["shared"]!["reply.refused"]);
        Assert.Equal(("provider_call_failed", "permanent"), Ending("message.send", "refusal_this_version_has_no_word_for"));
        foreach (var send in new[] { "message.send", "social_invitation.send" })
        {
            var key = (string?)ReplyErrorTable.Codes["per_operation"]![send]!["repeat:reply.refused:404"];
            Assert.Equal("send_not_settled", key);
            Assert.Equal(("send_not_settled", "permanent"), Ending(send, key!));
            Assert.Null(ReplyErrorTable.Codes["per_operation"]![send]!["reply.refused:404"]);
        }

        var feed = (string?)ReplyErrorTable.Codes["per_operation"]!["events.read"]!["reply.refused:404"];
        Assert.NotNull(feed);
        Assert.Equal(("events_unsupported", "permanent"), Ending("events.read", feed));
        var keys = ReplyErrorTable.Codes["per_operation"]!.AsObject()
            .SelectMany(operation => operation.Value!.AsObject().Where(entry => entry.Key.EndsWith(":404", StringComparison.Ordinal)).Select(entry => (operation.Key, Row: (string)entry.Value!)));
        Assert.All(keys, key => Assert.NotEqual(UnjudgedOutcomeCodes.CapabilityNotOffered, Ending(key.Key, key.Row).Code));
    }

    /// <summary>The code and the class the row an operation reaches by that name ends in: its own row, else the shared one.</summary>
    private static (string Code, string Class) Ending(string operation, string name)
    {
        var row = (ReplyErrorTable.Own(operation)[name] ?? ReplyErrorTable.Shared[name])!.AsObject();
        return (Text(row, "code"), Text(row, "class"));
    }

    /// <summary>The "when" of the shared row a 404 with no code of Reply's own lands on, from where it names that 404.</summary>
    private static string BareNotFoundInTheRow()
    {
        var when = Text(ReplyErrorTable.Shared["refusal_this_version_has_no_word_for"]!.AsObject(), "when");
        var start = when.IndexOf("status 404 and no code of Reply's own", StringComparison.Ordinal);
        Assert.True(start >= 0, when);
        return when[start..];
    }

    /// <summary>The first column of the README's row for a 404 with no code of Reply's own.</summary>
    private static string BareNotFoundInTheReadme()
    {
        var readme = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")).ReplaceLineEndings("\n");
        var row = Assert.Single(
            readme.Split('\n'),
            line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains("which reply-cli passes through as it is on every command but", StringComparison.Ordinal));
        return row.Split('|')[1];
    }

    private static string[] CommandsNamed(string text, HashSet<string> commands) =>
        [.. Quoted().Matches(text).Select(match => match.Groups["text"].Value).Where(commands.Contains).Distinct().Order(StringComparer.Ordinal)];

    [GeneratedRegex("`(?<text>[^`]+)`")]
    private static partial Regex Quoted();

    /// <summary>
    /// The rows are not the only place the package fails. A row a module names and the table does not hold falls back to
    /// a failed call, and an operation the manifest does not list ends <c>unknown_operation</c>. The fallback is
    /// reachable from every operation, so every one declares it under the class it is written with; the other is
    /// reachable from none, since no work item can name an operation the manifest does not list. Every other failure the
    /// package writes goes through a row, so the rows and these two are everything it can report.
    /// </summary>
    [Fact]
    public void A_failure_written_outside_the_rows_is_one_every_operation_declares_or_one_none_can_reach()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(ReplyPlugins.PackageSource, "modules"), "*.js")
            .Append(Path.Combine(ReplyPlugins.PackageSource, "main.js"))
            .Select(File.ReadAllText)
            .ToList();

        var literal = sources.SelectMany(source => LiteralFailure().Matches(source))
            .Select(match => (Class: match.Groups["class"].Value, Code: match.Groups["code"].Value))
            .Order()
            .ToList();
        Assert.Equal([("permanent", "provider_call_failed"), ("validation", "unknown_operation")], literal);

        var written = sources.Sum(source => source.Split("host.fail(").Length - 1);
        Assert.True(written == literal.Count + 1, $"the package calls host.fail {written} times: the two above and the one through a row are all it may.");

        foreach (var operation in ReplyErrorTable.Performed())
        {
            var declared = OperationCatalog.Find(operation)!.FailureCodes.SingleOrDefault(failure => failure.Code == "provider_call_failed");
            Assert.True(declared?.Class == FailureClass.Permanent, $"{operation} does not declare provider_call_failed as permanent, and a row the package lacks ends that way.");
        }
    }

    [GeneratedRegex(@"host\.fail\(\{\s*class: ""(?<class>[a-z]+)"",\s*code: ""(?<code>[a-z_]+)""")]
    private static partial Regex LiteralFailure();

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

    /// <summary>
    /// The events contracts the package answers, from the manifest's <c>contracts</c>: <c>events.read</c> where it declares
    /// <c>events: [1]</c>, and none where it does not.
    /// </summary>
    public static IReadOnlyList<string> Answered()
    {
        var manifest = File.ReadAllLines(Path.Combine(ReplyPlugins.PackageSource, "plugin.yaml"));
        return manifest.Any(entry => entry == "  events: [1]") ? ["events.read"] : [];
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
