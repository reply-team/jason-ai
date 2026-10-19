using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A provider's events from end to end, through the shipped program: a package that declares the events contract,
/// installed by copying it, a route that binds an account of it, and a runtime that reads that account in a plugin-host
/// child off its scan — storing each event once, saying what concerns a campaign in that campaign's chronicle, keeping
/// what concerns nobody, and reading after a restart only what is new. The plugin's cursor and the provider's own event
/// ids carry one mark for the run, and the mark reaches neither the chronicle nor the runtime's log.
/// </summary>
public class EventsE2ETests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private const string PluginId = "fake-events";

    private static string CliDirectory => AppContext.BaseDirectory;

    private static string Assembly => Path.Combine(AppContext.BaseDirectory, "jason.dll");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_linked_campaign_hears_its_provider_s_events_and_a_restart_reads_only_what_is_new()
    {
        var mark = "zq" + Guid.NewGuid().ToString("N");
        using var run = new Run("feed", mark);
        await run.AccountAsync(prefix: "c-" + mark + "-");
        var pids = new HashSet<int>();
        try
        {
            await run.StartAsync(pids);
            await Ok(run.JasonAsync("plugin", "reload", "--reason", "installed the events plugin"));

            // A campaign, linked at the plugin by the provider campaign its own operation pinned, and live.
            var campaign = (string)Json(await Ok(run.JasonAsync("campaign", "create", "--name", "Events path")))["id"]!;
            var link = (string)Json(await Ok(run.JasonAsync(
                "workitem", "create", campaign, "--kind", "provider_op", "--operation", "campaign.get",
                "--input", new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "p-1" } }.ToJsonString(JasonJson.Options))))["id"]!;
            await Ok(run.JasonAsync("campaign", "start", campaign));
            await run.PollAsync(() => run.JasonAsync("workitem", "get", link), item => (string?)item["status"] == "succeeded", $"the link {link}");

            // Then the provider records three events: a reply in a thread of the campaign, a bounce in it, and an
            // accepted invitation in a provider campaign nobody here is linked to.
            var now = DateTime.UtcNow;
            await run.EventsAsync(
                Moved("t-1", "p-1", now, $"e-{mark}-1"),
                Bounced("c-1", "p-1", now, $"e-{mark}-2"),
                Accepted("c-2", "p-elsewhere", now, $"e-{mark}-3"));

            // A restart catches every account up from its cursor at its first scan.
            await run.RestartAsync(pids);
            var replies = await run.LinesAsync(campaign, "inbound_reply_observed", 1);
            var provider = await run.LinesAsync(campaign, "provider_event_observed", 1);
            Assert.Equal(("fake-events", "t-1"), ((string?)replies[0]["new"]!["plugin"], (string?)replies[0]["new"]!["thread"]));
            Assert.Equal(("bounce", "fake-events"), ((string?)provider[0]["new"]!["kind"], (string?)provider[0]["new"]!["plugin"]));
            Assert.StartsWith("evt_", (string?)provider[0]["new"]!["event_id"], StringComparison.Ordinal);

            // The read that stored them stored three, said two, and kept the one that concerns nobody.
            var stored = await run.ReadsAsync(read => (int)read["new"]!["stored"]! > 0, 1);
            Assert.Equal((3, 2), ((int)stored[0]["new"]!["stored"]!, (int)stored[0]["new"]!["said"]!));

            // The record of the account says it is read, healthy and holds a cursor — and never what the cursor is.
            var sources = await Ok(run.JasonAsync("events", "sources"));
            var source = Json(sources)["items"]!.AsArray().Single()!;
            Assert.Equal(
                ("fake-events", true, true, true),
                ((string?)source["plugin"], (bool)source["bound"]!, (bool)source["has_cursor"]!, (bool)source["healthy"]!));
            Assert.DoesNotContain(mark, sources.Output, StringComparison.Ordinal);
            Assert.Contains((string)source["id"]!, (await Ok(run.JasonAsync("events", "sources", "--human"))).Output, StringComparison.Ordinal);

            // A signal for the account is kept on it and answered with its receipt.
            var receipt = Json(await Ok(run.JasonAsync("events", "signal", "--source", (string)source["id"]!)));
            Assert.NotNull((string?)receipt["signaled_at"]);
            Assert.NotNull((string?)receipt["next_read_at"]);

            // One more event, recorded while the runtime is stopped: the next start reads it, and only it.
            await Ok(run.JasonAsync("runtime", "stop"));
            await run.EventsAsync(
                Moved("t-1", "p-1", now, $"e-{mark}-1"),
                Bounced("c-1", "p-1", now, $"e-{mark}-2"),
                Accepted("c-2", "p-elsewhere", now, $"e-{mark}-3"),
                Bounced("c-3", "p-1", now.AddSeconds(1), $"e-{mark}-4"));
            await run.StartAsync(pids);
            var reads = await run.ReadsAsync(read => (int)read["new"]!["stored"]! > 0, 2);
            Assert.Equal((1, 0, "cursor"), ((int)reads[1]["new"]!["stored"]!, (int)reads[1]["new"]!["duplicates"]!, (string?)reads[1]["new"]!["mode"]));
            await run.LinesAsync(campaign, "provider_event_observed", 2);

            await Ok(run.JasonAsync("runtime", "stop"));
            run.AssertTheMarkStayedAtTheProvider(campaign);
        }
        finally
        {
            run.Stop(pids);
        }
    }

    [Fact]
    public async Task A_provider_paged_by_offset_alone_is_read_back_thirty_days_and_then_only_for_what_is_new()
    {
        var mark = "zq" + Guid.NewGuid().ToString("N");
        using var run = new Run("offset", mark);
        var now = DateTime.UtcNow;
        await run.AccountAsync(prefix: "c-");
        await run.EventsAsync(
            Bounced("c-1", "p-1", now.AddDays(-40), $"e-{mark}-1"),
            Bounced("c-2", "p-1", now.AddHours(-1), $"e-{mark}-2"));
        var pids = new HashSet<int>();
        try
        {
            await run.StartAsync(pids);
            await Ok(run.JasonAsync("plugin", "reload", "--reason", "installed the events plugin"));

            // The first read reaches back thirty days: the bounce of an hour ago, and not the one of forty days ago.
            var first = await run.ReadsAsync(_ => true, 1);
            Assert.Equal(("first_start", 1, "succeeded"), ((string?)first[0]["new"]!["mode"], (int)first[0]["new"]!["stored"]!, (string?)first[0]["new"]!["outcome"]));

            await Ok(run.JasonAsync("runtime", "stop"));
            await run.EventsAsync(
                Bounced("c-1", "p-1", now.AddDays(-40), $"e-{mark}-1"),
                Bounced("c-2", "p-1", now.AddHours(-1), $"e-{mark}-2"),
                Bounced("c-3", "p-1", now, $"e-{mark}-3"));
            await run.StartAsync(pids);

            // The next start reads from the plugin's own cursor: the newest event, and the one it had seen last again.
            var next = await run.ReadsAsync(read => (int)read["new"]!["stored"]! > 0, 2);
            Assert.Equal((1, "cursor"), ((int)next[1]["new"]!["stored"]!, (string?)next[1]["new"]!["mode"]));

            await Ok(run.JasonAsync("runtime", "stop"));
            run.AssertTheMarkStayedAtTheProvider(campaign: null);
        }
        finally
        {
            run.Stop(pids);
        }
    }

    /// <summary>
    /// A span the first read did not reach is read again on a person's request, through the shipped program: over more
    /// than one page, every event at or after its start and before its end and none after it — the plugin keeps the end
    /// in its own cursor — while the account's own cursor stays where its reading left it. The person's reason reaches
    /// no log file.
    /// </summary>
    [Fact]
    public async Task A_span_the_first_read_did_not_reach_is_read_again_on_request()
    {
        var mark = "zq" + Guid.NewGuid().ToString("N");
        using var run = new Run("feed", mark);
        await run.AccountAsync(prefix: "c-" + mark + "-");
        var now = DateTime.UtcNow;
        var from = now.AddDays(-36);
        var to = now.AddDays(-34);
        var old = Enumerable.Range(0, EventsInTheSpan)
            .Select(index => Bounced($"c-old-{index}", "p-1", now.AddDays(-35).AddSeconds(index), $"e-{mark}-{index}"))
            .Append(Bounced("c-after", "p-1", to.AddHours(1), $"e-{mark}-after"))
            .ToArray();
        await run.EventsAsync(old);
        var pids = new HashSet<int>();
        try
        {
            await run.StartAsync(pids);
            await Ok(run.JasonAsync("plugin", "reload", "--reason", "installed the events plugin"));

            // The first read reaches back thirty days, and finds none of them.
            var first = await run.ReadsAsync(_ => true, 1);
            Assert.Equal(("first_start", 0), ((string?)first[0]["new"]!["mode"], (int)first[0]["new"]!["stored"]!));
            var before = Json(await Ok(run.JasonAsync("events", "sources")))["items"]!.AsArray().Single()!;

            var asked = Json(await Ok(run.JasonAsync(
                "events", "backfill", "--source", (string)before["id"]!, "--from", Stamp(from), "--to", Stamp(to), "--reason", "reason-" + mark)));
            Assert.Equal("pending", (string?)asked["status"]);

            var after = await run.PollAsync(
                () => run.JasonAsync("events", "sources"),
                page => page["items"]![0]!["backfills"]!.AsArray() is [{ } span, ..] && (string?)span["status"] == "done",
                "the span read to its end");
            var record = after["items"]![0]!;
            Assert.Equal(EventsInTheSpan, (int)record["backfills"]![0]!["stored"]!);
            Assert.Equal((string?)before["cursor_at"], (string?)record["cursor_at"]);
            var spans = await run.ReadsAsync(read => (string?)read["new"]!["mode"] == "backfill", 1);
            Assert.Equal((EventsInTheSpan, (string?)asked["id"]), ((int)spans[0]["new"]!["stored"]!, (string?)spans[0]["new"]!["backfill"]));

            await Ok(run.JasonAsync("runtime", "stop"));
            run.AssertTheMarkStayedAtTheProvider(campaign: null);
        }
        finally
        {
            run.Stop(pids);
        }
    }

    /// <summary>More than a page's hundred, so the span is read over two pages and the second is asked for by the plugin's own cursor.</summary>
    private const int EventsInTheSpan = 101;

    private static JsonObject Moved(string thread, string campaign, DateTime at, string id) => new()
    {
        ["kind"] = "thread_moved",
        ["id"] = id,
        ["occurred_at"] = Stamp(at),
        ["thread"] = thread,
        ["contact"] = null,
        ["campaign"] = campaign,
        ["moved_by"] = "other_side",
        ["channel"] = "email",
        ["unread"] = true,
        ["has_meeting_intent"] = null,
    };

    private static JsonObject Bounced(string contact, string campaign, DateTime at, string id) => new()
    {
        ["kind"] = "bounce",
        ["id"] = id,
        ["occurred_at"] = Stamp(at),
        ["contact"] = contact,
        ["campaign"] = campaign,
        ["message"] = null,
        ["bounce"] = "hard",
    };

    private static JsonObject Accepted(string contact, string campaign, DateTime at, string id) => new()
    {
        ["kind"] = "invitation_accepted",
        ["id"] = id,
        ["occurred_at"] = Stamp(at),
        ["contact"] = contact,
        ["campaign"] = campaign,
        ["account"] = null,
    };

    private static string Stamp(DateTime at) => at.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static async Task<CliResult> Ok(Task<CliResult> step)
    {
        var result = await step;
        Assert.True(
            result.ExitCode == ExitCodes.Success && result.Error.Length == 0,
            $"'jason {result.Command}' exited with {result.ExitCode}.{Environment.NewLine}stdout: {result.Output}{Environment.NewLine}stderr: {result.Error}");
        return result;
    }

    private static JsonObject Json(CliResult result) => JsonNode.Parse(result.Output)!.AsObject();

    /// <summary>One run: its data directory, the stand-in account the route binds, and the package installed into it.</summary>
    private sealed class Run : IDisposable
    {
        private readonly string _mark;

        public Run(string paging, string mark)
        {
            _mark = mark;
            Root = Path.Combine(Path.GetTempPath(), "jason-events", Guid.NewGuid().ToString("N"));
            Workspace = Path.Combine(Path.GetTempPath(), "jason-events-account", Guid.NewGuid().ToString("N"));
            Paths = new JasonPaths(Root);
            Directory.CreateDirectory(Path.Combine(Root, "config"));
            Directory.CreateDirectory(Workspace);
            File.WriteAllText(Paths.UserSettingsFile, Settings(Workspace, paging));
            Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", PluginId), Paths.PluginPackageDirectory(PluginId));
        }

        public string Root { get; }

        public string Workspace { get; }

        public JasonPaths Paths { get; }

        /// <summary>The provider's account: one campaign, and the prefix its feed's cursors carry.</summary>
        public async Task AccountAsync(string prefix)
        {
            await File.WriteAllTextAsync(
                Path.Combine(Workspace, "campaigns.json"),
                new JsonObject
                {
                    ["p-1"] = new JsonObject
                    {
                        ["id"] = "p-1",
                        ["name"] = "Q3",
                        ["status"] = "Active",
                        ["enrollments"] = new JsonArray(),
                        ["vendor"] = new JsonObject(),
                    },
                }.ToJsonString(JasonJson.Options),
                Ct);
            await File.WriteAllTextAsync(Path.Combine(Workspace, "events.json"), new JsonObject { ["prefix"] = prefix, ["events"] = new JsonArray() }.ToJsonString(), Ct);
        }

        /// <summary>The events the account holds, in the order they happened.</summary>
        public async Task EventsAsync(params JsonObject[] events)
        {
            var file = Path.Combine(Workspace, "events.json");
            var held = JsonNode.Parse(await File.ReadAllTextAsync(file, Ct))!.AsObject();
            held["events"] = new JsonArray([.. events]);
            await File.WriteAllTextAsync(file, held.ToJsonString(), Ct);
        }

        public async Task StartAsync(HashSet<int> pids)
        {
            Assert.True(File.Exists(Assembly), $"The shipped program is not beside these tests: '{Assembly}'.");
            var started = await PatientStart.RunAsync(
                Paths,
                within => JasonAsync(within, "runtime", "start"),
                () => JasonAsync(StepTimeout, "runtime", "status"),
                result => (result.ExitCode, result.Output));
            Assert.True(started.ExitCode == ExitCodes.Success, $"runtime start exited with {started.ExitCode}: {started.Output} {started.Error}");
            if (ReadDescriptor() is { } descriptor)
            {
                pids.Add(descriptor.Pid);
            }
        }

        public async Task RestartAsync(HashSet<int> pids)
        {
            await Ok(JasonAsync("runtime", "stop"));
            await StartAsync(pids);
        }

        /// <summary>The lines of one kind a campaign holds, once there are at least so many.</summary>
        public async Task<List<JsonNode>> LinesAsync(string campaign, string kind, int atLeast) =>
            await ItemsAsync(() => JasonAsync("journal", "list", "--campaign", campaign, "--kind", kind), _ => true, atLeast, $"{atLeast} {kind} line(s) in {campaign}");

        /// <summary>The events-read lines that match, oldest first, once there are at least so many.</summary>
        public async Task<List<JsonNode>> ReadsAsync(Func<JsonNode, bool> matching, int atLeast) =>
            await ItemsAsync(() => JasonAsync("journal", "list", "--kind", "events_read"), matching, atLeast, $"{atLeast} events_read line(s)");

        public async Task<JsonObject> PollAsync(Func<Task<CliResult>> step, Func<JsonObject, bool> expected, string what)
        {
            var deadline = DateTime.UtcNow.Add(Patience);
            JsonObject item;
            do
            {
                item = Json(await Ok(step()));
                if (expected(item))
                {
                    return item;
                }

                await Task.Delay(200, Ct);
            }
            while (DateTime.UtcNow < deadline);

            Assert.Fail($"Never saw {what}: {item.ToJsonString()}");
            return item;
        }

        /// <summary>
        /// Neither the plugin's cursor nor the provider's own event ids — the two places the run's mark went — reach a
        /// log file or a line of the chronicle; the log files hold the reads by their source's id, so the check reads
        /// lines a leak would have travelled in.
        /// </summary>
        public void AssertTheMarkStayedAtTheProvider(string? campaign)
        {
            var written = Directory.GetFiles(Paths.LogsDirectory, "*", SearchOption.AllDirectories).ToDictionary(file => file, RuntimeLogs.ReadAllText, StringComparer.Ordinal);
            Assert.NotEmpty(written);
            Assert.Contains(written.Values, text => text.Contains("evs_", StringComparison.Ordinal));
            foreach (var (file, text) in written)
            {
                Assert.False(text.Contains(_mark, StringComparison.OrdinalIgnoreCase), $"'{Path.GetFileName(file)}' carries the plugin's cursor or a provider's event id.");
            }

            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Paths.DatabaseFile};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(new_json, '') || COALESCE(key, '') FROM journal";
            using var reader = command.ExecuteReader();
            var lines = 0;
            while (reader.Read())
            {
                lines++;
                Assert.DoesNotContain(_mark, reader.GetString(0), StringComparison.OrdinalIgnoreCase);
            }

            Assert.True(lines > 0, "The chronicle holds no line at all.");
            _ = campaign;
        }

        public void Stop(HashSet<int> pids)
        {
            if (ReadDescriptor() is { } surviving)
            {
                pids.Add(surviving.Pid);
            }

            foreach (var pid in pids)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        public void Dispose()
        {
            Delete(Root);
            Delete(Workspace);
        }

        public Task<CliResult> JasonAsync(params string[] args) => JasonAsync(StepTimeout, args);

        private async Task<List<JsonNode>> ItemsAsync(Func<Task<CliResult>> step, Func<JsonNode, bool> matching, int atLeast, string what)
        {
            var deadline = DateTime.UtcNow.Add(Patience);
            List<JsonNode> items;
            do
            {
                items = [.. Json(await Ok(step()))["items"]!.AsArray().OfType<JsonNode>().Where(matching).OrderBy(item => (string?)item["id"], StringComparer.Ordinal)];
                if (items.Count >= atLeast)
                {
                    return items;
                }

                await Task.Delay(250, Ct);
            }
            while (DateTime.UtcNow < deadline);

            Assert.Fail($"Never saw {what}: {string.Join(", ", items.Select(item => item.ToJsonString()))}");
            return items;
        }

        private async Task<CliResult> JasonAsync(TimeSpan within, params string[] args)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add(Assembly);
            foreach (var argument in args)
            {
                start.ArgumentList.Add(argument);
            }

            TestRuntimeEnvironment.Offline(start.Environment, Root);

            // The package declares the stand-in vendor program by its own name, installed beside these tests.
            start.Environment["PATH"] = CliDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

            using var process = Process.Start(start) ?? throw new InvalidOperationException("The jason executable could not be started.");
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(Ct);
            var error = process.StandardError.ReadToEndAsync(Ct);
            await process.WaitForExitAsync(Ct).WaitAsync(within, Ct);
            return new CliResult(process.ExitCode, (await output.WaitAsync(within, Ct)).Trim(), (await error.WaitAsync(within, Ct)).Trim(), string.Join(' ', args));
        }

        private RuntimeDescriptor? ReadDescriptor()
        {
            try
            {
                return JsonSerializer.Deserialize<RuntimeDescriptor>(File.ReadAllBytes(Paths.DescriptorFile), JasonJson.Options);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// A dispatcher that ticks fast, because the test waits on it; reads of events a minute apart, the shortest
        /// cadence; the loudest log level, so the privacy check reads everything the runtime could write; and one route,
        /// to the account in this run's directory.
        /// </summary>
        private static string Settings(string workspace, string paging) =>
            new JsonObject
            {
                ["Logging"] = new JsonObject { ["MinimumLevel"] = "Verbose" },
                ["Dispatcher"] = new JsonObject { ["TickSeconds"] = 1, ["RetryDelaySeconds"] = 0, ["EventsCadenceSeconds"] = 60 },
                ["Plugins"] = new JsonObject { ["Grants"] = new JsonObject { [PluginId] = new JsonObject { ["Exec"] = new JsonArray("*") } } },
                ["Routes"] = new JsonObject
                {
                    ["Default"] = new JsonObject
                    {
                        ["Plugin"] = PluginId,
                        ["Binding"] = new JsonObject { ["workspace"] = workspace, ["paging"] = paging },
                    },
                },
            }.ToJsonString(JasonJson.Options);

        private static void Copy(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }

        private static void Delete(string directory)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record CliResult(int ExitCode, string Output, string Error, string Command);
}
