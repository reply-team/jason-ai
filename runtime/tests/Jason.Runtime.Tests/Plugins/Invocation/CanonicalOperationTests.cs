using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// The three published contracts, implemented by a real plugin against a real provider account and driven
/// through the shipped executable. The fixtures under <c>docs/contracts/fixtures/</c> say what a conforming
/// answer looks like; this is their live counterpart, and what makes the claim "these contracts are
/// implementable" something other than an opinion.
/// </summary>
/// <remarks>
/// Nothing routes a work item to a plugin yet, so the canonical input is composed here exactly as the operation
/// document defines it, and checked against that document before it is sent. Every answer is checked back
/// against the document too, so a result that would be refused once outcome validation is wired up fails here
/// first, next to the code that produced it.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class CanonicalOperationTests
{
    private const string CampaignId = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string SecondWorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE";
    private const string ProviderCampaign = "cmp_42";
    private const string ProviderList = "lst_7";
    private const string Marta = "marta@example.test";
    private const string Blocked = "blocked@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_first_read_links_a_campaign_by_the_identifier_the_planner_supplied_and_pins_it()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Q3 outbound", "Draft", "p_1001", "p_1002");
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, workspace, "campaign.get", CampaignGetInput(ProviderCampaign), Ct);

        var answer = Succeeded(result, "campaign.get");
        Assert.Equal(ProviderCampaign, answer["campaign"]!["external_id"]!.GetValue<string>());
        Assert.Equal("Q3 outbound", answer["campaign"]!["name"]!.GetValue<string>());
        Assert.Equal("draft", answer["campaign"]!["status"]!.GetValue<string>());
        Assert.Equal(2, answer["campaign"]!["counts"]!["enrolled"]!.GetValue<int>());

        // The provider's own word for the state is kept, untranslated, next to the neutral one.
        Assert.Equal("Draft", answer["vendor"]!["state"]!.GetValue<string>());

        // The pin is what every later operation works by, so the read has to return it.
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal(ProviderCampaign, outcome.ExternalIds!["campaign"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_campaign_already_pinned_is_read_without_the_planner_naming_it_again()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Q3 outbound", "Active");
        await using var api = await StartAsync();

        var input = new JsonObject
        {
            ["args"] = new JsonObject(),
            ["campaign"] = Campaign(pinned: ProviderCampaign),
        };

        var answer = Succeeded(await InvokeAsync(api, workspace, "campaign.get", input, Ct), "campaign.get");

        Assert.Equal("live", answer["campaign"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_provider_state_the_vocabulary_has_no_word_for_is_reported_as_other()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Q3 outbound", "Sunsetting");
        await using var api = await StartAsync();

        var answer = Succeeded(await InvokeAsync(api, workspace, "campaign.get", CampaignGetInput(ProviderCampaign), Ct), "campaign.get");

        Assert.Equal("other", answer["campaign"]!["status"]!.GetValue<string>());
        Assert.Equal("Sunsetting", answer["vendor"]!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_campaign_the_provider_does_not_hold_fails_as_permanent_rather_than_answering_emptily()
    {
        using var workspace = new TestWorkspace();
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, workspace, "campaign.get", CampaignGetInput("cmp_none"), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("campaign_not_found", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
    }

    [Fact]
    public async Task An_add_creates_the_provider_contact_and_brings_its_identifier_back_to_be_pinned()
    {
        using var workspace = new TestWorkspace();
        workspace.WithList(ProviderList, "Q3 prospects");
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, workspace, "list_membership.add", ListAddInput(WorkItemId), Ct);

        var item = Succeeded(result, "list_membership.add")["items"]!.AsArray()[0]!;
        Assert.Equal(ContactId, item["contact_id"]!.GetValue<string>());
        Assert.Equal("added", item["status"]!.GetValue<string>());

        var provider = item["external_ids"]!["contact"]!.GetValue<string>();
        Assert.Equal(provider, Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!["contact"]!.GetValue<string>());
        Assert.Equal([provider], workspace.MembersOf(ProviderList));
        Assert.Equal(Marta, workspace.Contacts[provider]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_contact_that_is_already_pinned_is_worked_by_that_identifier_and_never_by_the_address()
    {
        using var workspace = new TestWorkspace();
        workspace.WithContact("p_1001", "email", "an.older.address@example.test").WithList(ProviderList, "Q3 prospects");
        await using var api = await StartAsync();

        var input = ListAddInput(WorkItemId, pinnedContact: "p_1001");
        var item = Succeeded(await InvokeAsync(api, workspace, "list_membership.add", input, Ct), "list_membership.add")["items"]!.AsArray()[0]!;

        Assert.Equal("p_1001", item["external_ids"]!["contact"]!.GetValue<string>());
        Assert.Equal(["by_id"], workspace.AsksFor("p_1001"));

        // Nobody new was created from the address the item carried, which is the whole point of a pin.
        Assert.Single(workspace.Contacts);
    }

    [Fact]
    public async Task A_second_call_under_the_same_key_answers_already_member_and_adds_nobody_twice()
    {
        using var workspace = new TestWorkspace();
        workspace.WithList(ProviderList, "Q3 prospects");
        await using var api = await StartAsync();

        var first = Succeeded(await InvokeAsync(api, workspace, "list_membership.add", ListAddInput(WorkItemId), Ct), "list_membership.add");
        var second = Succeeded(await InvokeAsync(api, workspace, "list_membership.add", ListAddInput(WorkItemId), Ct), "list_membership.add");

        Assert.Equal("added", first["items"]!.AsArray()[0]!["status"]!.GetValue<string>());
        Assert.Equal("already_member", second["items"]!.AsArray()[0]!["status"]!.GetValue<string>());
        Assert.Single(workspace.MembersOf(ProviderList));
        Assert.Single(workspace.Contacts);

        // A first attempt has nothing to recover from, so the second call is a plain repeat that the provider's
        // own record of the key answers.
        Assert.Equal(["contact ensure", "list add", "contact ensure", "list add"], workspace.Calls);
    }

    [Fact]
    public async Task An_attempt_after_a_lost_answer_reads_the_ledger_first_and_leaves_one_effect_behind()
    {
        using var workspace = new TestWorkspace();
        workspace.WithList(ProviderList, "Q3 prospects").FailAfterEffect(WorkItemId);
        await using var api = await StartAsync();

        var lost = await InvokeAsync(api, workspace, "list_membership.add", ListAddInput(WorkItemId), Ct);

        // The provider acted and said nothing, which is ambiguous by definition; the operation declares a
        // recovery read, so the next attempt is allowed to run.
        var failed = Assert.IsType<InvocationOutcome.Failed>(lost.Outcome);
        Assert.Equal("provider_answer_lost", failed.Error.Code);
        Assert.Equal(FailureClass.Ambiguous, failed.Error.Class);
        Assert.Equal(RepeatAfterAmbiguous.AfterRecoveryRead, OperationCatalog.Find("list_membership.add")!.RepeatAfterAmbiguous);

        var retry = await InvokeAsync(api, workspace, "list_membership.add", ListAddInput(WorkItemId), Ct, attempt: 2);

        var item = Succeeded(retry, "list_membership.add")["items"]!.AsArray()[0]!;
        Assert.Equal("already_member", item["status"]!.GetValue<string>());
        Assert.Single(workspace.MembersOf(ProviderList));
        Assert.Single(workspace.Contacts);

        // What the account was asked, in order: the second attempt read the ledger and then stopped. A plugin
        // that wrote blind would leave the same state behind here, so the calls are the evidence, not the state.
        Assert.Equal(["contact ensure", "list add", "ledger get"], workspace.Calls);
    }

    [Fact]
    public async Task An_address_the_provider_suppresses_fails_permanently_rather_than_being_added()
    {
        using var workspace = new TestWorkspace();
        workspace.WithList(ProviderList, "Q3 prospects").WithSuppressed(Blocked);
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, workspace, "list_membership.add", ListAddInput(WorkItemId, address: Blocked), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("suppressed", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Empty(workspace.MembersOf(ProviderList));
    }

    [Fact]
    public async Task A_list_the_provider_does_not_hold_fails_with_the_code_the_contract_declares()
    {
        using var workspace = new TestWorkspace();
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, workspace, "list_membership.add", ListAddInput(WorkItemId), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("list_not_found", failed.Error.Code);
    }

    [Fact]
    public async Task An_enrollment_into_a_campaign_that_is_not_live_says_so()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Q3 outbound", "Draft");
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, workspace, "campaign.enroll", EnrollInput(WorkItemId), Ct);

        var answer = Succeeded(result, "campaign.enroll");
        Assert.False(answer["campaign_live"]!.GetValue<bool>());
        var item = answer["items"]!.AsArray()[0]!;
        Assert.Equal("enrolled", item["status"]!.GetValue<string>());

        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal(ProviderCampaign, outcome.ExternalIds!["campaign"]!.GetValue<string>());
        Assert.Equal(item["external_ids"]!["contact"]!.GetValue<string>(), outcome.ExternalIds["contact"]!.GetValue<string>());
        Assert.Equal([item["external_ids"]!["contact"]!.GetValue<string>()], workspace.EnrollmentsIn(ProviderCampaign));
    }

    [Fact]
    public async Task An_enrollment_into_a_live_campaign_reports_the_send_it_was()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Q3 outbound", "Active");
        await using var api = await StartAsync();

        var answer = Succeeded(await InvokeAsync(api, workspace, "campaign.enroll", EnrollInput(WorkItemId), Ct), "campaign.enroll");

        Assert.True(answer["campaign_live"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_collision_the_caller_refused_to_decide_about_fails_rather_than_being_decided()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Q3 outbound", "Draft");
        await using var api = await StartAsync();

        await InvokeAsync(api, workspace, "campaign.enroll", EnrollInput(WorkItemId), Ct);
        var refused = await InvokeAsync(api, workspace, "campaign.enroll", EnrollInput(SecondWorkItemId, collision: "refuse"), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(refused.Outcome);
        Assert.Equal("collision_refused", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Single(workspace.EnrollmentsIn(ProviderCampaign));
    }

    [Fact]
    public async Task An_enrollment_after_a_lost_answer_is_not_a_second_enrollment()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Q3 outbound", "Draft").FailAfterEffect(WorkItemId);
        await using var api = await StartAsync();

        var lost = await InvokeAsync(api, workspace, "campaign.enroll", EnrollInput(WorkItemId), Ct);
        var retry = await InvokeAsync(api, workspace, "campaign.enroll", EnrollInput(WorkItemId), Ct, attempt: 2);

        Assert.Equal("provider_answer_lost", Assert.IsType<InvocationOutcome.Failed>(lost.Outcome).Error.Code);
        var answer = Succeeded(retry, "campaign.enroll");
        Assert.Equal("already_enrolled", answer["items"]!.AsArray()[0]!["status"]!.GetValue<string>());
        Assert.False(answer["campaign_live"]!.GetValue<bool>());
        Assert.Single(workspace.EnrollmentsIn(ProviderCampaign));

        // Both reads the contract names, in the order it names them: what the prior run under this key decided,
        // and then whether the campaign is live — which is what says whether that decision was a send.
        Assert.Equal(["contact ensure", "campaign enroll", "ledger get", "campaign get"], workspace.Calls);
    }

    /// <summary>
    /// A call the provider's program refused before doing anything is not what `ambiguous` is for. The program
    /// distinguishes a call it could not make sense of from an exit after the effect had already landed, and a
    /// plugin that answers both the same way leaves a work item waiting on a recovery read for something that
    /// never happened.
    /// </summary>
    [Fact]
    public async Task A_call_the_provider_program_refused_before_acting_is_permanent_rather_than_ambiguous()
    {
        using var workspace = new TestWorkspace();
        await using var api = await StartAsync();

        // The binding names a directory holding no account at all, which the program answers as a usage error.
        var binding = new JsonObject { ["workspace"] = Path.Combine(workspace.Root, "no-such-account") };
        var result = await InvokeAsync(api, "campaign.get", CampaignGetInput(ProviderCampaign), TestPlugins.FakeProviderId, binding, Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal("provider_call_failed", failed.Error.Code);
    }

    [Fact]
    public async Task The_second_package_answers_the_two_operations_it_declares_and_refuses_the_third()
    {
        await using var api = await StartAsync(paths => TestPlugins.InstallOtherProvider(paths));

        var read = await InvokeAsync(api, "campaign.get", CampaignGetInput(ProviderCampaign), TestPlugins.OtherProviderId, null, Ct);
        var add = await InvokeAsync(api, "list_membership.add", ListAddInput(WorkItemId), TestPlugins.OtherProviderId, null, Ct);
        var enroll = await InvokeAsync(api, "campaign.enroll", EnrollInput(WorkItemId), TestPlugins.OtherProviderId, null, Ct);

        // It answers from its input alone: no capabilities, no account, nothing to call.
        Assert.Equal("other-provider", Succeeded(read, "campaign.get")["vendor"]!["answered_by"]!.GetValue<string>());
        var pinned = Succeeded(add, "list_membership.add")["items"]!.AsArray()[0]!["external_ids"]!["contact"]!.GetValue<string>();
        Assert.StartsWith("other-", pinned, StringComparison.Ordinal);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(enroll.Outcome);
        Assert.Equal(ProtocolCodes.PluginOperationUnsupported, failure.Code);
    }

    // -----------------------------------------------------------------------------------------------------
    // Composing the canonical input, and checking both halves against the published document
    // -----------------------------------------------------------------------------------------------------

    private static JsonObject CampaignGetInput(string providerCampaign) => new()
    {
        ["args"] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = providerCampaign } },
        ["campaign"] = Campaign(),
    };

    private static JsonObject ListAddInput(string key, string? pinnedContact = null, string address = Marta) => new()
    {
        ["args"] = new JsonObject
        {
            ["list"] = new JsonObject { ["external_id"] = ProviderList },
            ["channel"] = "email",
        },
        ["contacts"] = new JsonArray(Contact(pinnedContact, address)),
        ["campaign"] = Campaign(),
        ["idempotency_key"] = key,
    };

    private static JsonObject EnrollInput(string key, string collision = "skip", string? pinnedContact = null) => new()
    {
        ["args"] = new JsonObject
        {
            ["campaign"] = new JsonObject { ["external_id"] = ProviderCampaign },
            ["channel"] = "email",
            ["collision"] = collision,
            ["start"] = new JsonObject { ["position"] = "first_step" },
            ["first_touch"] = "authored_delay",
        },
        ["contacts"] = new JsonArray(Contact(pinnedContact, Marta)),
        ["campaign"] = Campaign(),
        ["idempotency_key"] = key,
    };

    /// <summary>The projection the contract declares: the neutral identity fields and the one channel consumed.</summary>
    private static JsonObject Contact(string? pinned, string address) => new()
    {
        ["id"] = ContactId,
        ["first_name"] = "Marta",
        ["last_name"] = "Alvarez",
        ["company"] = "Puerto Analytics",
        ["title"] = null,
        ["time_zone"] = "America/Bogota",
        ["channels"] = new JsonArray(new JsonObject
        {
            ["channel"] = "email",
            ["value"] = address,
            ["primary"] = true,
        }),
        ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
    };

    private static JsonObject Campaign(string? pinned = null) => new()
    {
        ["id"] = CampaignId,
        ["name"] = "Q3 outbound",
        ["status"] = "draft",
        ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["campaign"] = pinned },
    };

    /// <summary>The result the plugin answered with, having first been held to the operation's output schema.</summary>
    private static JsonObject Succeeded(PluginInvocationResult result, string operation)
    {
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        var contract = OperationCatalog.Find(operation)!;

        Assert.Empty(OutcomeContract.CheckResult(contract, outcome.Result));
        Assert.Empty(OutcomeContract.CheckExternalIds(contract, outcome.ExternalIds));
        return outcome.Result!.AsObject();
    }

    private static async Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api,
        TestWorkspace workspace,
        string operation,
        JsonObject input,
        CancellationToken kill,
        int attempt = 1) =>
        await InvokeAsync(
            api,
            operation,
            input,
            TestPlugins.FakeProviderId,
            new JsonObject { ["workspace"] = workspace.Root },
            kill,
            attempt);

    private static async Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api,
        string operation,
        JsonObject input,
        string plugin,
        JsonObject? binding,
        CancellationToken kill,
        int attempt = 1)
    {
        // The input is what the document says it is before anything runs: a test that sent nonsense would prove
        // nothing about whether the operation is implementable.
        Assert.Empty(SchemaValidator.Validate(input, OperationCatalog.Find(operation)!.InputSchema));

        return await PluginInvokerTests.InvokeAsync(
            api,
            new PluginInvocationRequest(
                plugin,
                operation,
                input,
                binding,
                "att_01K0" + operation.Replace('.', '_'),
                AttemptId: "att_01K0" + operation.Replace('.', '_'),
                AttemptNumber: attempt,
                WorkItemId: WorkItemId,
                CampaignId: CampaignId),
            kill);
    }

    private static Task<RuntimeApiFixture> StartAsync(Action<JasonPaths>? extra = null) =>
        PluginInvokerTests.StartAsync(paths =>
        {
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"], env: ["FAKE_TOKEN"]);
            extra?.Invoke(paths);
        });
}
