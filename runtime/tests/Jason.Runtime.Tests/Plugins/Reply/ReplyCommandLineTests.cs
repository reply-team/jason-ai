using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// What the official package may put on a command line, asserted from the stand-in's own call log rather than
/// from the package's claim about itself. The rule is not about one operation: a command line is built in one
/// place for all three, so every assertion here runs over the calls of all three, and a rule that held for the
/// read and not for the two writes would fail rather than pass on the read alone.
/// </summary>
/// <remarks>
/// The class writes a process-wide variable — the one the CLI finds its account through — so it belongs to the
/// collection that never runs two such classes at once. Every fixture starts through
/// <see cref="ReplyPlugins.StartAsync"/>, which holds the resolved program to the test tree before anything
/// runs: a resolution that escaped would not be a wrong answer, it would be a call to somebody's real account.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyCommandLineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("-k")]
    [InlineData("--api-key")]
    [InlineData("--verbose")]
    [InlineData("--user-id")]
    [InlineData("--user-email")]
    [InlineData("--pretty")]
    public async Task A_flag_this_plugin_must_never_pass_appears_in_no_call(string flag)
    {
        // `--verbose` prints the whole request and response to stderr, which is where a contact's address would
        // reach the exec capture and from there whatever reads it; `--user-id` and `--user-email` are
        // impersonation, and the closed binding has no field for either and will keep having none; `--api-key`
        // and `-k` would put a credential into an argument the runtime writes to its log, when the whole point
        // of driving a CLI is that the credential never leaves its own store; `--pretty` only makes an answer
        // bigger than the capture that has to hold it.
        //
        // The stand-in already refuses a flag it does not know with exit 2, so a package that passed one would
        // fail the operation as well — but the ban is asserted from the log too, because it has to hold even if
        // the stand-in one day grows tolerant of something.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        Assert.Equal(ReplyOperations.EveryCall, Paths(account));
        foreach (var call in account.Calls)
        {
            Assert.DoesNotContain(flag, call.Args);
        }
    }

    [Fact]
    public async Task The_global_prefix_is_exactly_what_the_package_promises()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        // `--json -q [--profile P] [--team-id T] api <path> [--method M] [--body -]`, and nothing else ever, in
        // that order. Every call of every operation is compared with the whole vector rebuilt from what that
        // call was, which is stricter than looking for what must not be there: it also fails on something new
        // that nobody thought to forbid.
        AssertEveryArgumentVector(account, profile: null, team: null);
    }

    [Fact]
    public async Task The_binding_the_route_carries_is_what_reaches_the_command_line()
    {
        // A route's binding is how an installation says which account a campaign acts in. It names a profile
        // and a team and can name nothing else: the schema is closed, so there is no field here for a key or
        // for a user to act as.
        const string Profile = "acme";
        const string Team = "team-42";
        using var account = ReplyOperations.Plant(new ReplyAccount(Profile));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var binding = new JsonObject { ["profile"] = Profile, ["team_id"] = Team };
        await ReplyOperations.RunEachAsync(api, binding, Ct);

        // Read from the log: the account these calls reached is the one that profile names, and both halves of
        // the binding are in the prefix, in the published order, on every call of all three operations.
        AssertEveryArgumentVector(account, Profile, Team);
    }

    [Fact]
    public async Task A_route_with_no_binding_adds_neither_a_profile_nor_a_team()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        // Absent means "whatever the CLI itself is set to", so an empty binding has to add nothing at all
        // rather than an empty flag — proven from the call log rather than from the package's own claim.
        Assert.Equal(ReplyOperations.EveryCall, Paths(account));
        foreach (var call in account.Calls)
        {
            Assert.DoesNotContain("--profile", call.Args);
            Assert.DoesNotContain("--team-id", call.Args);
            Assert.Equal(["--json", "-q", "api"], call.Args.Take(3));
        }
    }

    [Fact]
    public async Task Every_request_body_travels_on_stdin_and_never_in_an_argument()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        // The `exec` diagnostic records every argument and the runtime's logs may never hold contact data, so a
        // body is on stdin or it is nowhere. A call that has one says so with `--body -`; a call that has none
        // never mentions the flag.
        var withBody = 0;
        foreach (var call in account.Calls)
        {
            Assert.DoesNotContain(call.Args, argument => argument.Contains('{', StringComparison.Ordinal));
            if (call.Body is null)
            {
                Assert.DoesNotContain("--body", call.Args);
                continue;
            }

            withBody++;
            Assert.Equal(["--body", "-"], call.Args.TakeLast(2));
            Assert.Single(call.Args, argument => argument == "--body");
        }

        // Four of the eight calls carry one, or the rule above would be a rule about nothing: the two ensures,
        // which carry a person's own data, and the two writes that name them by identifier.
        Assert.Equal(4, withBody);
    }

    /// <summary>
    /// Every call of all three operations, with its whole argument vector rebuilt from what that call was and
    /// compared in full. Nothing is skipped and nothing is matched loosely: the promise is the exact vector.
    /// </summary>
    private static void AssertEveryArgumentVector(ReplyAccount account, string? profile, string? team)
    {
        // The three operations really ran, in full, or every loop below would be one over an empty log.
        Assert.Equal(ReplyOperations.EveryCall, Paths(account));

        foreach (var call in account.Calls)
        {
            Assert.Equal(Expected(call, profile, team), call.Args);
        }
    }

    /// <summary>The one grammar the package publishes, written out here so the package cannot be its own judge.</summary>
    private static IReadOnlyList<string> Expected(ReplyCall call, string? profile, string? team)
    {
        var expected = new List<string> { "--json", "-q" };
        if (profile is not null)
        {
            expected.Add("--profile");
            expected.Add(profile);
        }

        if (team is not null)
        {
            expected.Add("--team-id");
            expected.Add(team);
        }

        expected.Add("api");
        expected.Add(call.Path);

        if (call.Method != "GET")
        {
            expected.Add("--method");
            expected.Add(call.Method);
        }

        if (call.Body is not null)
        {
            expected.Add("--body");
            expected.Add("-");
        }

        return expected;
    }

    /// <summary>Every call the account was asked to make, as "METHOD /path", in the order it was asked.</summary>
    private static IReadOnlyList<string> Paths(ReplyAccount account) =>
        [.. account.Calls.Select(call => call.Method + " " + call.Path)];
}

