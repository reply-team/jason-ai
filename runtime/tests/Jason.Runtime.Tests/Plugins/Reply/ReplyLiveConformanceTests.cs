using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Tests.Plugins.Invocation;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>campaign.get</c>, <c>list_membership.add</c> and <c>campaign.enroll</c> against a real Reply account, through
/// the real package, the real plugin host and the operator's own vendor CLI. It is the only test in this package
/// that is not offline, and everything about it is arranged so that it cannot happen by accident: it runs only when
/// an operator has named, in the environment, the account to act as and the one recipient it may touch, and it skips
/// loudly — naming the key that is absent and what that key is for — the moment either is missing. A skip that said
/// nothing would be indistinguishable from a pass, and this is the one test where a false pass would mean "nobody
/// ever checked the provider" while the suite claimed it had.
/// </summary>
/// <remarks>
/// <para>
/// What it asserts is exactly what the offline tests assert, with nothing planted: the answers satisfy the
/// published output schemas through <see cref="OutcomeContract"/>, the words come from the documents' own
/// vocabularies, the pins come back, a repeated list add leaves one membership, and no address ever
/// reaches a command-line argument. What it cannot assert is the ordered call log, because a real account
/// keeps none for us — so where an offline test reads the stand-in's calls, this one reads the <c>exec</c>
/// diagnostics the host itself wrote.
/// </para>
/// <para>
/// It joins the collection that serialises the classes writing process-wide variables. It writes none itself,
/// but it depends on one it must not find rewritten: the offline classes redirect the CLI's configuration
/// directory to a planted account, and a live child that inherited that redirection would look for the
/// operator's credential in a temporary directory and fail for a reason nobody could read.
/// </para>
/// </remarks>
[Trait(Category, Manual)]
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyLiveConformanceTests
{
    /// <summary>The trait an automated runner excludes by, because this class is driven by a person.</summary>
    public const string Category = "Category";

    public const string Manual = "manual-live";

    /// <summary>
    /// The first key: which account this run acts as. It is the reply-cli profile whose stored credential the
    /// CLI holds — Jason never sees one — and it travels as a route's binding, exactly as an operator's own
    /// route would carry it.
    /// </summary>
    private const string ProfileKey = "JASON_LIVE_REPLY_PROFILE";

    /// <summary>
    /// The second key, and the reason there are two: it is a person. <c>campaign.enroll</c> into a live
    /// sequence is a send, so the address named here is the only one this file may ever reach, and an
    /// enrollment without it does not run at all.
    /// </summary>
    private const string RecipientKey = "JASON_LIVE_REPLY_RECIPIENT";

    /// <summary>The list the recipient is added to. A live identifier is never guessed at.</summary>
    private const string ListKey = "JASON_LIVE_REPLY_LIST";

    /// <summary>The sequence that is read and enrolled into. A live identifier is never guessed at.</summary>
    private const string SequenceKey = "JASON_LIVE_REPLY_SEQUENCE";

    /// <summary>
    /// A contact already taking part in that sequence, by Reply's own identifier. Named only for the guard below,
    /// which must run against a sequence nobody has started: if the guard were wrong, its write would land there.
    /// </summary>
    private const string ParticipantKey = "JASON_LIVE_REPLY_PARTICIPANT";

    /// <summary>
    /// A fresh address its operator controls, which Reply must not hold yet: the person a list add imports under a
    /// derived first name. A list add sends nothing, so naming it reaches nobody's inbox.
    /// </summary>
    private const string NamelessKey = "JASON_LIVE_REPLY_NAMELESS";

    /// <summary>
    /// Optional: a made-up last name for that person. Set, the first name is derived from it; unset, from the address's
    /// text before the <c>@</c>.
    /// </summary>
    private const string NamelessLastKey = "JASON_LIVE_REPLY_NAMELESS_LAST";

    private const string CampaignGet = "campaign.get";
    private const string MembershipAdd = "list_membership.add";
    private const string Enroll = "campaign.enroll";
    private const string EnrollmentList = "enrollment.list";

    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string CampaignId = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRJ";

    private const string TheProfile =
        "It names the reply-cli profile whose stored credential this run acts as; the CLI owns that credential and Jason never sees it.";

    private const string TheRecipient =
        "It names the one controlled test recipient this run may touch; an enrollment into a live sequence is a message to a person, and no other address is ever sent to.";

    private const string TheList = "It names the live Reply list this run adds the recipient to.";

    private const string TheSequence = "It names the live Reply sequence this run reads and enrolls into.";

    private const string TheNameless =
        "It names a fresh address its operator controls, which Reply does not hold yet: the person a list add imports under a derived first name. Nothing is sent to it.";

    private const string TheParticipant =
        "It names, by Reply's contact identifier, a person already taking part in that sequence; the sequence must be one nobody has started.";

    /// <summary>
    /// The flags A1 forbids, held to here as well as offline: a key on a command line, a verbose mode that
    /// would print the whole request and response, an impersonation the binding has no field for, a
    /// pretty-printer nothing parses, and a key the API does not honour.
    /// </summary>
    private static readonly string[] Forbidden = ["-k", "--api-key", "--verbose", "--user-id", "--user-email", "--pretty", "--idempotency-key"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // campaign.get, list_membership.add and campaign.enroll, each against the live account
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_live_campaign_read_resolves_the_named_sequence_and_pins_it()
    {
        var profile = Require(ProfileKey, TheProfile);
        var sequence = Require(SequenceKey, TheSequence);

        await using var api = await StartAgainstTheMachineAsync(Ct);

        var result = await InvokeAsync(api, CampaignGet, CampaignGetInput(sequence), profile, Ct);

        // Held to the published document first, so "it answered" is never mistaken for "it answered what the
        // contract says": the result satisfies the output schema, the status is one of the words that schema
        // publishes, and the identifier the provider confirmed comes back as the pin the runtime records.
        var answer = Succeeded(CampaignGet, result);
        Assert.Equal(sequence, answer["campaign"]!["external_id"]!.GetValue<string>());
        Assert.Contains(answer["campaign"]!["status"]!.GetValue<string>(), CampaignStatusWords());

        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal(sequence, outcome.ExternalIds!["campaign"]!.GetValue<string>());

        await AssertNothingLeakedAsync(api, address: null, result);
    }

    [Fact]
    public async Task A_live_list_add_puts_the_named_recipient_on_the_named_list_and_a_repeat_leaves_one_membership()
    {
        var profile = Require(ProfileKey, TheProfile);
        var recipient = Require(RecipientKey, TheRecipient);
        var list = Require(ListKey, TheList);

        await using var api = await StartAgainstTheMachineAsync(Ct);

        var first = await InvokeAsync(api, MembershipAdd, MembershipInput(list, recipient), profile, Ct);

        // The contact is ensured and the membership written, and Reply's own identifier for that person comes
        // back in both places the contract names — the item, which records whose identifier it is, and the
        // envelope, which is what the runtime pins.
        var added = Succeeded(MembershipAdd, first);
        Assert.Equal("added", added["items"]![0]!["status"]!.GetValue<string>());
        var pin = added["items"]![0]!["external_ids"]!["contact"]!.GetValue<string>();
        Assert.Equal(pin, Assert.IsType<InvocationOutcome.Succeeded>(first.Outcome).ExternalIds!["contact"]!.GetValue<string>());

        // The repeat after a lost answer, against the provider: Reply keeps one membership however often a person is
        // added and does not say whether they were there already, so the repeat runs the add again and answers
        // `added` for the same contact.
        var again = await InvokeAsync(api, MembershipAdd, MembershipInput(list, recipient, pinned: pin), profile, Ct, attempt: 2);
        var repeat = Succeeded(MembershipAdd, again);
        Assert.Equal("added", repeat["items"]![0]!["status"]!.GetValue<string>());
        Assert.Equal(pin, repeat["items"]![0]!["external_ids"]!["contact"]!.GetValue<string>());

        await AssertNothingLeakedAsync(api, recipient, first, again);
    }

    /// <summary>
    /// What Reply accepts as a derived first name, which the stand-in cannot say: a person with no first name, whom Reply
    /// does not hold, added to a list once. The first name is their last name when one is named, or else the address's
    /// text before the <c>@</c>, dot and plus and case included. It never enrols, so nothing can be sent. The run leaves a
    /// contact behind on purpose; its operator deletes it after reading it at Reply.
    /// </summary>
    [Fact]
    public async Task A_live_list_add_of_a_person_reply_does_not_hold_and_who_has_no_first_name_imports_them_under_a_derived_one()
    {
        var profile = Require(ProfileKey, TheProfile);
        var list = Require(ListKey, TheList);
        var nameless = Require(NamelessKey, TheNameless);
        var last = Environment.GetEnvironmentVariable(NamelessLastKey) is { Length: > 0 } given ? given.Trim() : null;

        await using var api = await StartAgainstTheMachineAsync(Ct);

        var result = await InvokeAsync(api, MembershipAdd, NamelessInput(list, nameless, last), profile, Ct);

        var added = Succeeded(MembershipAdd, result);
        Assert.Equal("added", added["items"]![0]!["status"]!.GetValue<string>());
        var pin = added["items"]![0]!["external_ids"]!["contact"]!.GetValue<string>();
        Assert.Equal(pin, Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!["contact"]!.GetValue<string>());

        // One line says a name was derived, and from which field. None means the lookup found somebody Reply already
        // held under the address, and then nothing was derived and this run proved nothing about it.
        var stderr = ReadShared(Path.Combine(result.Launch!.WorkDir, "stderr.log"));
        var derived = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonNode.Parse(line) as JsonObject)
            .Where(line => line is not null && line["source"]?.GetValue<string>() == "plugin")
            .ToList();
        Assert.True(derived.Count > 0, $"No first name was derived, so Reply already held {NamelessKey}'s address: name a fresh address.");
        Assert.Equal(last is null ? "email_local_part" : "last_name", Assert.Single(derived)!["data"]!["first_name_from"]!.GetValue<string>());

        if (last is not null)
        {
            foreach (var argument in ExecArguments(api.Paths).SelectMany(command => command))
            {
                AssertAbsent("The last name", last, argument, "a command-line argument");
            }

            AssertAbsent("The last name", last, ReadAll(api.Paths.PluginWorkDirectory), "a diagnostic");
        }

        await AssertNothingLeakedAsync(api, nameless, result);
    }

    [Fact]
    public async Task A_live_enrollment_runs_once_for_the_named_recipient_and_says_honestly_whether_it_was_a_send()
    {
        var profile = Require(ProfileKey, TheProfile);

        // Asked for second on purpose: a run that named the account and forgot the person must be told which of
        // the two keys is missing, and this is the operation where the difference is a message to a stranger.
        var recipient = Require(RecipientKey, TheRecipient);
        var sequence = Require(SequenceKey, TheSequence);

        await using var api = await StartAgainstTheMachineAsync(Ct);

        // What the sequence's state is, read through the operation a planner would read it with rather than
        // asserted from what somebody remembered setting up. It is the answer `campaign_live` has to agree with.
        var read = Succeeded(CampaignGet, await InvokeAsync(api, CampaignGet, CampaignGetInput(sequence), profile, Ct));
        var live = string.Equals(read["campaign"]!["status"]!.GetValue<string>(), "live", StringComparison.Ordinal);

        var result = await InvokeAsync(api, Enroll, EnrollInput(sequence, recipient), profile, Ct);

        var answer = Succeeded(Enroll, result);
        var item = Assert.Single(answer["items"]!.AsArray())!.AsObject();
        Assert.Equal(ContactId, item["contact_id"]!.GetValue<string>());
        Assert.Contains(item["status"]!.GetValue<string>(), ItemStatusWords(Enroll));

        // The field the whole operation is judged by: whether this was bookkeeping or a message to a person. A
        // plugin that always said false would pass every offline test that never plants a live sequence, so it
        // is checked against the state the provider itself reported a moment earlier.
        Assert.Equal(live, answer["campaign_live"]!.GetValue<bool>());
        Assert.Equal(sequence, Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!["campaign"]!.GetValue<string>());

        // Once means once. Every invocation gets a work directory of its own, so the count is the number of
        // times this test reached the provider at all: the state read, and the enrollment.
        Assert.Equal(2, Directory.GetDirectories(api.Paths.PluginWorkDirectory).Length);

        // And the repeat, which is the path that keeps one interrupted attempt from sending to a person twice. The CLI
        // reads the participation this run just made before it writes anything, and finds it: a repeated attempt
        // answers `already_enrolled` from that, whatever the policy.
        var pin = item["external_ids"]!["contact"]!.GetValue<string>();
        var again = await InvokeAsync(api, Enroll, EnrollInput(sequence, recipient, pinned: pin), profile, Ct, attempt: 2);
        var repeat = Succeeded(Enroll, again);
        Assert.Equal("already_enrolled", Assert.Single(repeat["items"]!.AsArray())!["status"]!.GetValue<string>());
        Assert.Equal(live, repeat["campaign_live"]!.GetValue<bool>());

        await AssertNothingLeakedAsync(api, recipient, result, again);
    }

    /// <summary>
    /// reply-cli reads whether a person already takes part before it adds them, and answers that with nothing written;
    /// the package decides the answer by the policy. This is that, live, for a contact already in a sequence nobody has
    /// started: `skip` answers `already_enrolled`, `refuse` fails `collision_refused`, nothing is written, and the
    /// participation reads the same afterwards as before.
    /// </summary>
    [Fact]
    public async Task A_live_enrollment_of_a_person_already_taking_part_writes_nothing_under_either_policy()
    {
        var profile = Require(ProfileKey, TheProfile);
        var sequence = Require(SequenceKey, TheSequence);
        var participant = Require(ParticipantKey, TheParticipant);

        await using var api = await StartAgainstTheMachineAsync(Ct);

        await LeavesAParticipantAloneAsync((operation, input) => InvokeAsync(api, operation, input, profile, Ct), sequence, participant);

        // Both policies reached the CLI's add for the participant, and the participation read the same before and
        // after: the host's own record of every program it ran shows the two adds.
        Assert.Equal(2, ExecArguments(api.Paths).Count(command => command.Contains("add-contact") && command.Contains(participant)));
    }

    /// <summary>
    /// The guard's run, against whichever account <paramref name="invoke"/> reaches. The campaign is read first, and a
    /// live one stops it before any enrolment: the run is meant for a sequence nobody has started, where a guard that
    /// failed would write without sending, and naming a live sequence by mistake must not become a send. Then the
    /// participation is read, both policies are asked for, and the participation is read again, unchanged.
    /// </summary>
    internal static async Task LeavesAParticipantAloneAsync(
        Func<string, JsonObject, Task<PluginInvocationResult>> invoke,
        string sequence,
        string participant)
    {
        var read = await invoke(CampaignGet, CampaignGetInput(sequence));
        Assert.True(
            read.Outcome is InvocationOutcome.Succeeded,
            $"The campaign could not be read, so nothing was enrolled: {(read.Outcome as InvocationOutcome.Failed)?.Error.Code ?? read.Outcome.GetType().Name}.");
        var status = Succeeded(CampaignGet, read)["campaign"]!["status"]!.GetValue<string>();
        Assert.True(
            status != "live",
            $"The sequence is {status} at the provider. This run is only for a sequence nobody has started, so nothing was enrolled.");

        var before = Participation(Succeeded(EnrollmentList, await invoke(EnrollmentList, EnrollmentListInput(sequence))), participant);

        // The address is never sent for a pinned person; the contract's projection still carries a channel value.
        var skip = Succeeded(Enroll, await invoke(Enroll, EnrollInput(sequence, "participant@example.invalid", pinned: participant)));
        Assert.Equal("already_enrolled", Assert.Single(skip["items"]!.AsArray())!["status"]!.GetValue<string>());

        var refused = Assert.IsType<InvocationOutcome.Failed>(
            (await invoke(Enroll, EnrollInput(sequence, "participant@example.invalid", pinned: participant, collision: "refuse"))).Outcome);
        Assert.Equal("collision_refused", refused.Error.Code);

        var after = Participation(Succeeded(EnrollmentList, await invoke(EnrollmentList, EnrollmentListInput(sequence))), participant);
        Assert.True(JsonNode.DeepEquals(before, after), $"The participation changed: {before.ToJsonString()} became {after.ToJsonString()}.");
    }

    /// <summary>The participant's row of a sequence's people, reduced to what an enrolment could change.</summary>
    private static JsonObject Participation(JsonObject page, string participant)
    {
        var row = Assert.Single(page["items"]!.AsArray(), item => (string?)item!["external_id"] == participant)!;
        return new JsonObject
        {
            ["status"] = row["status"]!.DeepClone(),
            ["current_step"] = row["current_step"]?.DeepClone(),
            ["current_step_id"] = row["vendor"]!["current_step_id"]?.DeepClone(),
            ["added_at"] = row["added_at"]?.DeepClone(),
        };
    }

    // -------------------------------------------------------------------------------------------------------
    // The gate
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// One key of the gate. A missing key skips the test and says which key it was and what it is for, because
    /// a silent skip inside a suite of two thousand passes reads exactly like a pass — and this is the one file
    /// where that would mean nobody ever checked the provider.
    /// </summary>
    private static string Require(string key, string what)
    {
        var value = Environment.GetEnvironmentVariable(key);
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(value),
            $"Live Reply conformance skipped: {key} is not set. {what} This run acts on a real account, so every key is named by a person or nothing runs.");
        return value!.Trim();
    }

    // -------------------------------------------------------------------------------------------------------
    // The machine, which is the one place in this package where the operator's own program is the right answer
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A runtime with the official package installed, granted the one program it declares, and resolving that
    /// program on the machine's own search path.
    /// </summary>
    /// <remarks>
    /// Every other Reply test registers <see cref="ReplyPlugins.SearchPath"/>, which deliberately leaves the
    /// machine's <c>PATH</c> out so that a test can never reach the operator's real CLI, and asserts that what
    /// resolved lies inside the test tree. This class asserts the exact opposite, and it is the only place in
    /// the suite where that is correct: the whole point here is to run the operator's own <c>reply</c>, against
    /// the operator's own account. A resolution that landed on the stand-in instead would be a live test that
    /// proved nothing about the provider, so it fails here rather than reporting a green run.
    /// </remarks>
    private static async Task<RuntimeApiFixture> StartAgainstTheMachineAsync(CancellationToken cancellationToken)
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            cancellationToken,
            prepare: paths =>
            {
                ReplyPlugins.Install(paths);
                TestPlugins.Grant(paths, ReplyPlugins.PluginId, exec: [ReplyPlugins.ExecutableName]);
            },
            configureServices: services =>
            {
                services.AddSingleton<ISearchPath>(new EnvironmentSearchPath());
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
            });

        try
        {
            var registry = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, cancellationToken);
            var plugin = Assert.Single(registry.Plugins, listed => listed.Id == ReplyPlugins.PluginId);
            Assert.True(
                plugin.Status == PluginStatus.Valid,
                "The package did not load on this machine, so the gate was set where it cannot run: "
                    + string.Join("; ", plugin.Problems.Select(problem => problem.Code + " — " + problem.Message)));

            var resolved = Assert.Single(plugin.Capabilities.Exec!.Requested).Path;
            Assert.False(
                ReplyPlugins.IsInsideTheTestTree(resolved),
                $"'{resolved}' is the stand-in rather than the machine's own CLI; a live run that resolved the stand-in would prove nothing.");
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // What must never leave, checked over what really did
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The privacy rules, held to live. Two things are being ruled out and they are checked differently. A
    /// person's address legitimately reaches Reply, but only inside a body on stdin, so it may appear in no
    /// argument, no outcome, no diagnostic and no log. The account's credential may reach nothing of ours at
    /// all — and its stored value is the one thing this run may not read, because reading it would mean opening
    /// the operator's credential store, so what is checked here is the shape a leak would take: no flag that
    /// could carry a key on a command line, and no value of a vendor-named variable the operator happens to
    /// have exported. The offline privacy test plants a marker and searches for the value itself.
    /// </summary>
    private static async Task AssertNothingLeakedAsync(RuntimeApiFixture api, string? address, params PluginInvocationResult[] results)
    {
        // Every argument vector the host actually started a program with, read out of its own `exec`
        // diagnostics. The list is asserted non-empty first: a rule that held because nothing ran is not a rule.
        var commands = ExecArguments(api.Paths);
        Assert.NotEmpty(commands);

        foreach (var argument in commands.SelectMany(command => command))
        {
            Assert.DoesNotContain(argument, Forbidden);
            if (address is not null)
            {
                AssertAbsent("The recipient's address", address, argument, "a command-line argument");
            }
        }

        var answered = string.Concat(results.Select(Answered));
        var diagnostics = ReadAll(api.Paths.PluginWorkDirectory);

        // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);

        if (address is not null)
        {
            AssertAbsent("The recipient's address", address, answered, "an outcome");
            AssertAbsent("The recipient's address", address, diagnostics, "a diagnostic");
            AssertAbsent("The recipient's address", address, logs, "a log file");
        }

        // No `REPLY_*` name enters the runtime, so a key an operator exported in the shell that started this
        // cannot reach a child — and this is the cheapest place to notice if that ever stopped being true.
        if (Environment.GetEnvironmentVariable("REPLY_API_KEY") is { Length: > 0 } exported)
        {
            AssertAbsent("The exported key", exported, answered, "an outcome");
            AssertAbsent("The exported key", exported, diagnostics, "a diagnostic");
            AssertAbsent("The exported key", exported, logs, "a log file");
        }
    }

    /// <summary>
    /// What may never be found, said without ever naming it in the failure. A value that leaked is not made
    /// safer by being printed into a test log, and a live run's corpus holds a real person's address.
    /// </summary>
    private static void AssertAbsent(string what, string needle, string corpus, string where) =>
        Assert.True(!corpus.Contains(needle, StringComparison.OrdinalIgnoreCase), $"{what} appears in {where}.");

    /// <summary>
    /// Every command the host started, as the arguments it was started with: what the runtime resolved the name
    /// to, and then what the plugin asked for.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<string>> ExecArguments(JasonPaths paths)
    {
        var commands = new List<IReadOnlyList<string>>();
        foreach (var file in Directory.GetFiles(paths.PluginWorkDirectory, "stderr.log", SearchOption.AllDirectories))
        {
            foreach (var line in ReadShared(file).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (JsonNode.Parse(line) is not JsonObject diagnostic
                    || diagnostic["message"]?.GetValue<string>() != "exec"
                    || diagnostic["data"] is not JsonObject data)
                {
                    continue;
                }

                commands.Add([.. Words(data["launch"]), .. Words(data["args"])]);
            }
        }

        return commands;
    }

    private static IEnumerable<string> Words(JsonNode? node) =>
        node is JsonArray array ? array.Select(word => word!.GetValue<string>()) : [];

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

    // -------------------------------------------------------------------------------------------------------
    // Composing the inputs, from what the operator named and nothing else
    // -------------------------------------------------------------------------------------------------------

    private static JsonObject CampaignGetInput(string sequence) => new()
    {
        ["args"] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = sequence } },
        ["campaign"] = Campaign(),
    };

    private static JsonObject EnrollmentListInput(string sequence) => new()
    {
        ["args"] = new JsonObject
        {
            ["campaign"] = new JsonObject { ["external_id"] = sequence },
            ["limit"] = 100,
            ["offset"] = 0,
        },
        ["campaign"] = Campaign(),
    };

    private static JsonObject MembershipInput(string list, string recipient, string? pinned = null) => new()
    {
        ["args"] = new JsonObject
        {
            ["list"] = new JsonObject { ["external_id"] = list },
            ["channel"] = "email",
        },
        ["contacts"] = new JsonArray(Contact(recipient, pinned)),
        ["campaign"] = Campaign(),
        ["idempotency_key"] = WorkItemId,
    };

    /// <summary>
    /// The person with no first name, as Jason may hold one: the address, and a last name only when the operator named
    /// one. Nothing else is supplied, so nothing else can reach the account.
    /// </summary>
    private static JsonObject NamelessInput(string list, string address, string? last) => new()
    {
        ["args"] = new JsonObject
        {
            ["list"] = new JsonObject { ["external_id"] = list },
            ["channel"] = "email",
        },
        ["contacts"] = new JsonArray(new JsonObject
        {
            ["id"] = ContactId,
            ["first_name"] = null,
            ["last_name"] = last,
            ["company"] = null,
            ["title"] = null,
            ["time_zone"] = null,
            ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = address, ["primary"] = true }),
            ["external_ids"] = new JsonObject(),
        }),
        ["campaign"] = Campaign(),
        ["idempotency_key"] = WorkItemId,
    };

    private static JsonObject EnrollInput(string sequence, string recipient, string? pinned = null, string collision = "skip") => new()
    {
        ["args"] = new JsonObject
        {
            ["campaign"] = new JsonObject { ["external_id"] = sequence },
            ["channel"] = "email",

            // Skip rather than refuse, because a controlled recipient already in the sequence from an earlier
            // run is the ordinary case here and is not a reason to fail.
            ["collision"] = collision,
            ["start"] = new JsonObject { ["position"] = "first_step" },
            ["first_touch"] = "authored_delay",
        },
        ["contacts"] = new JsonArray(Contact(recipient, pinned)),
        ["campaign"] = Campaign(),
        ["idempotency_key"] = WorkItemId,
    };

    /// <summary>
    /// The person this run may touch, and nothing about them that nobody supplied. Every field a live account
    /// would otherwise have written is null on purpose — inventing a company or a title would put made-up data
    /// into somebody's CRM. The first name is the exception and it is not invented either: Reply's import
    /// refuses an item that carries none, and the import is the path that matches an existing contact by email
    /// instead of creating a second one, so the address's own local part stands in for it. That is the only
    /// value here that did not come from the operator verbatim, and it is derived from one that did.
    /// </summary>
    private static JsonObject Contact(string recipient, string? pinned) => new()
    {
        ["id"] = ContactId,
        ["first_name"] = LocalPart(recipient),
        ["last_name"] = null,
        ["company"] = null,
        ["title"] = null,
        ["time_zone"] = null,
        ["channels"] = new JsonArray(new JsonObject
        {
            ["channel"] = "email",
            ["value"] = recipient,
            ["primary"] = true,
        }),
        ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
    };

    private static string LocalPart(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at > 0 ? address[..at] : address;
    }

    /// <summary>
    /// The campaign as Jason holds it, which is the half that never reaches the provider: the operation works
    /// from the identifier the operator named, and this projection is what the input schema requires beside it.
    /// </summary>
    private static JsonObject Campaign() => new()
    {
        ["id"] = CampaignId,
        ["name"] = "Live conformance",
        ["status"] = "active",
        ["external_ids"] = new JsonObject(),
    };

    // -------------------------------------------------------------------------------------------------------
    // Holding the answers to the published documents
    // -------------------------------------------------------------------------------------------------------

    /// <summary>The result the plugin answered with, having first been held to the operation's own document.</summary>
    /// <remarks>
    /// Held exactly as the recorded runs hold it, so an outcome that is not a success is reported with everything it
    /// carried: a live run that stops here cannot simply be run again to learn which row it ended on and why.
    /// </remarks>
    private static JsonObject Succeeded(string operation, PluginInvocationResult result) =>
        ReplyOperations.Succeeded(operation, result);

    /// <summary>The states a campaign may be reported in, read from the published schema rather than restated.</summary>
    private static IReadOnlyList<string> CampaignStatusWords() =>
        Vocabulary(OperationCatalog.Find(CampaignGet)!.OutputSchema["properties"]!["campaign"]!["properties"]!["status"]!);

    /// <summary>The words a per-person item may carry, read from the published schema rather than restated.</summary>
    private static IReadOnlyList<string> ItemStatusWords(string operation) =>
        Vocabulary(OperationCatalog.Find(operation)!.OutputSchema["properties"]!["items"]!["items"]!["properties"]!["status"]!);

    private static IReadOnlyList<string> Vocabulary(JsonNode field) =>
        [.. field["enum"]!.AsArray().Select(word => word!.GetValue<string>())];

    private static async Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api,
        string operation,
        JsonObject input,
        string profile,
        CancellationToken kill,
        int attempt = 1)
    {
        // The input is what the document says it is before anything runs: a test that sent nonsense would prove
        // nothing about whether the operation is implementable.
        Assert.Empty(SchemaValidator.Validate(input, OperationCatalog.Find(operation)!.InputSchema));

        return await PluginInvokerTests.InvokeAsync(
            api,
            new PluginInvocationRequest(
                ReplyPlugins.PluginId,
                operation,
                input,

                // The account travels as a route's binding would carry it, which is the only way this package
                // lets one be chosen: never a key, never an impersonation, never a variable in a shell.
                new JsonObject { ["profile"] = profile },
                CorrelationId: "att_01K0REPLYLIVECONFORMANCE",
                AttemptId: "att_01K0REPLYLIVECONFORMANCE",
                AttemptNumber: attempt,
                WorkItemId: WorkItemId,
                CampaignId: CampaignId),
            kill);
    }
}
