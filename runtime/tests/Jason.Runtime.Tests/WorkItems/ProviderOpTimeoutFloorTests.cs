using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// The same floor the configuration is held to, per item. A provider attempt's child runs for the operation's
/// own budget, so an item that asks for a lease shorter than that budget could only ever end ambiguously —
/// and the runtime already knows it. A planner is told to repair the number rather than having it raised
/// behind their back: a lease silently changed is a lease the planner believes and the runtime does not hold.
/// </summary>
public class ProviderOpTimeoutFloorTests
{
    /// <summary>300 000 ms of `campaign.enroll` plus the 5 000 ms a child is given to stop, rounded up.</summary>
    private const int EnrolmentFloor = 305;

    /// <summary>60 000 ms of `campaign.get` plus the same grace.</summary>
    private const int ReadFloor = 65;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(Operations.WorkItemCreate)]
    [InlineData(Operations.WorkItemUpdate)]
    public async Task A_provider_item_may_not_ask_for_less_time_than_its_operation_needs(string operation)
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var error = await RefusedAsync(api, operation, campaign, "campaign.enroll", ToEnrol(), timeoutSeconds: 30);

        var detail = Assert.Single(error.Details!);
        Assert.Equal("timeout_seconds", detail.Field);
        Assert.Contains("campaign.enroll", detail.Message, StringComparison.Ordinal);
        Assert.Contains("305", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The floor is the operation's, not one number for every provider item: a read that takes a minute and an
    /// enrolment that takes five minutes are different promises, and a lease is measured against the one it has
    /// to keep.
    /// </summary>
    [Theory]
    [InlineData(Operations.WorkItemCreate)]
    [InlineData(Operations.WorkItemUpdate)]
    public async Task Each_operation_carries_its_own_floor(string operation)
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var error = await RefusedAsync(api, operation, campaign, "campaign.get", ToRead(), timeoutSeconds: ReadFloor - 1);

        var detail = Assert.Single(error.Details!);
        Assert.Contains("campaign.get", detail.Message, StringComparison.Ordinal);
        Assert.Contains("65", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>A lease that exactly covers the operation is a lease that covers it: the floor is inclusive.</summary>
    [Fact]
    public async Task A_lease_that_covers_the_operation_is_accepted_at_create_and_at_update()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var created = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            Create(campaign, "campaign.enroll", ToEnrol(), EnrolmentFloor),
            Ct);

        Assert.Equal(EnrolmentFloor, created.TimeoutSeconds);

        var updated = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemUpdate,
            new { work_item_id = created.Id, timeout_seconds = EnrolmentFloor + 1 },
            Ct);

        Assert.Equal(EnrolmentFloor + 1, updated.TimeoutSeconds);
    }

    /// <summary>
    /// An item that names no lease of its own takes the kind's default, and the default is the one the start-up
    /// validator already holds to the same floor. Clearing an override is therefore always allowed.
    /// </summary>
    [Fact]
    public async Task An_item_that_names_no_lease_of_its_own_is_covered_by_the_default()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var created = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, Create(campaign, "campaign.enroll", ToEnrol(), null), Ct);
        Assert.Null(created.TimeoutSeconds);

        var raised = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemUpdate,
            new { work_item_id = created.Id, timeout_seconds = EnrolmentFloor },
            Ct);
        Assert.Equal(EnrolmentFloor, raised.TimeoutSeconds);

        var cleared = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemUpdate,
            new { work_item_id = created.Id, timeout_seconds = (int?)null },
            Ct);
        Assert.Null(cleared.TimeoutSeconds);
    }

    /// <summary>The grace a child is given to stop is part of the floor, so configuring more moves it.</summary>
    [Fact]
    public async Task A_longer_grace_for_stopping_a_child_raises_the_floor_with_it()
    {
        await using var api = await StartAsync("""{"Dispatcher":{"Enabled":false},"Plugins":{"Invoker":{"KillGraceMs":10000}}}""");
        var campaign = await CampaignAsync(api);

        var error = await RefusedAsync(api, Operations.WorkItemCreate, campaign, "campaign.get", ToRead(), timeoutSeconds: ReadFloor);

        Assert.Contains("70", Assert.Single(error.Details!).Message, StringComparison.Ordinal);
    }

    /// <summary>An agent item is measured by the range alone: no operation, no contract, no floor.</summary>
    [Fact]
    public async Task Agent_work_keeps_the_lease_it_asks_for()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "ai_role", role = "researcher", timeout_seconds = 30 },
            Ct);

        Assert.Equal(30, item.TimeoutSeconds);
    }

    private static Task<RuntimeApiFixture> StartAsync(string? settings = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, settings ?? RuntimeApiFixture.DispatcherOff));

    /// <summary>The same mistake made at create and made later by a patch, refused the same way both times.</summary>
    private static async Task<ErrorBody> RefusedAsync(
        RuntimeApiFixture api,
        string operation,
        string campaign,
        string providerOperation,
        JsonObject arguments,
        int timeoutSeconds)
    {
        if (operation == Operations.WorkItemCreate)
        {
            return await api.PostErrorAsync(operation, Create(campaign, providerOperation, arguments, timeoutSeconds), HttpStatusCode.BadRequest, Ct);
        }

        var item = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, Create(campaign, providerOperation, arguments, null), Ct);
        return await api.PostErrorAsync(operation, new { work_item_id = item.Id, timeout_seconds = timeoutSeconds }, HttpStatusCode.BadRequest, Ct);
    }

    private static object Create(string campaign, string operation, JsonObject arguments, int? timeoutSeconds) => new
    {
        campaign_id = campaign,
        kind = "provider_op",
        operation,
        context = new JsonObject { ["input"] = arguments },
        timeout_seconds = timeoutSeconds,
    };

    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Flow" }, Ct);
        return campaign.Id;
    }

    private static JsonObject ToRead() => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = "c-7714" },
    };

    private static JsonObject ToEnrol() => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = "c-7714" },
        ["channel"] = "email",
        ["collision"] = "skip",
        ["start"] = new JsonObject { ["position"] = "first_step" },
        ["first_touch"] = "immediately",
    };
}
