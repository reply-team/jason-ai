using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// The desktop's chat marker as the runtime records it. The CLI sends <c>Jason-Confined</c> on every request while
/// <c>JASON_CONFINED</c> is set; a work item created under it says <c>confined</c> on its own <c>workitem_created</c> line, and
/// an edit of an item's input under it marks the item once, by a <c>workitem_updated</c> line keyed <c>confined</c>. A
/// request without the header marks nothing, and nothing takes a mark back.
/// </summary>
public class ChatMarkerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string> FromTheChat = new() { ["Jason-Confined"] = "1" };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_send_created_under_the_marker_says_so_on_its_created_line(bool confined)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);

        var (status, body) = await api.PostAsync(Operations.WorkItemCreate, Send(campaign, "Thursday at 10 works."), confined ? FromTheChat : null, Ct);

        Assert.True(status == HttpStatusCode.OK, body);
        var item = (string)JsonNode.Parse(body)!["id"]!;
        var created = Assert.Single(await LinesAsync(api, item, "workitem_created"));
        Assert.Equal(confined ? "true" : null, created["new"]!["confined"]?.ToJsonString());
    }

    /// <summary>
    /// An edit of the input under the marker marks the send once; a second edit there writes no second mark, an edit outside
    /// the chat takes nothing back, and a patch that leaves the input as it was marks nothing.
    /// </summary>
    [Fact]
    public async Task An_edit_of_the_input_under_the_marker_marks_the_send_once()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);
        var item = (await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, Send(campaign, "Thursday at 10 works."), Ct)).Id;

        await EditAsync(api, item, priority: true, FromTheChat);
        Assert.Empty(await LinesAsync(api, item, "workitem_updated", "confined"));

        await EditAsync(api, item, "Friday at 10 works.", FromTheChat);
        await EditAsync(api, item, "Friday at 11 works.", FromTheChat);
        await EditAsync(api, item, "Friday at noon works.", headers: null);

        var marked = Assert.Single(await LinesAsync(api, item, "workitem_updated", "confined"));
        Assert.Equal(("false", "true"), (marked["old"]!.ToJsonString(), marked["new"]!.ToJsonString()));
    }

    private static object Send(string campaign, string body) => new
    {
        CampaignId = campaign,
        Kind = "provider_op",
        Operation = "message.send",
        Context = new { input = Message(body) },
    };

    private static JsonObject Message(string body) => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = body },
    };

    private static async Task EditAsync(RuntimeApiFixture api, string item, string body, IReadOnlyDictionary<string, string>? headers)
    {
        var (status, text) = await api.PostAsync(
            Operations.WorkItemUpdate,
            new { WorkItemId = item, Set = new JsonObject { ["input"] = Message(body) }, Actor = new { type = "human", id = "ada" } },
            headers,
            Ct);
        Assert.True(status == HttpStatusCode.OK, text);
    }

    private static async Task EditAsync(RuntimeApiFixture api, string item, bool priority, IReadOnlyDictionary<string, string>? headers)
    {
        var (status, text) = await api.PostAsync(
            Operations.WorkItemUpdate,
            new { WorkItemId = item, Priority = priority ? 5 : 0, Actor = new { type = "human", id = "ada" } },
            headers,
            Ct);
        Assert.True(status == HttpStatusCode.OK, text);
    }

    private static async Task<List<JsonNode>> LinesAsync(RuntimeApiFixture api, string item, string kind, string? key = null)
    {
        var (status, text) = await api.PostAsync(Operations.JournalList, new { WorkItemId = item, Kind = kind, Limit = 100 }, Ct);
        Assert.True(status == HttpStatusCode.OK, text);
        return [.. JsonNode.Parse(text)!["items"]!.AsArray().Select(line => line!).Where(line => key is null || (string?)line["key"] == key)];
    }

    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Work" }, Ct);
        return campaign.Id;
    }
}
