using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Audiences;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A 404 with no code of Reply's own on a send. Reply's direct-send routes are on every account, so such an answer says
/// that Reply's API changed under this package version: <c>provider_call_failed</c>, permanent, with <c>reply.refused</c>
/// and the 404 in its details, its message quoting nothing of Reply's sentence, and nothing is sent; the work item it
/// ends is failed like any other permanent failure.
/// </summary>
public partial class ReplyMessageSendTests
{
    private static readonly DateTime Noon = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A send naming no moment and one naming a moment ahead end alike: a moment is never dropped.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("2099-11-03T14:00:00Z")]
    public async Task A_404_without_reply_s_code_on_a_send_is_a_provider_call_failed_and_nothing_is_sent(string? moment)
    {
        const string detail = "No endpoint matches POST /v3/contacts/1001/send-direct-email.";
        using var account = ByEmail();
        account.Refuses(
            "contact send-email", "reply.refused", status: 404, detail: detail,
            ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = moment is null ? Input("email", pinned: "1001") : WithMoment(Input("email", pinned: "1001"), moment);
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.DoesNotContain(detail, error.Message, StringComparison.Ordinal);
        Assert.Equal(("reply.refused", 404), ((string?)error.Details!["cli_code"], (int?)error.Details["status"]));
        Assert.Null(error.Details["provider_code"]);
        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Equal(moment, account.Calls[^1].Option("--send-after"));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The whole way, against an account whose Reply answers the direct email with a 404 that carries no code of its own:
    /// one call, not repeated, the work item failed with the code and its class, and the journal's line for it saying so.
    /// Answered, the same send goes.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_send_meeting_a_404_without_reply_s_code_ends_the_work_item_failed_and_the_journal_says_why(bool offered)
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
            new SearchLanding(new JournalWriter(clock), new ExternalIdStore(new JournalWriter(clock), clock), clock),
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
        Assert.Equal(("provider_call_failed", FailureClass.Permanent, false), (item.LastError!.Code, item.LastError.Class, item.LastError.Retriable));
        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.WorkItemFailed, Ct);
        Assert.Equal((item.PublicId, "provider_call_failed"), (line.WorkItemId, (string?)line.New!["code"]));
        Assert.Empty(account.Sent);
    }
}
