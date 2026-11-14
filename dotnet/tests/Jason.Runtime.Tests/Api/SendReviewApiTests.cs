using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Approvals;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// The critic the runtime creates for a send, through the runtime as it is composed: <c>workitem.create</c> over the API of
/// a started runtime, under an automatic mode a person set, answers with the send waiting on a critic the runtime made.
/// The service reads the roles' settings to know a critic could be launched and creates none without them, so only the
/// real composition proves the container hands them over.
/// </summary>
public class SendReviewApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object Ada = new { type = "human", id = "ada" };

    [Fact]
    public async Task A_send_created_over_the_api_under_an_automatic_mode_waits_on_the_critic_the_runtime_made()
    {
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Roles":{"DefaultEntryCommand":["agent-host"]}}"""));
        await api.PostOkAsync<JsonObject>(Operations.PreferenceSet, new { key = SendModes.ColdKey, value = new { mode = "automatic" }, actor = Ada }, Ct);
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Work" }, Ct)).Id;
        var contact = (await api.PostOkAsync<ContactDto>(Operations.ContactCreate, new { first_name = "Ada", channels = new[] { new { channel = "email", value = "ada@example.test" } } }, Ct)).Id;
        await api.PostOkAsync<AddContactsResult>(Operations.CampaignAddContacts, new { campaign_id = campaign, contacts = new[] { new { contact_id = contact } }, actor = Ada }, Ct);

        var send = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation = "message.send",
                contact_id = contact,
                context = new
                {
                    input = new JsonObject
                    {
                        ["channel"] = "email",
                        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
                        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = "Thursday at 10 works." },
                    },
                },
                actor = Ada,
            },
            Ct);

        Assert.Equal(WorkItemStatus.Waiting, send.Status);
        var on = Assert.Single(send.Wait!.On);
        var critic = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = on.WorkItemId }, Ct);
        Assert.Equal(
            (WorkItemKind.AiRole, "critic", ActorType.System, "runtime", contact),
            (critic.Kind, critic.Role, critic.CreatedBy.Type, critic.CreatedBy.Id, critic.ContactId));
        Assert.Equal(
            new JsonObject { ["send_review"] = new JsonObject { ["work_item_id"] = send.Id, ["operation"] = "message.send" } }.ToJsonString(),
            critic.Context.ToJsonString());
    }
}
