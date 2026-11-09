using System.IO.Compression;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// The round trip no unit test can give: an installation a real runtime wrote, exported by the shipped program while that
/// runtime is stopped, and imported into another data directory and another harness root, where the runtime the import
/// starts holds the same projects, campaigns, contacts and work items by id, and records each folder where this machine
/// holds it.
/// </summary>
/// <remarks>
/// The dispatcher is off in both installations, so a work item is what it was when it was made and the comparison is of
/// the records alone. The two harness roots are the ones the test environment gives each data directory, in a home beside
/// it, so neither is the other's and both are removed with their installation.
/// </remarks>
public class ExportImportRoundTripTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_one_runtime_wrote_another_holds_by_id_after_export_and_import_with_the_folders_this_machine_s()
    {
        using var from = GoldenPath.Create("export");
        using var to = GoldenPath.Create("import");
        await File.WriteAllTextAsync(from.Paths.UserSettingsFile, Settings, Ct);

        await GoldenPath.StartAsync(from);
        var project = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(from, "project", "create", "--name", "Acme Widgets", "--slug", "acme")))["id"]!;
        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(from, "campaign", "create", "--name", "Autumn outreach", "--project", project)))["id"]!;
        var contacts = from.Root + "-contacts.json";
        await File.WriteAllTextAsync(
            contacts,
            """
            [
              { "first_name": "Ana", "channels": [{ "channel": "email", "value": "ana@example.test" }] },
              { "first_name": "Ben", "channels": [{ "channel": "email", "value": "ben@example.test" }] }
            ]
            """,
            Ct);
        await GoldenPath.Ok(GoldenPath.JasonAsync(from, "campaign", "add-contacts", campaign, "--file", contacts, "--match-by", "email"));
        File.Delete(contacts);
        var item = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(from, "workitem", "create", campaign, "--kind", "ai_role", "--role", "researcher")))["id"]!;

        var before = await RecordsAsync(from, campaign);
        await GoldenPath.StopAsync(from);

        var file = from.Root + "-archive.zip";
        try
        {
            // The database as the stopped runtime left it: whichever of its files are there, never one planted.
            var left = Directory.GetFiles(Path.Combine(from.Root, "state"), "jason.db*")
                .Select(path => "data/state/" + Path.GetFileName(path))
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Contains("data/state/jason.db", left);
            var exported = await GoldenPath.JasonAsync(from, "export", file);
            Assert.True(exported.ExitCode == ExitCodes.Success, $"export: {exported.Output} {exported.Error}");
            using (var archive = ZipFile.OpenRead(file))
            {
                Assert.Equal(left, archive.Entries.Select(entry => entry.FullName).Where(name => name.StartsWith("data/state/", StringComparison.Ordinal) && !name.EndsWith('/')).Order(StringComparer.Ordinal).ToArray());
            }

            // Another machine holds no folder of the one exported. Here the first root is moved aside within its own home, so
            // a link finds no marker at the recorded place and the folder is removed with its installation.
            var fromRoot = Path.Combine(TempDataDir.HomeOf(from.Root), "Jason");
            var toRoot = Path.Combine(TempDataDir.HomeOf(to.Root), "Jason");
            Directory.Move(fromRoot, fromRoot + "-elsewhere");

            // An empty data directory: the folder the test made for a settings file holds none.
            Directory.Delete(Path.Combine(to.Root, "config"));
            var imported = await GoldenPath.JasonAsync(to, "import", file);

            // The runtime the import started is this installation's to end, whatever the import answered.
            if (GoldenPath.ReadDescriptor(to.Paths) is { } descriptor)
            {
                to.Pid = descriptor.Pid;
                to.PidStartedAt = descriptor.StartedAt;
            }

            Assert.True(imported.ExitCode == ExitCodes.Success, $"import: {imported.Output} {imported.Error}");
            Assert.NotNull(to.Pid);
            var answer = GoldenPath.Json(imported);
            Assert.Empty(answer["link_refused"]!.AsArray());
            Assert.Equal([project], [.. answer["linked"]!.AsArray().Select(link => (string)link!["project_id"]!)]);

            var after = await RecordsAsync(to, campaign);

            Assert.Equal(Ids(before.Projects), Ids(after.Projects));
            Assert.Contains(project, Ids(after.Projects));
            Assert.Equal(Ids(before.Campaigns), Ids(after.Campaigns));
            Assert.Equal([campaign], Ids(after.Campaigns));
            Assert.Equal(Ids(before.Contacts), Ids(after.Contacts));
            Assert.Equal(2, after.Contacts.Count);
            Assert.Equal(Ids(before.WorkItems), Ids(after.WorkItems));
            Assert.Equal([item], Ids(after.WorkItems));

            // What the records say is the same but where a folder is: each recorded folder is under this root now.
            Assert.Equal(Plain(before.Contacts), Plain(after.Contacts));
            Assert.Equal(Plain(before.WorkItems), Plain(after.WorkItems));
            foreach (var record in after.Projects.Concat(after.Campaigns))
            {
                var id = (string)record["id"]!;
                var folder = (string?)record["folder_path"];
                var was = (string?)before.Projects.Concat(before.Campaigns).Single(old => (string)old["id"]! == id)["folder_path"];
                if (was is null)
                {
                    Assert.Null(folder);
                    continue;
                }

                Assert.True(was.StartsWith(fromRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), $"{id} was at '{was}', outside '{fromRoot}'.");
                Assert.NotNull(folder);
                Assert.True(folder.StartsWith(toRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), $"{id} is recorded at '{folder}', outside this machine's root '{toRoot}'.");
                Assert.True(Directory.Exists(folder), $"{id} is recorded at '{folder}', which is not there.");
            }

            await GoldenPath.StopAsync(to);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>The settings both installations run under: the shipped defaults, no route, and no dispatch.</summary>
    private static string Settings => new JsonObject
    {
        ["Runtime"] = new JsonObject { ["Port"] = 0 },
        ["Dispatcher"] = new JsonObject { ["Enabled"] = false },
    }.ToJsonString(JasonJson.Options);

    private sealed record Records(List<JsonObject> Projects, List<JsonObject> Campaigns, List<JsonObject> Contacts, List<JsonObject> WorkItems);

    private static async Task<Records> RecordsAsync(Installation it, string campaign) => new(
        await ItemsAsync(it, "project", "list"),
        await ItemsAsync(it, "campaign", "list"),
        await ItemsAsync(it, "contact", "list"),
        await ItemsAsync(it, "workitem", "list", "--campaign", campaign));

    private static async Task<List<JsonObject>> ItemsAsync(Installation it, params string[] args) =>
        [.. GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, args)))["items"]!.AsArray().Select(node => node!.AsObject())];

    private static string[] Ids(IEnumerable<JsonObject> records) => [.. records.Select(record => (string)record["id"]!).Order(StringComparer.Ordinal)];

    private static string[] Plain(IEnumerable<JsonObject> records) =>
        [.. records.OrderBy(record => (string)record["id"]!, StringComparer.Ordinal).Select(record => record.ToJsonString())];
}
