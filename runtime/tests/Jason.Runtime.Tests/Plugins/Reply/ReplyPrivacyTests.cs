using System.Text.Json.Nodes;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// What must never leave, proven by running the package's operations and then looking everywhere a value could
/// have gone: the outcomes the runtime kept, the argument vectors the stand-in recorded, the diagnostics the child
/// wrote, and the log files on disk. Two different things are being ruled out and they leak differently — the
/// account's credential, which is the CLI's alone and may reach nothing of ours, and a person's own data, which
/// legitimately reaches Reply but only ever inside a body on stdin.
/// </summary>
/// <remarks>
/// The class writes process-wide variables — the one the CLI finds its account through, and the vendor-named
/// ones an operator might have exported — so it belongs to the collection that never runs two such classes at
/// once. Every fixture starts through <see cref="ReplyPlugins.StartAsync"/>, which holds the resolved program
/// to the test tree before anything runs.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyPrivacyTests
{
    /// <summary>
    /// The credential the stand-in account holds. It is distinctive on purpose: a match somewhere it may not be
    /// could not then be a coincidence.
    /// </summary>
    private const string Credential = "reply-credential-marker-8c41f7";

    /// <summary>
    /// What an operator has in their own shell. No <c>REPLY_*</c> name enters the runtime, so neither of these
    /// may reach a child — and a value that did would be a key in a place nobody granted one.
    /// </summary>
    private const string ExportedKey = "reply-exported-key-marker-2ae9";

    private const string ExportedTeam = "reply-exported-team-marker-5d30";

    /// <summary>The one vendor-named variable a child may see: the package's own, and not a credential.</summary>
    private const string UpdateCheck = "REPLY_NO_UPDATE_CHECK";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_the_vendor_cli_holds_reaches_no_outcome_argument_diagnostic_or_log()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount()).WithMarker(Credential);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var results = await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        // The credential is held by the CLI and by nothing else. The whole reason for driving a vendor's own
        // tool is that its store is the only place one exists, so finding this value in anything of ours would
        // mean the arrangement bought nothing.
        // Each of the four searches below runs over a corpus that demonstrably holds what did travel, so a
        // value that was there would be found: the package holds no credential and no capability that could
        // reach one, which makes this a claim about the arrangement rather than about the package's care.
        Assert.Equal(ReplyOperations.EveryCall, Paths(account));
        Assert.Contains(ReplyOperations.Ensured, Answered(results[1]), StringComparison.Ordinal);

        foreach (var result in results)
        {
            Assert.DoesNotContain(Credential, Answered(result), StringComparison.Ordinal);
        }

        foreach (var call in account.Calls)
        {
            Assert.DoesNotContain(Credential, string.Join(' ', call.Args), StringComparison.Ordinal);
        }

        // Every diagnostic line every child wrote: the `exec` record of what was started with which arguments
        // is here, which is exactly why an argument may never carry one.
        var diagnostics = ReadAll(api.Paths.PluginWorkDirectory);
        Assert.Contains("\"exec\"", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(Credential, diagnostics, StringComparison.Ordinal);

        // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);

        // The invocations are accounted for — which package, which operation — and then say nothing about what
        // any of them carried.
        Assert.Contains(ReplyPlugins.PluginId, logs, StringComparison.Ordinal);
        Assert.Contains(ReplyOperations.Enroll, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Credential, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_contact_field_ever_appears_in_an_argument()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        // This is the rule that makes the whole privacy claim real, and it is about data that legitimately
        // travels rather than about a secret that never should. The `exec` diagnostic records every argument
        // and the runtime's logs may never hold contact data, so a person's own fields ride on stdin and
        // nowhere else — asserted over every call those operations make, not only the import.
        var personal = new[] { ReplyOperations.Address, ReplyOperations.FirstName, ReplyOperations.LastName, ReplyOperations.Company };
        Assert.Equal(ReplyOperations.EveryCall, Paths(account));
        foreach (var call in account.Calls)
        {
            var line = string.Join(' ', call.Args);
            foreach (var field in personal)
            {
                Assert.DoesNotContain(field, line, StringComparison.OrdinalIgnoreCase);
            }
        }

        // And the second half, which is what makes the first half mean something: the data really did reach
        // Reply, and it reached it the only way it may. The list add and the enrolment both carry the person, all four
        // fields, on stdin.
        var ensures = account.Calls.Where(call => call.Body is not null).ToList();
        Assert.Equal(2, ensures.Count);
        foreach (var ensure in ensures)
        {
            foreach (var field in personal)
            {
                Assert.Contains(field, ensure.Body!, StringComparison.Ordinal);
            }
        }

        // Nor may they turn up on disk afterwards, which is the same rule seen from the other end.
        Assert.DoesNotContain(ReplyOperations.Address, ReadAll(api.Paths.PluginWorkDirectory), StringComparison.OrdinalIgnoreCase);
        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);
        foreach (var field in personal)
        {
            Assert.DoesNotContain(field, logs, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task No_vendor_named_variable_of_the_operators_own_reaches_the_child()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);

        // Set on the runtime's own process, which is where an operator would have them: exported in the shell
        // that started it. The base environment is machine configuration rather than an integration point, so
        // no name of a vendor's is copied out of it — and an account is chosen by the profile store and the
        // route's binding instead.
        using var key = new ProcessVariable("REPLY_API_KEY", ExportedKey);
        using var team = new ProcessVariable("REPLY_TEAM_ID", ExportedTeam);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var results = await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        Assert.Equal(ReplyOperations.EveryCall, Paths(account));
        foreach (var call in account.Calls)
        {
            // Read from the child's own environment as it saw it: the only vendor-named variable there is the
            // one the package sets on the call, and it switches off an update check rather than carrying a
            // credential.
            Assert.Equal([UpdateCheck], call.Environment.Keys.Order());
            Assert.Equal("1", call.Environment[UpdateCheck]);
        }

        // And neither value reached anything else either: not an argument, not an outcome, not a log.
        foreach (var call in account.Calls)
        {
            Assert.DoesNotContain(ExportedKey, string.Join(' ', call.Args), StringComparison.Ordinal);
            Assert.DoesNotContain(ExportedTeam, string.Join(' ', call.Args), StringComparison.Ordinal);
        }

        foreach (var result in results)
        {
            Assert.DoesNotContain(ExportedKey, Answered(result), StringComparison.Ordinal);
        }

        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);
        Assert.DoesNotContain(ExportedKey, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(ExportedTeam, logs, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reads bring people's data back rather than sending it: names, a thread's subject and preview, a message's
    /// body and the addresses on it. The documents let a result carry them, and the result is where they stay — not
    /// in an argument, not in a request body (a read sends identifiers and a window, nothing else), not in a
    /// diagnostic the child wrote and not in a log. Each corpus is shown to hold what did travel, so a value that
    /// had gone there would be found.
    /// </summary>
    [Fact]
    public async Task What_the_reads_bring_back_reaches_no_argument_request_diagnostic_or_log()
    {
        const string Name = "Ottilie-marker-3f1a";
        const string Surname = "Varga-marker-6b02";
        const string Address = "ottilie.marker@example.com";
        const string Subject = "Subject-marker-77c2";
        const string Preview = "Preview-marker-90ab";
        const string Body = "Body-marker-e41d";
        string[] personal = [Name, Surname, Address, Subject, Preview, Body];

        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, Address, Name, lastName: Surname);
        account.WithEnrollment(ReplyOperations.Sequence, 1001);
        var thread = ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z", name: Name + " " + Surname, subject: Subject, preview: Preview);
        thread["contact"]!["email"] = Address;
        thread["messages"] = new JsonArray(new JsonObject
        {
            ["channel"] = "email",
            ["messageId"] = "<m501@mail.example.com>",
            ["date"] = "2026-10-03T07:41:09Z",
            ["body"] = Body,
            ["fromName"] = Name,
            ["isOutbound"] = false,
            ["subject"] = Subject,
            ["fromAddress"] = Address,
            ["to"] = new JsonArray("sdr@vendor.example"),
        });
        account.WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var campaign = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject { ["campaign"] = "7" },
        };
        var reads = new (string Operation, JsonObject Input)[]
        {
            ("enrollment.list", new JsonObject { ["args"] = new JsonObject { ["limit"] = 10, ["offset"] = 0 }, ["campaign"] = campaign.DeepClone() }),
            ("engagement.summarize", new JsonObject
            {
                ["args"] = new JsonObject { ["window"] = new JsonObject { ["preset"] = "last_week" }, ["group_by"] = "channel" },
                ["campaign"] = campaign.DeepClone(),
            }),
            ("conversation.list", new JsonObject
            {
                ["args"] = new JsonObject { ["limit"] = 10, ["offset"] = 0, ["source"] = "all" },
                ["contacts"] = new JsonArray(new JsonObject { ["id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", ["external_ids"] = new JsonObject { ["contact"] = "1001" } }),
                ["campaign"] = campaign.DeepClone(),
            }),
            ("conversation.get", new JsonObject
            {
                ["args"] = new JsonObject { ["thread"] = new JsonObject { ["external_id"] = "501" }, ["limit"] = 10, ["offset"] = 0 },
                ["campaign"] = campaign.DeepClone(),
            }),
        };

        var answered = new List<string>();
        foreach (var (operation, input) in reads)
        {
            answered.Add(ReplyOperations.Succeeded(operation, await ReplyOperations.InvokeAsync(api, operation, input, Ct)).ToJsonString());
        }

        // What the results carry, which is where it belongs: a name from the list, a subject and a preview from the
        // threads, a body and an address from the thread itself.
        Assert.Contains(Name, answered[0], StringComparison.Ordinal);
        Assert.Contains(Subject, answered[2], StringComparison.Ordinal);
        Assert.Contains(Preview, answered[2], StringComparison.Ordinal);
        Assert.Contains(Body, answered[3], StringComparison.Ordinal);
        Assert.Contains(Address, answered[3], StringComparison.Ordinal);

        // And nowhere else. A read sends identifiers, a window and a page, so the commands themselves hold none of it.
        // Every thread is two of Reply's buckets, so the list of threads is two of these calls.
        Assert.Equal(5, account.Calls.Count);
        foreach (var call in account.Calls)
        {
            var sent = string.Join(' ', call.Args) + (call.Body ?? string.Empty);
            foreach (var field in personal)
            {
                Assert.DoesNotContain(field, sent, StringComparison.OrdinalIgnoreCase);
            }
        }

        var diagnostics = ReadAll(api.Paths.PluginWorkDirectory);
        Assert.Contains("\"exec\"", diagnostics, StringComparison.Ordinal);
        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);
        Assert.Contains("conversation.get", logs, StringComparison.Ordinal);
        foreach (var field in personal)
        {
            Assert.DoesNotContain(field, diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(field, logs, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The two writes that take a person out carry what the person said and why they were taken out — Jason's record,
    /// which never reaches Reply — and the CLI finds the person by an address. The record reaches no argument, no
    /// request, no diagnostic and no log; the address travels once per command, in the person on stdin, and nowhere
    /// else.
    /// Each corpus is shown to hold what did travel, so a value that had gone there would be found.
    /// </summary>
    [Fact]
    public async Task What_a_stop_and_an_opt_out_record_reaches_no_request_argument_diagnostic_or_log()
    {
        const string Address = "wilhelmina.marker@example.com";
        const string Statement = "Statement-marker-5a9c please stop";
        const string ArrivedVia = "via_marker_4c2e";
        const string SaidAt = "2025-02-11T06:07:08Z";
        const string LearnedAt = "2025-02-11T06:09:10Z";
        const string Note = "Note-marker-b13f";
        string[] record = [Statement, ArrivedVia, SaidAt, LearnedAt, Note];

        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, Address, "Wilhelmina").WithEnrollment(ReplyOperations.Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var person = new JsonObject
        {
            ["id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["first_name"] = "Wilhelmina",
            ["last_name"] = null,
            ["company"] = null,
            ["title"] = null,
            ["time_zone"] = null,
            ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address, ["primary"] = true }),
            ["external_ids"] = new JsonObject(),
        };
        var campaign = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject { ["campaign"] = "7" },
        };
        var writes = new (string Operation, JsonObject Input)[]
        {
            ("optout.record", new JsonObject
            {
                ["args"] = new JsonObject
                {
                    ["channel"] = "email",
                    ["statement"] = Statement,
                    ["arrived_via"] = ArrivedVia,
                    ["said_at"] = SaidAt,
                    ["learned_at"] = LearnedAt,
                },
                ["contacts"] = new JsonArray(person.DeepClone()),
                ["campaign"] = campaign.DeepClone(),
                ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRP",
            }),
            ("enrollment.stop", new JsonObject
            {
                ["args"] = new JsonObject { ["channel"] = "email", ["reason"] = new JsonObject { ["code"] = "opted_out", ["note"] = Note } },
                ["contacts"] = new JsonArray(person.DeepClone()),
                ["campaign"] = campaign.DeepClone(),
                ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRQ",
            }),
        };

        foreach (var (operation, input) in writes)
        {
            ReplyOperations.Succeeded(operation, await ReplyOperations.InvokeAsync(api, operation, input, Ct));
        }

        // Both writes did their work, the CLI finding the person by the address each time.
        Assert.Equal(["contact opt-out", "sequence remove-contact 7"], Paths(account));
        Assert.Equal([1001], account.OptOuts.Select(entry => entry!.GetValue<int>()));
        Assert.Empty(account.EnrolledIn(ReplyOperations.Sequence));

        foreach (var call in account.Calls)
        {
            var sent = string.Join(' ', call.Args) + (call.Body ?? string.Empty);
            foreach (var field in record)
            {
                Assert.DoesNotContain(field, sent, StringComparison.OrdinalIgnoreCase);
            }

            // The address goes to the CLI in the person on stdin, and to nothing else.
            Assert.DoesNotContain(Address, string.Join(' ', call.Args), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Address, call.Body!, StringComparison.OrdinalIgnoreCase);
        }

        var diagnostics = ReadAll(api.Paths.PluginWorkDirectory);
        Assert.Contains("\"exec\"", diagnostics, StringComparison.Ordinal);
        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);
        Assert.Contains("enrollment.stop", logs, StringComparison.Ordinal);
        foreach (var field in record.Append(Address))
        {
            Assert.DoesNotContain(field, diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(field, logs, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The inbox's writes carry what a person wrote and chose: the text of a reply, and the name of a category. The text
    /// travels once, on the reply's stdin, the name never leaves the package — it is matched against Reply's list, and
    /// only Reply's number for the category reaches a command line — and the person's address, which the thread's own
    /// read brings back, is compared and dropped. None of them reaches an argument, a diagnostic or a log.
    /// Each corpus is shown to hold what did travel, so a value that had gone there would be found.
    /// </summary>
    [Fact]
    public async Task What_a_reply_says_and_the_category_it_names_reach_no_argument_diagnostic_or_log()
    {
        const string Address = "ottoline.marker@example.com";
        const string Text = "Text-marker-7d2e Thursday at 10 works.";
        const string CategoryName = "Category-marker-91fa";

        var thread = ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z");
        thread["contact"]!["email"] = Address;
        using var account = new ReplyAccount().WithCategory(3, CategoryName).WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var campaign = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject { ["campaign"] = "7" },
        };
        var writes = new (string Operation, JsonObject Input)[]
        {
            ("conversation.reply", new JsonObject
            {
                ["args"] = new JsonObject
                {
                    ["channel"] = "email",
                    ["thread"] = new JsonObject { ["external_id"] = "501" },
                    ["body"] = Text,
                },
                ["contacts"] = new JsonArray(new JsonObject
                {
                    ["id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                    ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address, ["primary"] = true }),
                    ["external_ids"] = new JsonObject(),
                }),
                ["campaign"] = campaign.DeepClone(),
                ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRS",
            }),
            ("conversation.classify", new JsonObject
            {
                ["args"] = new JsonObject
                {
                    ["thread"] = new JsonObject { ["external_id"] = "501" },
                    ["category"] = new JsonObject { ["name"] = CategoryName },
                },
                ["campaign"] = campaign.DeepClone(),
                ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRT",
            }),
        };

        foreach (var (operation, input) in writes)
        {
            ReplyOperations.Succeeded(operation, await ReplyOperations.InvokeAsync(api, operation, input, Ct));
        }

        Assert.Equal(["inbox get 501", "inbox reply 501", "inbox categories", "inbox categorize 501"], Paths(account));
        Assert.Equal(Text, (string?)JsonNode.Parse(account.Calls[1].Body!)!["body"]);
        foreach (var call in account.Calls)
        {
            var arguments = string.Join(' ', call.Args);
            foreach (var value in new[] { Text, CategoryName, Address })
            {
                Assert.DoesNotContain(value, arguments, StringComparison.OrdinalIgnoreCase);
            }

            if (call.Command != "inbox reply")
            {
                Assert.Null(call.Body);
            }
        }

        var diagnostics = ReadAll(api.Paths.PluginWorkDirectory);
        Assert.Contains("\"exec\"", diagnostics, StringComparison.Ordinal);
        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);
        Assert.Contains("conversation.reply", logs, StringComparison.Ordinal);
        foreach (var value in new[] { Text, CategoryName, Address })
        {
            Assert.DoesNotContain(value, diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(value, logs, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Everything one invocation answered, whichever way it ended, as one string to look through.</summary>
    private static string Answered(PluginInvocationResult result) => result.Outcome switch
    {
        InvocationOutcome.Succeeded succeeded =>
            (succeeded.Result?.ToJsonString() ?? string.Empty) + (succeeded.ExternalIds?.ToJsonString() ?? string.Empty),
        InvocationOutcome.Failed failed =>
            failed.Error.Code + failed.Error.Message + (failed.Error.Details?.ToJsonString() ?? string.Empty)
            + (failed.Error.ExternalIds?.ToJsonString() ?? string.Empty),
        InvocationOutcome.ProtocolFailure protocol => protocol.Code + protocol.Message + protocol.StderrTail,
        _ => string.Empty,
    };

    /// <summary>Every call the account was asked to make, as "METHOD /path", in the order it was asked.</summary>
    private static IReadOnlyList<string> Paths(ReplyAccount account) =>
        [.. account.Calls.Select(call => call.Line)];

    private static string ReadAll(string directory)
    {
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        return string.Concat(files.Select(ReadShared));
    }

    /// <summary>A log file the runtime may still hold open is read rather than fought over.</summary>
    private static string ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
