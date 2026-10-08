using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.App.Tests;

/// <summary>
/// Work actually being done, through the shipped executable and nothing else: a runtime started in the
/// background, a campaign filled from the command line, a role whose entry command is a real program, and a
/// dispatcher ticking on its own clock that claims the work, launches that program, and records what it
/// reported. Nothing here is in-process and nothing is driven by a test seam — this is the whole flow as a
/// user would run it.
/// </summary>
public class WorkFlowEndToEndTests
{
    /// <summary>Generous: a cold CLI process on a loaded machine is still far quicker than this.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a real child process may take to be launched, do its work and report back. Longer than the
    /// heartbeat window below, so that an attempt the runtime gives up on is still seen ending, with its reason.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A tick a second, no retry delay, and short budgets: the loop has to be visibly alive. The heartbeat window
    /// (two intervals, 40 seconds) stays inside the 60-second lease, so a missed heartbeat and a spent lease remain
    /// different endings. The window is wide because this test is about a crash being retried, and a missed
    /// heartbeat has tests of its own: once, on a loaded Windows runner, 20 seconds passed between an attempt's
    /// start and the runtime recording its child's exit, and the crash was recorded as a missed heartbeat.
    /// </summary>
    private const string Settings = """
        {"Dispatcher":{"TickSeconds":1,"RetryDelaySeconds":0,"AiRole":{"TimeoutSeconds":60,"HeartbeatSeconds":20,"MaxAttempts":3}}}
        """;

