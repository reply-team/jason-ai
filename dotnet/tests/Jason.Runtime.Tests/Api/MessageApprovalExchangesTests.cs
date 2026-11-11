using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// Two messages waiting on a person, one by email and one on LinkedIn, as <c>approval.list</c> and <c>approval.get</c>
/// answer them: the exchanges a consumer reads to say who a message goes to, which account it goes from and on which
/// channel. Each preview is the one the claim assembles, from the operation's own contract and the composed input.
/// </summary>
public class MessageApprovalExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Noon = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly OperationContract Send = OperationCatalog.Find("message.send")!;

    // Fixed ULID-shaped ids: the listing orders by id, so the email sorts before the LinkedIn message on every machine.
    private const string EmailApproval = "apr_01K7QR0000000000000000000A";
    private const string LinkedInApproval = "apr_01K7QR0000000000000000000B";

    [Fact]
    public async Task A_message_waiting_says_who_it_goes_to_from_which_account_and_on_which_channel()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        await SeedAsync(api);

        var pending = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "messages-pending", null, Ct);
        Assert.Equal(["Ines Duarte", "Noor Haddad"], pending.Items.Select(a => a.ContactName));
        Assert.All(pending.Items, a => Assert.Equal("message.send", a.Operation));

        var email = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "email-message", new { approval_id = EmailApproval }, Ct);
        var emailSeen = email.Preview["highlights"]!.AsObject();
        Assert.Equal("email", (string?)emailSeen["/args/channel"]);
        Assert.Equal("owner@example.test", (string?)emailSeen["/args/sender"]!["address"]);
        Assert.Equal("ines@example.test", (string?)emailSeen["/contacts/0/channels/0/value"]);

        var linkedIn = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "linkedin-message", new { approval_id = LinkedInApproval }, Ct);
        var linkedInSeen = linkedIn.Preview["highlights"]!.AsObject();
        Assert.Equal("linkedin", (string?)linkedInSeen["/args/channel"]);
        Assert.Equal("https://www.linkedin.com/in/owner-example", (string?)linkedInSeen["/args/sender"]!["address"]);
    }

    private static async Task SeedAsync(RuntimeApiFixture api)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var campaign = WorkItemFactory.NewCampaign("Spring launch", now: Noon);
        campaign.PublicId = "cmp_01K7QR00000000000000000001";
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);

        Add(db, campaign, EmailApproval, "Ines", "Duarte", "email", "ines@example.test",
            new JsonObject { ["external_id"] = "s_12", ["address"] = "owner@example.test" },
            new JsonObject { ["format"] = "email", ["subject"] = "Notes from the meetup", ["body"] = "Hi Ines, thanks for the talk at the meetup." });
        Add(db, campaign, LinkedInApproval, "Noor", "Haddad", "linkedin", "https://www.linkedin.com/in/noor-example",
            new JsonObject { ["external_id"] = "s_34", ["address"] = "https://www.linkedin.com/in/owner-example" },
            new JsonObject { ["format"] = "linkedin_message", ["body"] = "Thanks for connecting, Noor." });
        await db.SaveChangesAsync(Ct);
    }

    private static void Add(JasonDbContext db, Campaign campaign, string id, string first, string last, string channel, string value, JsonObject sender, JsonObject message)
    {
        var suffix = id[4..];
        var contact = new Contact { PublicId = "cnt_" + suffix, FirstName = first, LastName = last, Company = "Kavali", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = channel, Value = value, IsPrimary = true });
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.PublicId = "wi_" + suffix;
        item.Status = WorkItemStatus.AwaitingApproval;
        item.Contact = contact;
        db.WorkItems.Add(item);

        var input = new JsonObject
        {
            ["args"] = new JsonObject { ["channel"] = channel, ["sender"] = sender, ["message"] = message },
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = contact.PublicId,
                ["first_name"] = first,
                ["last_name"] = last,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = channel, ["value"] = value, ["primary"] = true }),
                ["external_ids"] = new JsonObject(),
            }),
            ["campaign"] = new JsonObject { ["id"] = campaign.PublicId, ["name"] = campaign.Name, ["status"] = "active", ["external_ids"] = new JsonObject { ["campaign"] = "c_7714" } },
        };
        var binding = new JsonObject { ["workspace"] = "a" };
        var subject = ApprovalSubject.Of(
            new ProviderOpPlan(TestPlugins.Loaded("reply", ["message.send"]), "snp_01K7QR00000000000000000001", "rts_01K7QR00000000000000000001",
                RouteScope.GlobalDefault, binding, BindingIdentity.Of(binding), Send, input),
            item);

        db.Approvals.Add(new Approval
        {
            PublicId = id,
            WorkItem = item,
            CampaignId = campaign.Id,
            Operation = Send.Id,
            OperationVersion = Send.Version,
            Subject = subject.Document,
            SubjectHash = subject.Hash,
            Preview = ApprovalPreview.Of(Send, subject, campaign, contact, channel),
            PluginId = "reply",
            BindingIdentity = BindingIdentity.Of(binding),
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_01K7QR00000000000000000001",
            RoutingSnapshotId = "rts_01K7QR00000000000000000001",
            Reason = ApprovalGate.ApprovalRequired,
            RequestedAt = Noon,
            Status = ApprovalStatus.Pending,
        });
    }
}
