using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Json;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// The first half of the spine: how a machine with nothing on it becomes a machine that can perform provider
/// work, one visible step at a time. It is written as a test rather than as prose because a documented sequence
/// nobody executes is a sequence that stops being true; the walkthrough prints these commands, and this is what
/// says they still work.
/// </summary>
/// <remarks>
/// Two claims here are as important as the steps themselves. A standalone install knows no provider — the
/// runtime starts with no plugin, no route and no idea that Reply exists, and only explicit acts change that.
/// And the one act an operator will reach for and not find, a global <c>route set</c>, is refused with the
/// sentence that says where a global route does live: the refusal is part of the documented path, not an
/// accident of the CLI.
/// <para>
/// The account at the other end is the stand-in vendor CLI installed beside these tests, never a real Reply
/// account; the package resolves to it by name, and the test asserts the resolved program lies inside the test
/// tree before it lets anything run.
/// </para>
/// </remarks>
public class BootstrapTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fresh_machine_is_brought_to_a_routed_reply_plugin_one_visible_step_at_a_time()
    {
        using var it = GoldenPath.Create("bootstrap");

        // 1. An installation that knows nothing about any provider: no route in the file at all.
        await GoldenPath.WriteSettingsAsync(it, new SettingsShape(Routed: false));
        var descriptor = await GoldenPath.StartAsync(it);

        var status = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "runtime", "status")));
        Assert.Equal(descriptor.InstanceId, (string?)status["instance_id"]);
        Assert.Equal(descriptor.Pid, (int?)status["pid"]);

        // 2. A standalone install implies nothing: no plugin is loaded and nothing is routed anywhere.
        var bare = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "plugin", "list")));
        Assert.Empty(bare["plugins"]!.AsArray());
        var unrouted = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "route", "list")));
        Assert.Null(unrouted["global"]!["default"]);
        Assert.Empty(unrouted["global"]!["operations"]!.AsObject());

        // 3. Installing a plugin is copying its directory. There is no install verb, and none is needed.
        GoldenPath.InstallReplyPackage(it);

        // 4. The reload is the act that reads the package, recomputes its digest and swaps the snapshot.
        var loaded = await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: false);
        Assert.True((bool)loaded["activated"]!);
        var plugin = Assert.Single(loaded["plugins"]!.AsArray())!.AsObject();
        Assert.Equal(GoldenPath.PluginId, (string?)plugin["id"]);
        Assert.Equal("valid", (string?)plugin["status"]);
        Assert.Empty(plugin["problems"]!.AsArray());
        var digest = (string?)plugin["digest"];
        Assert.StartsWith("sha256:", digest, StringComparison.Ordinal);

        // Declaration is not permission: the package asked for one program by name, and the grant answered that
        // one. And the program it resolved to is the stand-in beside these tests, never whatever this machine
        // may have installed and signed in.
        Assert.Equal(GoldenPath.PluginId, (string?)plugin["capabilities"]!["exec"]!["requested"]!.AsArray()[0]!["name"]);
        Assert.Equal(
            [GoldenPath.PluginId],
            plugin["capabilities"]!["exec"]!["granted"]!.AsArray().Select(granted => (string?)granted));
        GoldenPath.AssertTheStandInAnswered(plugin);

        // A loaded package is not a routed one: the work still has nowhere to go.
        var unroutedSnapshot = (string?)loaded["routing_snapshot_id"];
        var stillUnrouted = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "route", "list")));
        Assert.Null(stillUnrouted["global"]!["default"]);

        // 5. The global route is written where an installer can write it before a runtime has ever run, and it
        //    becomes active by the same explicit act as the packages it names.
        await GoldenPath.WriteSettingsAsync(it, new SettingsShape(Routed: true));
        var routed = await GoldenPath.ReloadAsync(it, "routed provider work to the reply plugin", routed: true);
        Assert.True((bool)routed["activated"]!);
        Assert.Empty(routed["last_reload"]!["routes"]!.AsArray());
        var routedSnapshot = (string?)routed["routing_snapshot_id"];
        Assert.NotEqual(unroutedSnapshot, routedSnapshot);

        // 6. And now both halves answer: the route is there, and it is the one a campaign's work would take.
        var listed = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "route", "list")));
        Assert.Equal(GoldenPath.PluginId, (string?)listed["global"]!["default"]!["plugin_id"]);
        Assert.Equal(it.Account.Profile, (string?)listed["global"]!["default"]!["binding"]!["profile"]);

        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "create", "--name", "Autumn outreach")))["id"]!;
        var resolution = GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "route", "resolve", "--campaign", campaign, "--operation", "campaign.enroll")));
        Assert.Equal(GoldenPath.PluginId, (string?)resolution["plugin_id"]);
        Assert.Equal("global_default", (string?)resolution["scope"]);
        Assert.True((bool)resolution["usable"]!);
        Assert.Empty(resolution["problems"]!.AsArray());
        Assert.Equal(routedSnapshot, (string?)resolution["routing_snapshot_id"]);

        // A route names an account by identity; the value itself stays in the route and never travels with what
        // it explains.
        Assert.NotNull((string?)resolution["binding_identity"]);
        Assert.DoesNotContain(it.Account.Root, resolution.ToJsonString(JasonJson.Options), StringComparison.OrdinalIgnoreCase);

        // 7. The one step an operator will reach for and not find. The refusal is documentation: it says where a
        //    global route lives and what makes it active, before anything is sent to the runtime.
        var refused = await GoldenPath.JasonAsync(it, "route", "set", "--plugin", GoldenPath.PluginId);
        Assert.Equal(ExitCodes.Usage, refused.ExitCode);
        Assert.Contains("settings.json", refused.Error, StringComparison.Ordinal);
        Assert.Contains("jason plugin reload", refused.Error, StringComparison.Ordinal);
        Assert.Empty(refused.Output);

        await GoldenPath.StopAsync(it);

        // Nothing was asked of the provider by any of this: a bootstrap configures, it does not act.
        Assert.Empty(it.Account.Calls);
    }
}
