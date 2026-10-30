using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Operations;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Routing;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A direct send from end to end: a real runtime and its dispatcher, the official package in a real plugin-host child,
/// and the stand-in at the other end. A <c>message.send</c> is approved and goes; a <c>message.status</c> names it by its
/// work item, the claim composes <c>send</c> from the reference the send's attempt returned, the package reads it with
/// <c>reply direct-send get</c>, and the campaign hears the send went in the one reserved line. A
/// <c>message.cancel</c> of a send held for a later moment goes the same way and takes it back.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyDirectSendTests
{
    /// <summary>A real dispatcher that never ticks on its own: every scan here is one the test asked for.</summary>
    private const string Idle = """{"TickSeconds":3600}""";

    private const string Address = "marta@example.com";
    private const string Mailbox = "ann@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_send_is_read_and_another_taken_back_by_the_reference_the_runtime_kept()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(12, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await ReplyPlugins.StartAsync(
            Ct,
            paths =>
            {
                // The grant is in the settings file already: the dispatcher's section goes beside it, not over it.
                var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
                settings["Dispatcher"] = JsonNode.Parse(Idle);
                File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(ReplyPlugins.PluginId));
            });
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);

        // 1. A send that goes now, approved by a person, comes back with the reference Reply gave it.
        var now = await SendAsync(api, campaign, contact, Message());
        Assert.Equal("email-1001", Assert.Single(now.Attempts!).Provenance!.ExternalIdsReturned!["send"]);
        Assert.Equal("sent", (string?)now.Result!["items"]![0]!["status"]);

        // 2. A read names the send by its work item alone; the reference it reads by is the one the send's attempt kept.
        var status = await RunAsync(api, campaign, "message.status", new JsonObject { ["send"] = now.Id });
        Assert.Equal(WorkItemStatus.Succeeded, status.Status);
        Assert.Equal("sent", (string?)status.Result!["state"]);
        Assert.Contains("direct-send get email-1001", account.Calls.Select(call => call.Line));

        var observed = Assert.Single((await api.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList, new { campaign_id = campaign, kind = "message_sent_observed" }, Ct)).Items);
        Assert.Equal((now.Id, now.Id), (observed.Key, observed.WorkItemId));
        Assert.Equal(
            (ReplyPlugins.PluginId, "email-1001", "status", status.Id),
            ((string?)observed.New!["plugin"], (string?)observed.New["reference"], (string?)observed.New["observed_by"], (string?)observed.New["read_work_item"]));

        // 3. A send held for a later moment is taken back the same way: named by its work item, cancelled by its reference.
        var later = Message();
        later["send_after"] = "2099-11-03T09:00:00-05:00";
        var held = await SendAsync(api, campaign, contact, later);
        Assert.Equal("email-1002", Assert.Single(held.Attempts!).Provenance!.ExternalIdsReturned!["send"]);
        Assert.Equal("accepted", (string?)held.Result!["items"]![0]!["status"]);

        var cancel = await RunAsync(api, campaign, "message.cancel", new JsonObject { ["send"] = held.Id });
        Assert.Equal(WorkItemStatus.Succeeded, cancel.Status);
        Assert.Equal("cancelled", (string?)cancel.Result!["items"]![0]!["state"]);
        Assert.Contains("direct-send cancel email-1002", account.Calls.Select(call => call.Line));
        Assert.Equal(
            ["sent", "cancelled"],
            account.DirectSends.Select(send => (string?)send!["state"]));

        // The send taken back never went, so nothing more is said of it.
        Assert.Single((await api.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList, new { campaign_id = campaign, kind = "message_sent_observed" }, Ct)).Items);
    }

    /// <summary>
    /// A message written as a template, from end to end under the package's own manifest: it lists the channels a message
    /// goes on as a template, so the template is claimed, approved by a person and goes to Reply in Reply's syntax. The same
    /// package without that list, as every package before 0.14.0 is, writes no template anywhere, so the claim refuses the
    /// item <c>template_not_offered</c> before a person is asked or a child exists, and nothing reaches Reply.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_template_is_claimed_only_under_a_package_that_lists_its_channel(bool listed)
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(12, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await ReplyPlugins.StartAsync(
            Ct,
            paths =>
            {
                var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
                settings["Dispatcher"] = JsonNode.Parse(Idle);
                File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(ReplyPlugins.PluginId));
                if (!listed)
                {
                    var manifest = Path.Combine(paths.PluginPackageDirectory(ReplyPlugins.PluginId), "plugin.yaml");
                    var text = File.ReadAllText(manifest).ReplaceLineEndings("\n");
                    const string list = "templates:\n  message.send: [email, linkedin]\n";
                    Assert.Contains(list, text, StringComparison.Ordinal);
                    File.WriteAllText(manifest, text.Replace(list, string.Empty, StringComparison.Ordinal));
                }
            });
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        var template = Message();
        template["message"]!["compose"] = "template";
        template["message"]!["body"] = "Hi {{contact.first_name}}, thanks for the chat at the meetup.";

        if (listed)
        {
            var sent = await SendAsync(api, campaign, contact, template);
            Assert.Equal("sent", (string?)sent.Result!["items"]![0]!["status"]);
            Assert.Equal("Hi {{FirstName}}, thanks for the chat at the meetup.", (string?)Assert.Single(account.Sent)!["text"]!["body"]);
            return;
        }

        var item = await CreateAsync(api, campaign, "message.send", template, contact);
        await api.Resolve<ScanRunner>().ScanOnceAsync(Ct);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(60)));
        var refused = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item, include_snapshots = true }, Ct);

        Assert.Equal((WorkItemStatus.Failed, "template_not_offered"), (refused.Status, refused.LastError?.Code));
        Assert.Empty((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);
        Assert.DoesNotContain(account.Calls, call => call.Line == "contact send-email");
        Assert.Empty(account.Sent);
    }

    private static JsonObject Message() => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "12", ["address"] = Mailbox },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "The Lisbon meetup", ["body"] = "Hi Marta, thanks for the chat at the meetup." },
    };

    private static async Task<(string Campaign, string Contact)> WorkAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 LatAm founders" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        var contact = await api.PostOkAsync<ContactDto>(
            Operations.ContactCreate,
            new { first_name = "Marta", channels = new[] { new { channel = "email", value = Address } } },
            Ct);
        await api.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaign.Id, contacts = new[] { new { contact_id = contact.Id } } },
            Ct);
        return (campaign.Id, contact.Id);
    }

    /// <summary>A send, parked by the claim for a person, approved, and claimed again: the item as it ended.</summary>
    private static async Task<WorkItemDto> SendAsync(RuntimeApiFixture api, string campaign, string contact, JsonObject arguments)
    {
        var item = await CreateAsync(api, campaign, "message.send", arguments, contact);
        Assert.Equal(0, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);

        var approval = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);
        Assert.Equal(item, approval.WorkItemId);
        await api.PostOkAsync<ApprovalDto>(
            Operations.ApprovalApprove,
            new { approval_id = approval.Id, actor = new { type = "human", id = "ada" }, reason = "go ahead" },
            Ct);

        var sent = await ClaimAsync(api, item);
        Assert.Equal((WorkItemStatus.Succeeded, null), (sent.Status, sent.LastError?.Code));
        return sent;
    }

    /// <summary>An item no person is asked about, created and claimed: the item as it ended.</summary>
    private static async Task<WorkItemDto> RunAsync(RuntimeApiFixture api, string campaign, string operation, JsonObject arguments) =>
        await ClaimAsync(api, await CreateAsync(api, campaign, operation, arguments, contact: null));

    private static async Task<string> CreateAsync(RuntimeApiFixture api, string campaign, string operation, JsonObject arguments, string? contact)
    {
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "provider_op", operation, contact_id = contact, context = new JsonObject { ["input"] = arguments } },
            Ct);
        return item.Id;
    }

    private static async Task<WorkItemDto> ClaimAsync(RuntimeApiFixture api, string item)
    {
        Assert.Equal(1, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(60)));
        return await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item, include_snapshots = true }, Ct);
    }
}
