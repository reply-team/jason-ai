using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The stand-in vendor CLI's workspace subcommands, driven directly. A plugin reaches these through
/// <c>host.exec</c>, but what they do to the account has to be settled before any JavaScript is in the way:
/// otherwise a failing canonical-operation test cannot say whether the plugin or its provider was wrong.
/// </summary>
public class FakeProviderWorkspaceTests
{
    private const string Email = "email";
    private const string Marta = "marta@example.test";
    private const string Blocked = "blocked@example.test";
    private const string Key = "wi_01K0WORKSPACE00000000000AA";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_person_the_provider_does_not_hold_yet_is_created_from_the_address()
    {
        using var workspace = new TestWorkspace();

        var (exitCode, answer, _) = await workspace.RunAsync(
            ["contact", "ensure"],
            new JsonObject { ["channel"] = Email, ["value"] = Marta },
            Ct);

        Assert.Equal(0, exitCode);
        Assert.True(answer!["created"]!.GetValue<bool>());
        var id = answer["id"]!.GetValue<string>();
        Assert.Equal(Marta, workspace.Contacts[id]!["value"]!.GetValue<string>());
        Assert.Equal(["by_channel"], workspace.AsksFor(id));
    }

    [Fact]
    public async Task A_person_the_caller_names_by_identifier_is_never_looked_up_by_address()
    {
        using var workspace = new TestWorkspace();
        workspace.WithContact("p_1001", Email, Marta);

        var (_, answer, _) = await workspace.RunAsync(
            ["contact", "ensure"],
            new JsonObject { ["channel"] = Email, ["value"] = "someone.else@example.test", ["external_id"] = "p_1001" },
            Ct);

        Assert.Equal("p_1001", answer!["id"]!.GetValue<string>());
        Assert.False(answer["created"]!.GetValue<bool>());

        // The record of how it was asked is the evidence: the address in the request was never matched against.
        Assert.Equal(["by_id"], workspace.AsksFor("p_1001"));
        Assert.Single(workspace.Contacts);
    }

    [Fact]
    public async Task An_address_the_provider_suppresses_is_refused_rather_than_created()
    {
        using var workspace = new TestWorkspace();
        workspace.WithSuppressed(Blocked);

        var (exitCode, answer, _) = await workspace.RunAsync(
            ["contact", "ensure"],
            new JsonObject { ["channel"] = Email, ["value"] = Blocked },
            Ct);

        Assert.Equal(0, exitCode);
        Assert.Equal("suppressed", answer!["error"]!["code"]!.GetValue<string>());
        Assert.Empty(workspace.Contacts);
    }

