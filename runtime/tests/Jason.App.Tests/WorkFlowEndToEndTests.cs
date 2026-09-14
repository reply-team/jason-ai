using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.App.Tests;

/// <summary>
/// Work actually being done, through the shipped executable and nothing else: a runtime started in the
/// background, a campaign filled from the command line, a role whose entry command is a real program, and a
/// dispatcher ticking on its own clock that claims the work, launches that program, and records what it
/// reported. Nothing here is in-process and nothing is driven by a test seam — this is the whole wave as a
/// user would run it.
/// </summary>
public class WorkFlowEndToEndTests
{
    /// <summary>Generous: a cold CLI process on a loaded machine is still far quicker than this.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a real child process may take to be launched, do its work and report back.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>A tick a second, no retry delay, and short budgets: the loop has to be visibly alive.</summary>
    private const string Settings = """
        {"Dispatcher":{"TickSeconds":1,"RetryDelaySeconds":0,"AiRole":{"TimeoutSeconds":60,"HeartbeatSeconds":10,"MaxAttempts":3}}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_role_entry_command_runs_reports_crashes_and_is_retried_through_the_command_line()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        var paths = new JasonPaths(root);
        await File.WriteAllTextAsync(paths.UserSettingsFile, Settings, Ct);

        var behaviour = Path.Combine(root, "behaviour.txt");
        var seenPids = new HashSet<int>();
        var workItems = new List<string>();

        try
        {
            AssertSuccess(await JasonAsync(root, "runtime", "start"));
            var descriptor = ReadDescriptor(paths) ?? throw new InvalidOperationException("runtime start returned success without leaving a descriptor behind.");
            seenPids.Add(descriptor.Pid);
            Assert.NotEqual(Environment.ProcessId, descriptor.Pid);

            var campaign = (string)Json(await Ok(JasonAsync(root, "campaign", "create", "--name", "Work")))["id"]!;

            var contacts = Path.Combine(root, "contacts.json");
            await File.WriteAllTextAsync(
                contacts,
                """
                [{ "first_name": "Ada", "channels": [{ "channel": "email", "value": "ada@example.test" }] }]
                """,
                Ct);
            var batch = Json(await Ok(JasonAsync(root, "campaign", "add-contacts", campaign, "--file", contacts, "--match-by", "email")));
            Assert.Equal(1, (int)batch["summary"]!["added"]!);
            var members = Json(await Ok(JasonAsync(root, "campaign", "list-contacts", campaign)));
            var contact = (string)Assert.Single(members["items"]!.AsArray())!["contact"]!["id"]!;

            // A role of the user's own, launched through a program whose behaviour the test can change
            // between attempts.
            var role = Json(await Ok(JasonAsync(
                root,
                "role",
                "add",
                "fake",
                "--entry-command",
                "dotnet",
                "--entry-command",
                FakeAgentHostLocator.Dll,
                "--entry-command",
                "script",
                "--entry-command",
                behaviour)));
            Assert.StartsWith("rol_", (string)role["id"]!, StringComparison.Ordinal);

            await File.WriteAllTextAsync(behaviour, "succeed", Ct);
            var first = Json(await Ok(JasonAsync(
                root,
                "workitem",
                "create",
                campaign,
                "--kind",
                "ai_role",
                "--role",
                "fake",
                "--contact",
                contact,
                "--context",
                "{\"icp\":\"founders\"}",
                "--result-format",
                "{\"shape\":\"summary\"}")));
            var work = (string)first["id"]!;
            workItems.Add(work);
            Assert.StartsWith("wi_", work, StringComparison.Ordinal);
            Assert.Equal("created", (string?)first["status"]);

            // A draft campaign is a backlog: the work is recorded and nothing claims it.
            Assert.False((bool)first["eligible"]!);
            Assert.Equal("active", (string?)Json(await Ok(JasonAsync(root, "campaign", "start", campaign)))["status"]);

            var done = await PollAsync(root, work, item => (string?)item["status"] == "succeeded");
            Assert.Equal("done", (string?)done["result"]!["summary"]);
            Assert.Equal(0, (int)done["attempt_count"]!);
            var attempt = Assert.Single(done["attempts"]!.AsArray())!;
            Assert.NotNull(attempt["launch"]!["pid"]);

            var chronicle = Json(await Ok(JasonAsync(root, "journal", "list", "--work-item", work)))["items"]!.AsArray()
                .Select(entry => (string?)entry!["kind"])
                .ToList();
            Assert.Contains("workitem_scheduled", chronicle);
            Assert.Contains("workitem_processing", chronicle);
            Assert.Contains("workitem_succeeded", chronicle);

            // The same role, now failing. The dispatcher gives the work back and hands it out again by itself;
            // the test only has to make the program work before the attempts run out.
            await File.WriteAllTextAsync(behaviour, "crash", Ct);
            var retried = (string)Json(await Ok(JasonAsync(
                root,
                "workitem",
                "create",
                campaign,
                "--kind",
                "ai_role",
                "--role",
                "fake",
                "--max-attempts",
                "5")))["id"]!;
            workItems.Add(retried);

            var failedOnce = await PollAsync(root, retried, item => (int)item["attempt_count"]! >= 1);
            Assert.Equal("executor_exited", (string?)failedOnce["attempts"]!.AsArray()[^1]!["error"]!["code"]);
            await File.WriteAllTextAsync(behaviour, "succeed", Ct);

            var recovered = await PollAsync(root, retried, item => (string?)item["status"] == "succeeded");
            Assert.True(recovered["attempts"]!.AsArray().Count >= 2, "The work succeeded without ever being retried.");
            Assert.Equal("done", (string?)recovered["result"]!["summary"]);

            var status = await Ok(JasonAsync(root, "runtime", "status", "--human"));
            Assert.Contains("Dispatcher: running", status.Output, StringComparison.Ordinal);

            AssertSuccess(await JasonAsync(root, "runtime", "stop"));
            Assert.False(File.Exists(paths.DescriptorFile));
            Assert.False(IsRunning(descriptor.Pid), "The runtime process was still alive after runtime stop returned.");
        }
        finally
        {
            // Whatever went wrong, nothing this test started is left running: the runtimes by descriptor, the
            // hosts by the pid each attempt recorded.
            if (ReadDescriptor(paths) is { } surviving)
            {
                seenPids.Add(surviving.Pid);
            }

            foreach (var id in workItems)
            {
                foreach (var pid in await PidsOfAsync(root, id))
                {
                    seenPids.Add(pid);
                }
            }

            foreach (var pid in seenPids)
            {
                Kill(pid);
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A handle may outlive the process that held it; a temporary directory left behind is harmless.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Reads one work item until it looks the way the test expects, because real processes take time.</summary>
    private static async Task<JsonObject> PollAsync(string root, string workItemId, Func<JsonObject, bool> expected)
    {
        var deadline = DateTime.UtcNow.Add(Patience);
        JsonObject item;
        do
        {
            item = Json(await Ok(JasonAsync(root, "workitem", "get", workItemId)));
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

    /// <summary>Every process the runtime recorded as having run an attempt of this item.</summary>
    private static async Task<IEnumerable<int>> PidsOfAsync(string root, string workItemId)
    {
        var result = await JasonAsync(root, "workitem", "get", workItemId);
        if (result.ExitCode != ExitCodes.Success)
        {
            return [];
        }

        return Json(result)["attempts"]?.AsArray()
            .Select(attempt => (int?)attempt?["launch"]?["pid"])
            .OfType<int>()
            .ToList() ?? [];
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

    /// <summary>One CLI step: the shipped executable, the isolated data directory, and everything it said.</summary>
    private static async Task<CliResult> JasonAsync(string root, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "jason.dll"));
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment[JasonPaths.DataDirectoryVariable] = root;

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

    private sealed record CliResult(int ExitCode, string Output, string Error, string Command);
}
