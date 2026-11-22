using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static Jason.Runtime.Tests.Dispatch.InboundSyncHarvestTests;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// The hold leaves a letter already on its way alone, and takes back only what the provider has already taken. A letter
/// claimed before the hold whose attempt succeeds after it, with the provider holding it for a later moment, is taken back
/// where the attempt ends: once its success is saved, the person being held in the campaign by then, one cancel names it.
/// </summary>
public partial class HeldSendsCancelTests
{
    private static OperationContract Send => OperationCatalog.Find("message.send")!;

    /// <summary>
    /// A letter on its way when the reply holds the person, which the provider then takes for tomorrow: exactly one cancel
    /// names it once its attempt has succeeded. The same letter to a person nobody held is left to go.
    /// </summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task A_letter_on_its_way_at_the_hold_is_taken_back_when_it_succeeds(bool replied, int cancels)
    {
        var reference = "send-" + PublicId.New("ref")[^8..];
        string? addressed = null;
        var command = new FakeCommand(WorkItemKind.ProviderOp, _ => Task.FromResult<CommandOutcome>(new CommandOutcome.Provider(Accepted(reference, addressed!), null)));
        using var harness = new DispatchHarness(Noon, commands: command);
        Load(harness, TestPlugins.Loaded(Mailer, operations: ["message.send", "message.cancel", "message.status", "conversation.list", Responder.ReadOperation]));
        RouteAll(harness, Mailer);
        var (campaign, person) = await SceneAsync(harness);
        addressed = (await PublicIdsAsync(harness, person))[0];
        var work = await OnItsWayAsync(harness, campaign, person);
        if (replied)
        {
            await ReplyLineAsync(harness, campaign, person, "th-1", Noon.AddMinutes(-5));
            await harness.RespondAsync(Ct);
            await using var before = harness.Open();
            Assert.Equal(MembershipState.Paused, await StateOfAsync(before, campaign, person));
            Assert.False(await before.WorkItems.AnyAsync(w => w.Operation == HeldSends.Cancel, Ct));
        }

        await AttemptHandler.RunAsync(harness.Scopes, work, harness.Registry, NullLogger.Instance);

        await using var db = harness.Open();
        Assert.Equal(WorkItemStatus.Succeeded, await db.WorkItems.Where(w => w.Id == work.WorkItemId).Select(w => w.Status).SingleAsync(Ct));
        var queued = await db.WorkItems.AsNoTracking().Where(w => w.Operation == HeldSends.Cancel).ToListAsync(Ct);
        Assert.Equal(cancels, queued.Count);
        Assert.All(queued, cancel =>
        {
            Assert.Equal(work.WorkItemPublicId, (string?)cancel.Context["input"]!["send"]);
            Assert.Equal((ActorType.System, "dispatcher", (int?)null), (cancel.CreatedByType, cancel.CreatedById, cancel.ContactId));
        });
    }

    /// <summary>A letter of the campaign to the person, for a moment two days out, claimed and about to run at <see cref="Mailer"/>.</summary>
    private static async Task<ClaimedWork> OnItsWayAsync(DispatchHarness harness, int campaign, int person)
    {
        await using var db = harness.Open();
        var owner = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        var item = WorkItemFactory.NewProviderOp(owner, Send.Id, Noon.AddHours(-1));
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "owner@example.test" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "A quiet word", ["body"] = "Ines, the numbers are in." },
                ["send_after"] = Later,
            },
        };
        item.ContactId = person;
        item.Status = WorkItemStatus.Scheduled;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Scheduled, Noon.AddMinutes(-6));
        attempt.Provenance = new AttemptProvenanceDto(
            Mailer, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            Send.Id, 1, null, null, RouteScope.CampaignDefault, RoutedIdentity);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        var input = new JsonObject
        {
            ["args"] = item.Context[WorkItemService.InputKey]!.DeepClone(),
            ["campaign"] = new JsonObject { ["id"] = owner.PublicId },
            ["idempotency_key"] = item.PublicId,
        };
        var plan = new ProviderOpPlan(
            TestPlugins.Loaded(Mailer, [Send.Id]),
            PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNA",
            RouteSnapshot.IdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNB",
            RouteScope.CampaignDefault,
            null,
            RoutedIdentity,
            Send,
            input);
        return new ClaimedWork(item.Id, attempt.Id, item.PublicId, attempt.PublicId, owner.PublicId, plan);
    }

    /// <summary>The provider took the letter, to send two days out, and says it can still cancel it by its reference.</summary>
    private static PluginInvocationResult Accepted(string reference, string person) => new(
        new InvocationOutcome.Succeeded(
            new JsonObject
            {
                ["items"] = new JsonArray(new JsonObject
                {
                    ["contact_id"] = person,
                    ["status"] = "accepted",
                    ["accepted_for"] = Later,
                    ["reference"] = reference,
                    ["cancelable"] = true,
                    ["channel"] = "email",
                    ["format"] = "email",
                    ["sender_external_id"] = "snd-1",
                    ["message"] = new JsonObject { ["external_id"] = null, ["sent_at"] = null },
                    ["external_ids"] = new JsonObject { ["contact"] = "pc-1", ["send"] = reference },
                }),
            },
            new JsonObject { ["contact"] = "pc-1", ["send"] = reference },
            new OutcomeDiagnostics(12, 1, 0, 1)),
        new InvocationProvenance(
            Mailer,
            "1.0.0",
            "sha256:0",
            PluginProtocol.CurrentVersion,
            PluginProtocol.OperationContractVersion,
            PluginProtocol.InvocationIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC",
            "att_01K5B7Q2WE5X3M9T0YH4C6RDND",
            PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNE"),
        Launch: null);
}