    [Fact]
    public async Task An_add_happens_once_and_a_repeat_under_the_same_key_is_answered_from_the_ledger()
    {
        using var workspace = new TestWorkspace();
        workspace.WithContact("p_1001", Email, Marta).WithList("lst_7", "Q3 prospects");

        var first = await AddAsync(workspace, "lst_7", "p_1001", Key);
        var second = await AddAsync(workspace, "lst_7", "p_1001", Key);

        Assert.Equal("added", first!["status"]!.GetValue<string>());
        Assert.Equal("already_member", second!["status"]!.GetValue<string>());
        Assert.True(second["from_ledger"]!.GetValue<bool>());
        Assert.Equal(["p_1001"], workspace.MembersOf("lst_7"));
        Assert.Equal("added", workspace.Ledger[Key]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_list_the_provider_does_not_hold_is_named_in_the_refusal()
    {
        using var workspace = new TestWorkspace();
        workspace.WithContact("p_1001", Email, Marta);

        var answer = await AddAsync(workspace, "lst_missing", "p_1001", Key);

        Assert.Equal("list_not_found", answer!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_forced_crash_writes_the_effect_and_then_says_nothing()
    {
        using var workspace = new TestWorkspace();
        workspace.WithContact("p_1001", Email, Marta).WithList("lst_7", "Q3 prospects").FailAfterEffect(Key);

        var (exitCode, answer, _) = await workspace.RunAsync(
            ["list", "add"],
            new JsonObject { ["list_id"] = "lst_7", ["contact_id"] = "p_1001", ["key"] = Key },
            Ct);

        Assert.NotEqual(0, exitCode);
        Assert.Null(answer);

        // This is exactly the state a recovery read exists to discover: the effect is there, the answer is not.
        Assert.Equal(["p_1001"], workspace.MembersOf("lst_7"));
        Assert.Equal("added", workspace.Ledger[Key]!["status"]!.GetValue<string>());

        // The instruction was spent, so the next attempt runs normally.
        var (retry, _, _) = await workspace.RunAsync(["ledger", "get"], new JsonObject { ["key"] = Key }, Ct);
        Assert.Equal(0, retry);
    }

    [Fact]
    public async Task The_ledger_answers_what_a_key_already_did()
    {
        using var workspace = new TestWorkspace();
        workspace.WithContact("p_1001", Email, Marta).WithList("lst_7", "Q3 prospects");
        await AddAsync(workspace, "lst_7", "p_1001", Key);

        var (_, found, _) = await workspace.RunAsync(["ledger", "get"], new JsonObject { ["key"] = Key }, Ct);
        var (_, missing, _) = await workspace.RunAsync(["ledger", "get"], new JsonObject { ["key"] = "wi_01K0NOTHING0000000000000AA" }, Ct);

        Assert.True(found!["found"]!.GetValue<bool>());
        Assert.Equal("list_membership.add", found["entry"]!["operation"]!.GetValue<string>());
        Assert.False(missing!["found"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_campaign_is_read_with_the_provider_s_own_word_for_its_state()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign("cmp_42", "Q3 outbound", "Draft", "p_1001");

        var (_, answer, _) = await workspace.RunAsync(["campaign", "get"], new JsonObject { ["external_id"] = "cmp_42" }, Ct);
        var (_, missing, _) = await workspace.RunAsync(["campaign", "get"], new JsonObject { ["external_id"] = "cmp_none" }, Ct);

        Assert.Equal("Q3 outbound", answer!["name"]!.GetValue<string>());
        Assert.Equal("Draft", answer["status"]!.GetValue<string>());
        Assert.False(answer["live"]!.GetValue<bool>());
        Assert.Equal(1, answer["counts"]!["enrolled"]!.GetValue<int>());
        Assert.Equal("Draft", answer["vendor"]!["state"]!.GetValue<string>());
        Assert.Equal("campaign_not_found", missing!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_enrollment_says_whether_the_campaign_was_live_and_refuses_a_collision_when_asked_to()
    {
        using var workspace = new TestWorkspace();
        workspace.WithContact("p_1001", Email, Marta).WithCampaign("cmp_42", "Q3 outbound", "Draft");

        var first = await EnrollAsync(workspace, "cmp_42", "p_1001", Key, "skip");
        var refused = await EnrollAsync(workspace, "cmp_42", "p_1001", "wi_01K0SECONDKEY0000000000AA", "refuse");

        Assert.Equal("enrolled", first!["status"]!.GetValue<string>());
        Assert.False(first["live"]!.GetValue<bool>());
        Assert.Equal("collision_refused", refused!["error"]!["code"]!.GetValue<string>());
        Assert.Equal(["p_1001"], workspace.EnrollmentsIn("cmp_42"));
    }

    [Fact]
    public async Task An_archived_campaign_takes_no_enrollment_at_all()
    {
        using var workspace = new TestWorkspace();
        workspace.WithContact("p_1001", Email, Marta).WithCampaign("cmp_42", "Q3 outbound", "Archived");

        var answer = await EnrollAsync(workspace, "cmp_42", "p_1001", Key, "skip");

        Assert.Equal("campaign_not_enrollable", answer!["error"]!["code"]!.GetValue<string>());
        Assert.Empty(workspace.EnrollmentsIn("cmp_42"));
    }

    [Fact]
    public async Task A_subcommand_the_program_does_not_know_is_a_usage_error()
    {
        using var workspace = new TestWorkspace();

        var (exitCode, _, stderr) = await workspace.RunAsync(["contact", "delete"], new JsonObject(), Ct);

        Assert.Equal(2, exitCode);
        Assert.NotEmpty(stderr);
    }

    private static async Task<JsonObject?> AddAsync(TestWorkspace workspace, string list, string contact, string key)
    {
        var (_, answer, _) = await workspace.RunAsync(
            ["list", "add"],
            new JsonObject { ["list_id"] = list, ["contact_id"] = contact, ["key"] = key },
            Ct);
        return answer;
    }

    private static async Task<JsonObject?> EnrollAsync(TestWorkspace workspace, string campaign, string contact, string key, string collision)
    {
        var (_, answer, _) = await workspace.RunAsync(
            ["campaign", "enroll"],
            new JsonObject
            {
                ["campaign_id"] = campaign,
                ["contact_id"] = contact,
                ["key"] = key,
                ["collision"] = collision,
                ["start"] = "first_step",
                ["first_touch"] = "authored_delay",
            },
            Ct);
        return answer;
    }
}