    /// <summary>
    /// Past this between an attempt's start and the child's last call, the attempt is slow enough to explain. By
    /// hand the launch and the first call take about a fifth of a second.
    /// </summary>
    private static readonly TimeSpan SlowToLastCall = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Past this between the child's last call and the attempt ending, the same. The runtime's own bound there is
    /// the five-second output grace plus one write.
    /// </summary>
    private static readonly TimeSpan SlowToEnd = TimeSpan.FromSeconds(7);

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
            AssertSuccess(await StartRuntimeAsync(root));
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
                "{\"type\":\"object\",\"description\":\"a summary\"}")));
            var work = (string)first["id"]!;
            workItems.Add(work);
            Assert.StartsWith("wi_", work, StringComparison.Ordinal);
            Assert.Equal("created", (string?)first["status"]);

            // A draft campaign is a backlog: the work is recorded and nothing claims it.
            Assert.False((bool)first["eligible"]!);
            Assert.Equal("active", (string?)Json(await Ok(JasonAsync(root, "campaign", "start", campaign)))["status"]);

            // The host reports its result through the API and exits afterwards, and the launch record (pid, exit
            // code) is written when the process has ended — so "succeeded" can be visible a moment before it.
            var done = await PollAsync(
                root,
                work,
                item => (string?)item["status"] == "succeeded" && item["attempts"]?.AsArray().FirstOrDefault()?["launch"]?["pid"] is not null);
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
            var ending = (string?)failedOnce["attempts"]!.AsArray()[^1]!["error"]!["code"];
            if (ending != "executor_exited")
            {
                Assert.Fail($"The crash ended as {ending}, not executor_exited.{Environment.NewLine}{await PhasesAsync(root, retried)}");
            }

            await File.WriteAllTextAsync(behaviour, "succeed", Ct);

            var recovered = await PollAsync(root, retried, item => (string?)item["status"] == "succeeded");
            Assert.True(recovered["attempts"]!.AsArray().Count >= 2, "The work succeeded without ever being retried.");
            Assert.Equal("done", (string?)recovered["result"]!["summary"]);

            // An attempt that ended as expected but slowly is the same stall caught early. It is reported with the
            // results rather than left for the wide heartbeat window to swallow.
            foreach (var id in workItems)
            {
                if (await SlowAttemptsAsync(root, id) is { } slow)
                {
                    Report(slow);
                }
            }

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

        Assert.Fail($"Work item {workItemId} never got where it was going: {item.ToJsonString()}{Environment.NewLine}{LogLines(root, null)}");
        return item;
    }

    /// <summary>
    /// Says that an attempt was slow where a person will see it although the test passed: in the test's own results,
    /// and in the job's summary on a GitHub runner, whose log shows nothing a passing test writes.
    /// </summary>
    private static void Report(string slow)
    {
        TestContext.Current.AddWarning(slow);
        TestContext.Current.TestOutputHelper?.WriteLine(slow);
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            File.AppendAllText(summary, $"### {nameof(WorkFlowEndToEndTests)}: a slow attempt{Environment.NewLine}```{Environment.NewLine}{slow}{Environment.NewLine}```{Environment.NewLine}");
        }
    }

    /// <summary>
    /// The phases of an item's attempts when any of them was slow (<see cref="SlowToLastCall"/>,
    /// <see cref="SlowToEnd"/>), or null when every one kept to the times a hand run shows.
    /// </summary>
    private static async Task<string?> SlowAttemptsAsync(string root, string workItemId)
    {
        var result = await JasonAsync(root, "workitem", "get", workItemId);
        if (result.ExitCode != ExitCodes.Success)
        {
            return null;
        }

        var slow = Json(result)["attempts"]!.AsArray().Any(attempt =>
        {
            var started = Moment(attempt!["started_at"]);
            var lastCall = Moment(attempt["last_heartbeat_at"]) ?? started;
            var finished = Moment(attempt["finished_at"]);
            return started is not null
                && ((lastCall - started > SlowToLastCall) || (finished - lastCall > SlowToEnd));
        });
        return slow ? $"A slow attempt of {workItemId}:{Environment.NewLine}{await PhasesAsync(root, workItemId)}" : null;
    }

    /// <summary>
    /// What each attempt of an item spent its time on — the claim, the start, the child's last call and the end —
    /// and what the runtime logged meanwhile. With this a stalled attempt names its own phase, where the error code
    /// alone can only say that time ran out.
    /// </summary>
    private static async Task<string> PhasesAsync(string root, string workItemId)
    {
        var result = await JasonAsync(root, "workitem", "get", workItemId);
        if (result.ExitCode != ExitCodes.Success)
        {
            return $"'jason workitem get {workItemId}' exited with {result.ExitCode}: {result.Error}";
        }

        var report = new StringBuilder();
        DateTimeOffset? from = null;
        DateTimeOffset? to = null;
        foreach (var attempt in Json(result)["attempts"]!.AsArray().OrderBy(attempt => (int)attempt!["number"]!))
        {
            var claimed = Moment(attempt!["claimed_at"]);
            var end = Moment(attempt["finished_at"]) ?? DateTimeOffset.UtcNow;
            if (claimed is not null && (from is null || claimed < from))
            {
                from = claimed;
            }

            if (to is null || end > to)
            {
                to = end;
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"attempt {attempt["number"]} ({attempt["status"]}, {attempt["error"]?["code"] ?? "no error"}): claimed {claimed:HH:mm:ss.fff}, started {Since(claimed, attempt["started_at"])}, last call {Since(claimed, attempt["last_heartbeat_at"])}, finished {Since(claimed, attempt["finished_at"])}; pid {attempt["launch"]?["pid"]}, exit code {attempt["launch"]?["exit_code"]}, output abandoned {attempt["launch"]?["output_abandoned"] ?? false}");
        }

        report.Append(LogLines(root, from is { } first && to is { } last ? (first.AddSeconds(-1), last.AddSeconds(1)) : null));
        return report.ToString();
    }

    /// <summary>
    /// The runtime's log lines within a window (or its last lines, without one), each cut short. The log holds
    /// requests' methods, paths, statuses and timings, ids and pids, and never a body, a context or the token.
    /// </summary>
    private static string LogLines(string root, (DateTimeOffset From, DateTimeOffset To)? window)
    {
        const int MostLines = 80;
        const int LongestLine = 400;
        var logs = new JasonPaths(root).LogsDirectory;
        if (!Directory.Exists(logs))
        {
            return "The runtime left no log.";
        }

        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(logs, "runtime-*.jsonl").Order(StringComparer.Ordinal))
        {
            // The runtime may still hold the file open for writing.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                // The test's own polling, five reads a second, would crowd the runtime's own lines out of a long stall.
                if (line.Contains("\"RequestPath\":\"/v1/workitem.get\"", StringComparison.Ordinal))
                {
                    continue;
                }

                if (window is not { } within || (InWindow(line, within.From, within.To) ?? true))
                {
                    lines.Add(line.Length > LongestLine ? line[..LongestLine] + "…" : line);
                }
            }
        }

        var shown = lines.TakeLast(MostLines).ToList();
        return $"The runtime's log, {shown.Count} of {lines.Count} lines:{Environment.NewLine}{string.Join(Environment.NewLine, shown)}";
    }

    /// <summary>Whether a log line's own timestamp falls between two moments; null when it carries none to read.</summary>
    private static bool? InWindow(string line, DateTimeOffset from, DateTimeOffset to)
    {
        try
        {
            return JsonNode.Parse(line)?["@t"] is { } stamp && Moment(stamp) is { } at ? at >= from && at <= to : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Moment(JsonNode? node) =>
        node is not null && DateTimeOffset.TryParse(node.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
            ? moment
            : null;

    private static string Since(DateTimeOffset? claimed, JsonNode? node) =>
        Moment(node) is { } at && claimed is { } start
            ? string.Create(CultureInfo.InvariantCulture, $"{(at - start).TotalSeconds:+0.000;-0.000} s")
            : "never";

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
    private static async Task<CliResult> JasonAsync(string root, TimeSpan within, params string[] args)
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

        TestRuntimeEnvironment.Offline(start.Environment, root);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The jason executable could not be started.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(Ct);
        var error = process.StandardError.ReadToEndAsync(Ct);
        var command = string.Join(' ', args);

        await process.WaitForExitAsync(Ct).WaitAsync(within, Ct);
        return new CliResult(
            process.ExitCode,
            (await output.WaitAsync(within, Ct)).Trim(),
            (await error.WaitAsync(within, Ct)).Trim(),
            command);
    }

    /// <summary>The same step within <see cref="StepTimeout"/>.</summary>
    private static Task<CliResult> JasonAsync(string root, params string[] args) => JasonAsync(root, StepTimeout, args);

    /// <summary>A real start for a test whose subject is not the start (<see cref="EndToEnd.PatientStart"/>).</summary>
    private static Task<CliResult> StartRuntimeAsync(string root) =>
        EndToEnd.PatientStart.RunAsync(
            new JasonPaths(root),
            within => JasonAsync(root, within, "runtime", "start"),
            () => JasonAsync(root, "runtime", "status"),
            result => (result.ExitCode, result.Output));

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