/// <summary>
/// The three operations this package implements, driven against one account with the inputs their own documents
/// publish. The rules of this file and of the two beside it hold whatever the operation, so they are asserted
/// over all three — which is worth something only if all three really run, so every driver here holds what came
/// back to the operation's own contract before a test reads it.
/// </summary>
internal static class ReplyOperations
{
    public const string CampaignGet = "campaign.get";
    public const string MembershipAdd = "list_membership.add";
    public const string Enroll = "campaign.enroll";

    /// <summary>The sequence and the list every test plants, by identifiers of the shape Reply actually issues.</summary>
    public const int Sequence = 7;

    public const int List = 9;

    /// <summary>The identifier Reply gives the first contact an account creates.</summary>
    public const string Ensured = "1001";

    public const string Address = "marta@example.com";
    public const string FirstName = "Marta";
    public const string LastName = "Alvarez";
    public const string Company = "Puerto Analytics";
    public const string Title = "Head of Growth";
    public const string TimeZone = "America/Bogota";

    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string CampaignId = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRH";

    /// <summary>The three, in the order <see cref="RunEachAsync"/> drives them.</summary>
    public static IReadOnlyList<string> All => [CampaignGet, MembershipAdd, Enroll];

    /// <summary>
    /// Every call the three operations make against a planted account, in order: one read for the campaign;
    /// then the ensure, the suppression read and the add; then the ensure, the live-state read, the suppression
    /// read and the enrollment. A test that asserts a rule over "every call" asserts this list first, so a rule
    /// that held because nothing ran fails instead.
    /// </summary>
    public static IReadOnlyList<string> EveryCall =>
    [
        $"GET /v3/sequences/{Sequence}",
        "POST /v3/contacts/import",
        $"GET /v3/contacts/{Ensured}/statuses",
        $"POST /v3/contact-lists/{List}/add-contacts",
        "POST /v3/contacts/import",
        $"GET /v3/sequences/{Sequence}",
        $"GET /v3/contacts/{Ensured}/statuses",
        $"POST /v3/sequences/{Sequence}/contact-links/bulk",
    ];

