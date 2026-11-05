using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Subscriptions;
using Jason.Runtime.Tests.Integration;
using Jason.Runtime.Tests.Plugins.Invocation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins.Subscriptions;

/// <summary>
/// The runner as the runtime composes it, with real children: two subscribers in the order the settings give, the second
/// handed the envelope the first returned, and a handler's own failure ending the chain in one line that carries its
/// class and code and never its words.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class SubscriptionChildTests
{
    private const string Failed = "workitem_failed";

    private const string Words = "alpha words that stay with alpha";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_subscribers_run_in_the_order_set_each_handed_what_the_one_before_returned_and_a_failure_is_said_once()
    {
        await using var api = await PluginInvokerTests.StartAsync(
            Install,
            settings: """{"Dispatcher":{"Enabled":false},"Subscriptions":{"Order":{"workitem_failed":["beta","alpha"]}}}""");
        var handed = Line(api.Paths, key: null);
        var refused = Line(api.Paths, key: "refuse");

        var turn = await api.Resolve<SubscriptionRunner>().RunNextAsync(Failed, Ct);

        Assert.Equal(new SubscriptionTurn(Failed, 2, 0, SubscriptionTurn.Head), turn);
        using var scope = api.Runtime.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var runs = await db.SubscriptionRuns.AsNoTracking().OrderBy(run => run.Id).ToListAsync(Ct);
        Assert.Equal([SubscriptionRunStatus.Succeeded, SubscriptionRunStatus.Failed], runs.Select(run => run.Status));
        Assert.Equal($$"""{"beta":"{{handed}}","alpha":"{{handed}} after beta"}""", runs[0].Envelope!.ToJsonString());
        Assert.Equal(("alpha", "permanent", "alpha_refused"), (runs[1].FailedPluginId, runs[1].FailureClass, runs[1].FailureCode));

        var said = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.SubscriptionFailed, Ct);
        Assert.Equal(
            $$"""{"kind":"{{Failed}}","line":"{{refused}}","plugin_id":"alpha","class":"permanent","code":"alpha_refused","attempts":1}""",
            said.New!.ToJsonString());
        Assert.DoesNotContain(Words, said.New!.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Words, runs[1].Handlers.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>A line written straight into the journal after the load placed the kind's position: its public id.</summary>
    private static string Line(JasonPaths paths, string? key)
    {
        var id = PublicId.New("jrn");
        using var connection = new SqliteConnection($"Data Source={paths.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, key) VALUES ($id, $ts, 'system', 'runtime', $kind, $key)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", Failed);
        command.Parameters.AddWithValue("$key", (object?)key ?? DBNull.Value);
        command.ExecuteNonQuery();
        return id;
    }

    private static void Install(JasonPaths paths)
    {
        Subscriber(
            paths,
            "beta",
            "export function invoke(operation, input) { return { result: { envelope: { beta: input.line.id } } }; }");
        Subscriber(
            paths,
            "alpha",
            "export function invoke(operation, input) {\n"
            + $"  if (input.line.key === 'refuse') throw host.fail({{ class: 'permanent', code: 'alpha_refused', message: '{Words}' }});\n"
            + "  const envelope = input.envelope; envelope.alpha = envelope.beta + ' after beta'; return { result: { envelope: envelope } };\n"
            + "}");

        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        foreach (var id in new[] { "alpha", "beta" })
        {
            settings["Plugins"]!["Grants"]![id]!["Subscriptions"] = new JsonArray(Failed);
        }

        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }

    private static void Subscriber(JasonPaths paths, string id, string main)
    {
        TestPlugins.Write(paths, id, $"manifest_version: 1\nid: {id}\nversion: 1.0.0\nkind: subscriber\ncontracts:\n  protocol: [1]\nsubscriptions: [{Failed}]\n", main);
        TestPlugins.Grant(paths, id);
    }
}
