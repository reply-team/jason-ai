using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Tests.Plugins.Reply;
using Jason.Runtime.Tests.Routing;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// What the Accounts card reads when the plugin is the official Reply package: its real manifest as <c>plugin.list</c>
/// lists it, the global route to it, and the accounts <c>sender.list</c> answers by running the package's own code in
/// the plugin host against the Reply test harness's fake command-line tool, with invented accounts. No network and no
/// real account is reached (<see cref="ReplyPlugins.StartAsync"/> holds the resolved program to the test tree). The
/// older <c>card-accounts</c> pair, recorded in <see cref="CardExchangesTests"/>, holds a bare test plugin instead.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class CardAccountsReplyExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_accounts_of_the_reply_package_are_listed_with_its_own_manifest_and_its_senders()
    {
        using var account = new ReplyAccount()
            .WithSender(12, "email", "Owner Example", "owner@example.test")
            .WithSender(13, "email", "Colleague Example", "colleague@example.test", owner: 502);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);

        // The route is in the settings the runtime starts with, so the plugin list and the clock is fixed, so the list's own load moment is the same on every run.
        await using var api = await ReplyPlugins.StartAsync(Ct, prepare: paths => TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(ReplyPlugins.PluginId)), clock: new FixedClock(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero)));

        var plugins = await api.RecordAsync<JsonObject>(Operations.PluginList, "card-accounts-reply", new { }, Ct);
        var listed = Assert.Single(plugins["plugins"]!.AsArray())!;
        Assert.Equal(ReplyPlugins.PluginId, (string?)listed["id"]);
        Assert.NotNull(listed["name"]);

        var routes = await api.RecordAsync<JsonObject>(Operations.RouteList, "card-accounts-reply", new { }, Ct);
        Assert.Equal(ReplyPlugins.PluginId, (string?)routes["global"]!["default"]!["plugin_id"]);

        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring launch" }, Ct)).Id;
        var senders = await api.RecordAsync<SenderListResponse>(
            Operations.SenderList, "card-accounts-reply", new { campaign_id = campaign, owners = "team", sender_channel = "email", limit = 100 }, Ct);
        Assert.Equal(["12", "13"], senders.Items.Select(item => (string?)item!["external_id"]));
    }
}