    /// <summary>The provider's world all three operations need: one live sequence with steps, and one list.</summary>
    public static ReplyAccount Plant(ReplyAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account
            .WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11, 12)
            .WithList(List, "Q3 LatAm founders");
    }

    /// <summary>Drives all three operations once, each held to its own document, and answers with what they said.</summary>
    public static async Task<IReadOnlyList<PluginInvocationResult>> RunEachAsync(
        RuntimeApiFixture api,
        JsonObject? binding,
        CancellationToken kill)
    {
        var results = new List<PluginInvocationResult>();
        foreach (var operation in All)
        {
            var result = await InvokeAsync(api, operation, Input(operation), kill, binding: binding);
            Succeeded(operation, result);
            results.Add(result);
        }

        return results;
    }

    /// <summary>The canonical input of one operation, as its own document defines it.</summary>
    public static JsonObject Input(string operation) => operation switch
    {
        CampaignGet => CampaignGetInput(),
        MembershipAdd => MembershipAddInput(),
        Enroll => EnrollInput(),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "This package implements three operations."),
    };

    /// <summary>A campaign named by the identifier a planner supplied, or by the pin the runtime already holds.</summary>
    public static JsonObject CampaignGetInput(string? supplied = null, string? pinned = null) => new()
    {
        ["args"] = new JsonObject
        {
            ["campaign"] = new JsonObject
            {
                ["external_id"] = supplied ?? Sequence.ToString(CultureInfo.InvariantCulture),
            },
        },
        ["campaign"] = Campaign(pinned),
    };

    public static JsonObject MembershipAddInput(string? pinned = null) => new()
    {
        ["args"] = new JsonObject
        {
            ["list"] = new JsonObject { ["external_id"] = List.ToString(CultureInfo.InvariantCulture) },
            ["channel"] = "email",
        },
        ["contacts"] = new JsonArray(Contact(pinned)),
        ["campaign"] = Campaign(null),
        ["idempotency_key"] = WorkItemId,
    };

    public static JsonObject EnrollInput(string? pinned = null) => new()
    {
        ["args"] = new JsonObject
        {
            ["campaign"] = new JsonObject { ["external_id"] = Sequence.ToString(CultureInfo.InvariantCulture) },
            ["channel"] = "email",
            ["collision"] = "skip",
            ["start"] = new JsonObject { ["position"] = "first_step" },
            ["first_touch"] = "authored_delay",
        },
        ["contacts"] = new JsonArray(Contact(pinned)),
        ["campaign"] = Campaign(null),
        ["idempotency_key"] = WorkItemId,
    };

    public static async Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api,
        string operation,
        JsonObject input,
        CancellationToken kill,
        JsonObject? binding = null,
        int attempt = 1,
        TimeSpan? timeout = null)
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
                binding,
                CorrelationId: "att_01K0REPLYEVERYOPERATION",
                AttemptId: "att_01K0REPLYEVERYOPERATION",
                AttemptNumber: attempt,
                WorkItemId: WorkItemId,
                CampaignId: CampaignId,
                Timeout: timeout),
            kill);
    }

    /// <summary>The result the plugin answered with, having first been held to the operation's own document.</summary>
    /// <remarks>
    /// An outcome that is not a success is reported with everything it carried. Every test in this area drives
    /// its operations through <see cref="RunEachAsync"/>, so this is the last place the outcome exists: asserting
    /// only its type would answer an intermittent failure with "expected Succeeded, found Failed" and throw away
    /// the code, the message, the class and the child's stderr — the one sighting that could explain it.
    /// </remarks>
    public static JsonObject Succeeded(string operation, PluginInvocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome is not InvocationOutcome.Succeeded)
        {
            Assert.Fail($"{operation} did not succeed. {Describe(result.Outcome)}");
        }

        var outcome = (InvocationOutcome.Succeeded)result.Outcome;
        var contract = OperationCatalog.Find(operation)!;

        Assert.Empty(OutcomeContract.CheckResult(contract, outcome.Result));
        Assert.Empty(OutcomeContract.CheckExternalIds(contract, outcome.ExternalIds));
        return outcome.Result!.AsObject();
    }

    /// <summary>
    /// Everything an outcome that is not a success carried, in one line a failure message can hold: what the
    /// plugin declared, or how far the protocol got before it stopped.
    /// </summary>
    private static string Describe(InvocationOutcome outcome) => outcome switch
    {
        InvocationOutcome.Failed failed => string.Create(
            CultureInfo.InvariantCulture,
            $"The plugin declared a failure: {failed.Error.Class} '{failed.Error.Code}' — {failed.Error.Message}"
            + $"{(failed.Error.Details is { } details ? $" Details: {details.ToJsonString()}" : string.Empty)}"),
        InvocationOutcome.ProtocolFailure protocol => string.Create(
            CultureInfo.InvariantCulture,
            $"The protocol did not complete: '{protocol.Code}' — {protocol.Message} Exit code: "
            + $"{(protocol.ExitCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : "none")}. Stderr tail: "
            + $"{(string.IsNullOrWhiteSpace(protocol.StderrTail) ? "(empty)" : protocol.StderrTail)}"),
        _ => $"Unrecognised outcome {outcome.GetType().Name}.",
    };

    /// <summary>
    /// The failure the plugin declared, held to the codes the operation's own document publishes. A protocol
    /// failure is not a failure the plugin declared at all, and a code the document does not name is one the
    /// runtime would refuse, so both are told apart here rather than in every test.
    /// </summary>
    public static OutcomeError Failed(string operation, PluginInvocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        var contract = OperationCatalog.Find(operation)!;
        var declared = contract.FailureCodes.SingleOrDefault(code => code.Code == failed.Error.Code);

        Assert.True(
            declared is not null,
            $"{operation} declares no failure code '{failed.Error.Code}': {failed.Error.Message}");
        Assert.Equal(declared!.Class, failed.Error.Class);
        return failed.Error;
    }

    private static JsonObject Contact(string? pinned) => new()
    {
        ["id"] = ContactId,
        ["first_name"] = FirstName,
        ["last_name"] = LastName,
        ["company"] = Company,
        ["title"] = Title,
        ["time_zone"] = TimeZone,
        ["channels"] = new JsonArray(new JsonObject
        {
            ["channel"] = "email",
            ["value"] = Address,
            ["primary"] = true,
        }),
        ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
    };

    private static JsonObject Campaign(string? pinned) => new()
    {
        ["id"] = CampaignId,
        ["name"] = "Q3 LatAm founders",
        ["status"] = "active",
        ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["campaign"] = pinned },
    };
}
