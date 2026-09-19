using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A provider operation from end to end, through the shipped program and nothing else: a package installed by
/// copying it, a route written into the settings file, a runtime started in the background, and a work item that
/// a dispatcher claims, routes, runs in a plugin host against a real provider account, and answers — with what
/// was decided pinned onto the attempt and what the provider calls our contact pinned onto the contact.
/// </summary>
/// <remarks>
/// <para>
/// The second act is the one that proves the rule rather than the symptom. Removing the global route from the
/// settings file changes nothing at all until a reload: routes are frozen into a snapshot exactly as grants are,
/// so a work item created after the edit still succeeds, and only after <c>plugin reload</c> does the next one
/// fail with <c>no_route</c>. Without that half the test could not tell "frozen at reload" from "read live, and
/// the file happened to be re-read late".
/// </para>
/// <para>
/// Every distinctive value this run uses — the workspace directory, the list the arguments name, the address the
/// contact is reachable at, the answer the operation refused — carries one mark generated for the run, so the
/// final assertion about the log files cannot pass by matching nothing.
/// </para>
/// </remarks>
public class ProviderOperationE2ETests
{
    /// <summary>Generous: a cold CLI process on a loaded machine is still far quicker than this.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a claim, a child process and an answer may take between them.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private const string PluginId = "fake-provider";

    /// <summary>The stand-in vendor CLI is installed beside these tests rather than onto the machine.</summary>
    private static string CliDirectory => AppContext.BaseDirectory;

