using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A send the provider does not offer yet. A route Reply's API does not have answers 404 with no code of Reply's own,
/// which reply-cli passes through on a direct send as it is: <c>capability_not_offered</c>, permanent, its message naming
/// the operation and quoting nothing of Reply's sentence, and the work item it ends is failed like any other permanent
/// failure.
/// </summary>
public partial class ReplyMessageSendTests
{
    private static readonly DateTime Noon = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_route_reply_s_api_does_not_have_is_a_capability_not_offered()
    {
        const string detail = "No endpoint matches POST /v3/contacts/1001/send-direct-email.";
        using var account = ByEmail();
        account.Refuses(
            "contact send-email", "reply.refused", status: 404, detail: detail,
            ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal((UnjudgedOutcomeCodes.CapabilityNotOffered, FailureClass.Permanent), (error.Code, error.Class));
        Assert.StartsWith("message.send: ", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(detail, error.Message, StringComparison.Ordinal);
        Assert.Null(error.Details!["provider_code"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The whole way, against an account whose Reply does not offer the direct email: one call, not repeated, the work item
    /// failed with the code and its class, and the journal's line for it saying so. Offered, the same send goes.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_send_reply_does_not_offer_yet_ends_the_work_item_failed_and_the_journal_says_why(bool offered)
    {
        using var account = ByEmail().Offering("send_direct_email", offered);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Marta", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = Address, IsPrimary = true });
        var item = WorkItemFactory.NewProviderOp(campaign, Operation, Noon);
        item.Contact = contact;
        item.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        attempt.Provenance = new AttemptProvenanceDto(
            ReplyPlugins.PluginId, "0.4.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            Operation, 1, PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC", "rts_01K5B7Q2WE5X3M9T0YH4C6RDND",
            RouteScope.GlobalDefault, "sha256:1", CorrelationId: attempt.PublicId);
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);

        var result = await InvokeAsync(api, Input("email", pinned: "1001"));
        var recorder = new ProviderOutcomeRecorder(
            new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Settings(o => o.RetryDelaySeconds = 0)),
            new ExternalIdStore(new JournalWriter(clock), clock),
            new SentFacts(new JournalWriter(clock)),
            TestOptions.PluginSettings(_ => { }));
        await recorder.RecordAsync(db, item, attempt, OperationCatalog.Find(Operation)!, ReplyPlugins.PluginId, ReplyPlugins.SettlesByKey(Operation), result, Ct);
        await db.SaveChangesAsync(Ct);

        Assert.Single(account.Calls, call => call.Line == "contact send-email");
        if (offered)
        {
            Assert.Equal((WorkItemStatus.Succeeded, AttemptStatus.Succeeded), (item.Status, attempt.Status));
            Assert.Single(account.Sent);
            return;
        }

        Assert.Equal((WorkItemStatus.Failed, AttemptStatus.Failed), (item.Status, attempt.Status));
        Assert.Equal((UnjudgedOutcomeCodes.CapabilityNotOffered, FailureClass.Permanent, false), (item.LastError!.Code, item.LastError.Class, item.LastError.Retriable));
        Assert.StartsWith("message.send: ", item.LastError.Message, StringComparison.Ordinal);
        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.WorkItemFailed, Ct);
        Assert.Equal((item.PublicId, UnjudgedOutcomeCodes.CapabilityNotOffered), (line.WorkItemId, (string?)line.New!["code"]));
        Assert.Empty(account.Sent);
    }
}