    /// <summary>
    /// The shipped program as the repository's own documented command starts it — <c>dotnet jason.dll</c>, the
    /// same way every other end-to-end test here starts it. Nothing about the plugin host depends on that
    /// choice: a runtime run through the muxer names its entry assembly again when it starts a child.
    /// </summary>
    private static string Assembly => Path.Combine(AppContext.BaseDirectory, "jason.dll");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_provider_operation_runs_stays_routed_until_a_reload_and_leaves_nothing_private_in_the_logs()
    {
        // One mark for the run, in every value that must never reach a log file: the account directory a binding
        // names, the list an argument names, the address the person is reachable at, and the answer refused for
        // its shape. Distinctive, so the privacy assertion at the end is about these values and not about luck.
        var mark = "zq" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "jason-provider", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(Path.GetTempPath(), "jason-provider-account-" + mark);
        var address = mark + "@example.test";
        var list = "lst-" + mark;
        var providerCampaign = "cmp-" + mark;
        var refusedName = "name-" + mark;

        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(workspace);
        var paths = new JasonPaths(root);
        await File.WriteAllTextAsync(paths.UserSettingsFile, Settings(workspace, routed: true), Ct);

        // The provider's own account: a list to add someone to, and a campaign whose name is not a name. The
        // operation's output schema refuses that answer, which is how a rejected result gets onto an attempt.
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "lists.json"),
            new JsonObject
            {
                [list] = new JsonObject { ["id"] = list, ["name"] = "Q3 prospects", ["members"] = new JsonArray() },
            }.ToJsonString(JasonJson.Options),
            Ct);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "campaigns.json"),
            new JsonObject
            {
                [providerCampaign] = new JsonObject
                {
                    ["id"] = providerCampaign,
                    ["name"] = new JsonArray(refusedName),
                    ["status"] = "Active",
                    ["enrollments"] = new JsonArray(),
                    ["vendor"] = new JsonObject { ["state"] = "Active" },
                },
            }.ToJsonString(JasonJson.Options),
            Ct);

        // Installing a plugin is copying its directory. There is no install verb, and none is needed.
        Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", PluginId), paths.PluginPackageDirectory(PluginId));

        var pids = new HashSet<int>();
        try
        {
            Assert.True(File.Exists(Assembly), $"The shipped program is not beside these tests: '{Assembly}'.");
            AssertSuccess(await JasonAsync(root, "runtime", "start"));
            var descriptor = ReadDescriptor(paths) ?? throw new InvalidOperationException("runtime start returned success without leaving a descriptor behind.");
            pids.Add(descriptor.Pid);

            // 1. The route is frozen by the same act that freezes the packages it names, and the registry says
            //    which route snapshot is active beside which plugin snapshot.
            var reloaded = Json(await Ok(JasonAsync(root, "plugin", "reload", "--reason", "installed the provider plugin")));
            var routedSnapshot = (string)reloaded["routing_snapshot_id"]!;
            Assert.StartsWith("rts_", routedSnapshot, StringComparison.Ordinal);
            Assert.True((bool)reloaded["activated"]!);
            var pluginSnapshot = (string)reloaded["snapshot"]!["id"]!;
            var digest = (string)Assert.Single(reloaded["plugins"]!.AsArray())!["digest"]!;

            // 2. A campaign, a person in it, and the work: one canonical operation with its arguments under the
            //    one context key that carries them.
            var campaign = (string)Json(await Ok(JasonAsync(root, "campaign", "create", "--name", "Provider path")))["id"]!;
            var contactsFile = Path.Combine(root, "contacts.json");
            await File.WriteAllTextAsync(
                contactsFile,
                new JsonArray(new JsonObject
                {
                    ["first_name"] = "Marta",
                    ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = address }),
                }).ToJsonString(JasonJson.Options),
                Ct);
            var added = Json(await Ok(JasonAsync(root, "campaign", "add-contacts", campaign, "--file", contactsFile, "--match-by", "email")));
            Assert.Equal(1, (int)added["summary"]!["added"]!);
            var members = Json(await Ok(JasonAsync(root, "campaign", "list-contacts", campaign)));
            var contact = (string)Assert.Single(members["items"]!.AsArray())!["contact"]!["id"]!;

            var first = await ItemAsync(root, campaign, "list_membership.add", ToTheList(list), contact);
            AssertSuccess(await JasonAsync(root, "campaign", "start", campaign));

            var done = await PollAsync(root, first, item => Finished((string?)item["status"]));
            AssertSucceeded(done);

            // The neutral answer, not the provider's word for it: the contract publishes added | already_member.
            var outcome = done["result"]!["items"]!.AsArray()[0]!;
            Assert.Equal("added", (string?)outcome["status"]);
            Assert.Equal(contact, (string?)outcome["contact_id"]);

            // 3. What ran, as a person reads it: the package and its digest, the operation, the level of routing
            //    that chose it, which account by identity, and both snapshots the decision was made against.
            var human = (await Ok(JasonAsync(root, "workitem", "get", first, "--human"))).Output;
            Assert.Contains("RAN", human, StringComparison.Ordinal);
            Assert.Contains(PluginId, human, StringComparison.Ordinal);
            Assert.Contains(digest["sha256:".Length..][..12], human, StringComparison.Ordinal);
            Assert.Contains("list_membership.add v1", human, StringComparison.Ordinal);
            Assert.Contains("global_default", human, StringComparison.Ordinal);
            Assert.Contains("binding ", human, StringComparison.Ordinal);
            Assert.Contains("plugins " + pluginSnapshot, human, StringComparison.Ordinal);
            Assert.Contains("routes " + routedSnapshot, human, StringComparison.Ordinal);

            // The binding's identity is a hash, so what a provenance block shows can never be the account itself.
            Assert.DoesNotContain(workspace, human, StringComparison.OrdinalIgnoreCase);

            // 4. And what the provider calls this person, pinned on the contact by the attempt that learned it.
            var pinned = (await Ok(JasonAsync(root, "contact", "get", contact, "--human"))).Output;
            Assert.Contains("EXTERNAL IDS", pinned, StringComparison.Ordinal);
            Assert.Contains(PluginId, pinned, StringComparison.Ordinal);
            Assert.Contains("p_1001", pinned, StringComparison.Ordinal);

            // 5. An answer the operation's own schema refuses is not a result. The item ends, the pointers say
            //    where, and what was actually sent is kept on the attempt so its author can see it — under
            //    `--snapshots`, with the other evidence too large to ride on every read.
            var refused = await ItemAsync(root, campaign, "campaign.get", Named(providerCampaign), contact: null);
            var rejected = await PollAsync(root, refused, item => Finished((string?)item["status"]), snapshots: true);
            Assert.Equal("failed", (string?)rejected["status"]);
            var refusal = Assert.Single(rejected["attempts"]!.AsArray())!;
            Assert.Equal("result_invalid", (string?)refusal["error"]!["code"]);
            Assert.Equal("ambiguous", (string?)refusal["error"]!["class"]);
            Assert.False((bool)refusal["error"]!["retriable"]!);
            Assert.Equal("/campaign/name", (string?)Assert.Single(refusal["error"]!["details"]!.AsArray())!["field"]);
            Assert.Equal(
                refusedName,
                (string?)refusal["provenance"]!["rejected_result"]!["campaign"]!["name"]!.AsArray()[0]);

            // 6. An operation that needs a person's approval is not performed by a dispatcher standing in for
            //    that person. It waits, with no attempt behind it, until somebody decides.
            var gated = await ItemAsync(root, campaign, "campaign.enroll", ToEnroll(providerCampaign), contact);
            var waiting = await PollAsync(root, gated, item => (string?)item["status"] == "awaiting_approval");
            Assert.Empty(waiting["attempts"]!.AsArray());
            Assert.Equal(0, (int?)waiting["attempt_count"]);

            // Nothing reached the provider on its behalf. The account holds no enrollment — and, the claim that
            // actually matters, it was never asked for one: an empty array is what a refused write leaves behind
            // too. The stand-in account logs every call it answers, and the log is where "nothing was asked" is
            // readable. It is not empty, because everything above did reach this account.
            Assert.Empty(JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(workspace, "campaigns.json"), Ct))!
                .AsObject()[providerCampaign]!["enrollments"]!.AsArray());
            var asked = await CallsAsync(workspace);
            Assert.NotEmpty(asked);
            Assert.DoesNotContain("campaign enroll", asked);

            // 7. The route is taken out of the settings file — and nothing changes yet. A work item created now
            //    still runs against the plugin, because the dispatcher reads the frozen snapshot and never the file.
            await File.WriteAllTextAsync(paths.UserSettingsFile, Settings(workspace, routed: false), Ct);

            var stillRouted = await ItemAsync(root, campaign, "list_membership.add", ToTheList(list), contact);
            var unchanged = await PollAsync(root, stillRouted, item => Finished((string?)item["status"]));
            AssertSucceeded(unchanged);
            Assert.Equal("already_member", (string?)unchanged["result"]!["items"]!.AsArray()[0]!["status"]);
            Assert.Equal(routedSnapshot, (string?)Assert.Single(unchanged["attempts"]!.AsArray())!["provenance"]!["routing_snapshot_id"]);

            // 8. The reload is the act that changes it, and it changes both snapshots together.
            var unrouted = Json(await Ok(JasonAsync(root, "plugin", "reload", "--reason", "took the global route away")));
            var emptySnapshot = (string)unrouted["routing_snapshot_id"]!;
            Assert.NotEqual(routedSnapshot, emptySnapshot);
            var listed = Json(await Ok(JasonAsync(root, "route", "list")))["global"]!;
            Assert.Empty(listed["operations"]!.AsObject());
            Assert.Null(listed["default"]);

            // 9. Now the same work fails at the claim, before any child exists — and the attempt is kept, with
            //    the context it was claimed with and the provenance as far as the decision got.
            var stranded = await ItemAsync(root, campaign, "list_membership.add", ToTheList(list), contact);
            var lost = await PollAsync(root, stranded, item => Finished((string?)item["status"]), snapshots: true);
            Assert.Equal("failed", (string?)lost["status"]);
            var unclaimed = Assert.Single(lost["attempts"]!.AsArray())!;
            Assert.Equal("no_route", (string?)unclaimed["error"]!["code"]);
            Assert.False((bool)unclaimed["error"]!["retriable"]!);
            Assert.Equal(list, (string?)unclaimed["context_snapshot"]!["input"]!["list"]!["external_id"]);

            var asFarAsItGot = unclaimed["provenance"]!;
            Assert.Null((string?)asFarAsItGot["plugin_id"]);
            Assert.Equal("list_membership.add", (string?)asFarAsItGot["operation"]);
            Assert.Equal(emptySnapshot, (string?)asFarAsItGot["routing_snapshot_id"]);

            // 10. The runtime is stopped before the logs are read, so nothing is still buffered when they are.
            AssertSuccess(await JasonAsync(root, "runtime", "stop"));
            Assert.False(File.Exists(paths.DescriptorFile));
            Assert.False(IsRunning(descriptor.Pid), "The runtime process was still alive after runtime stop returned.");

            AssertNothingPrivateIsLogged(paths, first, mark, workspace, address, list, refusedName);
        }
        finally
        {
            // Whatever went wrong, nothing this test started is left running.
            if (ReadDescriptor(paths) is { } surviving)
            {
                pids.Add(surviving.Pid);
            }

            foreach (var pid in pids)
            {
                Kill(pid);
            }

            Delete(root);
            Delete(workspace);
        }
    }

    /// <summary>
    /// PA3a. Every file the runtime wrote under <c>logs/</c>, read back and held to the rule the logging
    /// contract states: no request body, no work-item context, no binding and no answer a plugin sent. The run
    /// above put one distinctive mark into every one of those places, so a log line that leaked any of them
    /// carries it — and a check that found no files at all would be a check about nothing, which is why the
    /// files and their size are asserted first.
    /// </summary>
    private static void AssertNothingPrivateIsLogged(JasonPaths paths, string ranAndWasLogged, string mark, params string[] values)
    {
        var written = Directory
            .GetFiles(paths.LogsDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(file => file, File.ReadAllText, StringComparer.Ordinal);
        Assert.NotEmpty(written);

        // The work this run did is in there by name, so the lines a leak would have travelled in are lines these
        // assertions actually read. Without this the whole check could pass against an empty directory.
        Assert.Contains(written.Values, text => text.Contains(ranAndWasLogged, StringComparison.Ordinal));

        foreach (var (file, text) in written)
        {
            // One mark covers the binding, the arguments, the contact's address and the refused answer at once:
            // a leak of any of them is a leak of this string.
            Assert.DoesNotContain(mark, text, StringComparison.OrdinalIgnoreCase);

            foreach (var value in values)
            {
                Assert.False(
                    text.Contains(value, StringComparison.OrdinalIgnoreCase),
                    $"'{Path.GetFileName(file)}' carries a value no log file may hold: {value}");
            }

            // What Task 9 added to an attempt, which is a document a plugin wrote and the pointers that refused
            // it: neither belongs in a log line either.
            Assert.DoesNotContain("rejected_result", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/campaign/name", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The settings of this run. The dispatcher ticks fast because a test waits on it; one attempt per item
    /// keeps every failure below its own single attempt; and the log level is the loudest the validator accepts,
    /// so the privacy assertion is made against everything this runtime could possibly have written.
    /// </summary>
    private static string Settings(string workspace, bool routed)
    {
        var settings = new JsonObject
        {
            ["Logging"] = new JsonObject { ["MinimumLevel"] = "Verbose" },
            ["Dispatcher"] = new JsonObject
            {
                ["TickSeconds"] = 1,
                ["RetryDelaySeconds"] = 0,
                ["ProviderOp"] = new JsonObject { ["MaxAttempts"] = 1 },
            },
            ["Plugins"] = new JsonObject
            {
                ["Grants"] = new JsonObject { [PluginId] = new JsonObject { ["Exec"] = new JsonArray("*") } },
            },
        };

        if (routed)
        {
            settings["Routes"] = new JsonObject
            {
                ["Default"] = new JsonObject
                {
                    ["Plugin"] = PluginId,
                    ["Binding"] = new JsonObject { ["workspace"] = workspace },
                },
            };
        }

        return settings.ToJsonString(JasonJson.Options);
    }

    /// <summary>
    /// Every subcommand the stand-in account answered, in the order it was asked. It is the account's own record
    /// of what was requested of it, which is the only way to tell work that was refused from work never sent.
    /// </summary>
    private static async Task<IReadOnlyList<string>> CallsAsync(string workspace)
    {
        var file = Path.Combine(workspace, "calls.json");
        return File.Exists(file)
            ? [.. JsonNode.Parse(await File.ReadAllTextAsync(file, Ct))!.AsArray().Select(call => call!["subcommand"]!.GetValue<string>())]
            : [];
    }

    private static JsonObject ToTheList(string list) => new()
    {
        ["list"] = new JsonObject { ["external_id"] = list },
        ["channel"] = "email",
    };

    private static JsonObject Named(string externalId) => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = externalId },
    };

    private static JsonObject ToEnroll(string externalId) => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = externalId },
        ["channel"] = "email",
        ["collision"] = "skip",
        ["start"] = new JsonObject { ["position"] = "first_step" },
        ["first_touch"] = "authored_delay",
    };

    private static async Task<string> ItemAsync(string root, string campaign, string operation, JsonObject input, string? contact)
    {
        string[] arguments = contact is null
            ? ["workitem", "create", campaign, "--kind", "provider_op", "--operation", operation, "--input", input.ToJsonString(JasonJson.Options)]
            : ["workitem", "create", campaign, "--kind", "provider_op", "--operation", operation, "--contact", contact, "--input", input.ToJsonString(JasonJson.Options)];

        var created = Json(await Ok(JasonAsync(root, arguments)));
        Assert.Equal("created", (string?)created["status"]);
        return (string)created["id"]!;
    }

    private static bool Finished(string? status) => status is "succeeded" or "failed" or "expired" or "cancelled";

    /// <summary>
    /// A work item that ran. The whole item is the failure message: an end-to-end run that went wrong somewhere
    /// in a child process says why in the attempt it left behind, and nowhere a reader of the test output could
    /// otherwise reach.
    /// </summary>
    private static void AssertSucceeded(JsonObject item) =>
        Assert.True(
            (string?)item["status"] == "succeeded",
            $"The work item did not succeed: {item.ToJsonString(JasonJson.Options)}");

    /// <summary>Reads one work item until it looks the way the test expects, because real processes take time.</summary>
    private static async Task<JsonObject> PollAsync(string root, string workItemId, Func<JsonObject, bool> expected, bool snapshots = false)
    {
        var deadline = DateTime.UtcNow.Add(Patience);
        JsonObject item;
        do
        {
            item = Json(await Ok(snapshots
                ? JasonAsync(root, "workitem", "get", workItemId, "--snapshots")
                : JasonAsync(root, "workitem", "get", workItemId)));
            if (expected(item))
            {
                return item;
            }

            await Task.Delay(200, Ct);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Work item {workItemId} never got where it was going: {item.ToJsonString()}");
        return item;
    }

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

    private static async Task<CliResult> Ok(Task<CliResult> step)
    {
        var result = await step;
        AssertSuccess(result);
        return result;
    }

    private static void AssertSuccess(CliResult result) =>
        Assert.True(
            result.ExitCode == ExitCodes.Success && result.Error.Length == 0,
            $"'jason {result.Command}' exited with {result.ExitCode}.{Environment.NewLine}stdout: {result.Output}{Environment.NewLine}stderr: {result.Error}");

    private static JsonObject Json(CliResult result) => JsonNode.Parse(result.Output)!.AsObject();

    /// <summary>One CLI step: the shipped program, the isolated data directory, and everything it said.</summary>
    private static async Task<CliResult> JasonAsync(string root, params string[] args)
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

        TestRuntimeEnvironment.Offline(start.Environment, root);

        // The package declares the stand-in vendor CLI by its own name, and that program is installed beside
        // these tests rather than onto the machine, so the runtime this starts has to be told where to look.
        start.Environment["PATH"] = CliDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The jason executable could not be started.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(Ct);
        var error = process.StandardError.ReadToEndAsync(Ct);
        var command = string.Join(' ', args);

        await process.WaitForExitAsync(Ct).WaitAsync(StepTimeout, Ct);
        return new CliResult(
            process.ExitCode,
            (await output.WaitAsync(StepTimeout, Ct)).Trim(),
            (await error.WaitAsync(StepTimeout, Ct)).Trim(),
            command);
    }

    private static RuntimeDescriptor? ReadDescriptor(JasonPaths paths)
    {
        try
        {
            return JsonSerializer.Deserialize<RuntimeDescriptor>(File.ReadAllBytes(paths.DescriptorFile), JasonJson.Options);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void Kill(int pid)
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
            // Already gone, which is the state this is trying to reach.
        }
        catch (InvalidOperationException)
        {
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
            // A handle may outlive the process that held it; a temporary directory left behind is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record CliResult(int ExitCode, string Output, string Error, string Command);
}
