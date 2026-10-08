using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.App.Tests;

/// <summary>
/// The whole campaign flow through the shipped executable: every step is a real <c>jason</c> process against an isolated
/// data directory, exactly as an agent or a script would run it. Nothing here is in-process, because
/// <c>runtime start</c> resolves "the same executable" from the running process — in-process it would launch
/// the test host instead of the runtime.
/// </summary>
public class CampaignFlowEndToEndTests
{
    /// <summary>Generous: a cold CLI process on a loaded machine is still far quicker than this.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How much of the runtime's log a failing step carries in its message.</summary>
    private const int LogTailLines = 40;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_campaign_survives_stopping_and_starting_the_runtime_from_the_command_line()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-flow", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new JasonPaths(root);
        var seenPids = new HashSet<int>();

        try
        {
            // Nothing is running yet: the runtime is launched in the background and start returns only once it
            // answers for itself.
            var started = await StartRuntimeAsync(root);
            AssertSuccess(started, root);
            var descriptor = ReadDescriptor(paths) ?? throw new InvalidOperationException("runtime start returned success without leaving a descriptor behind.");
            seenPids.Add(descriptor.Pid);
            Assert.Equal(descriptor.InstanceId, (string?)Json(started)["instance_id"]);
            Assert.NotEqual(Environment.ProcessId, descriptor.Pid);
            Assert.True(IsRunning(descriptor.Pid), "The runtime the descriptor names is not in the process table.");

            var campaign = (string)Json(await Ok(root, "campaign", "create", "--name", "Flow"))["id"]!;

            var contacts = Path.Combine(root, "contacts.json");
            await File.WriteAllTextAsync(
                contacts,
                """
                [
                  { "first_name": "Ana", "channels": [{ "channel": "email", "value": "ana@example.test" }] },
                  { "first_name": "Ben", "channels": [{ "channel": "email", "value": "ben@example.test" }] }
                ]
                """,
                Ct);

            var batch = Json(await Ok(root, "campaign", "add-contacts", campaign, "--file", contacts, "--match-by", "email"));
            Assert.Equal(2, (int)batch["summary"]!["added"]!);
            Assert.Equal("active", (string?)Json(await Ok(root, "campaign", "start", campaign))["status"]);

            // Stopping is graceful and complete: the acknowledgement names the instance, the verb returns only once
            // the process has left the process table, and the runtime removed its descriptor before it exited.
            var stopped = Json(await Ok(root, "runtime", "stop"));
            Assert.True((bool)stopped["stopping"]!);
            Assert.Equal(descriptor.InstanceId, (string?)stopped["instance_id"]);
            Assert.False(File.Exists(paths.DescriptorFile));
            Assert.False(IsRunning(descriptor.Pid), "The runtime process was still alive after runtime stop returned.");

            var status = await JasonAsync(root, "runtime", "status");
            Assert.Equal(ExitCodes.RuntimeUnavailable, status.ExitCode);
            Assert.Contains("\"code\":\"no_descriptor\"", status.Output, StringComparison.Ordinal);

            // Everything the campaign knew was on disk, so a fresh instance picks it up unchanged.
            var again = await StartRuntimeAsync(root);
            AssertSuccess(again, root);
            var restarted = ReadDescriptor(paths)!;
            seenPids.Add(restarted.Pid);
            Assert.NotEqual(descriptor.InstanceId, restarted.InstanceId);
            Assert.Equal(restarted.InstanceId, (string?)Json(again)["instance_id"]);

            Assert.Equal("active", (string?)Json(await Ok(root, "campaign", "get", campaign))["status"]);
            var members = Json(await Ok(root, "campaign", "list-contacts", campaign));
            Assert.Equal(2, members["items"]!.AsArray().Count);

            await Ok(root, "runtime", "stop");

            var logs = string.Concat(Directory.GetFiles(paths.LogsDirectory).Select(RuntimeLogs.ReadAllText));
            Assert.Contains("Shutdown requested through the API", logs, StringComparison.Ordinal);

            // What each runtime found attached to it, read from the file as the file carries it. `runtime start`
            // launches the runtime with no console window, which on Windows still hands it a console: the
            // detached runtime lets go of it and says freed_hidden, once per start. This is what holds both the
            // hand-over from the mode router and the half of "attached" that a windowless console answers to.
            // Without the hand-over no line is written at all; without that half, a runtime started this way said
            // "none" and kept its console. Elsewhere nothing is said, because there is no console to speak of.
            var consoles = logs
                .Split('\n')
                .Where(line => line.TrimStart().StartsWith('{'))
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@mt"] == Jason.Runtime.Hosting.ProcessDetacher.LogTemplate)
                .Select(line => (string?)line["Console"])
                .ToList();
            string?[] expected = OperatingSystem.IsWindows() ? ["freed_hidden", "freed_hidden"] : [];
            Assert.Equal(expected, consoles);
        }
        finally
        {
            // Whatever went wrong, no runtime this test started is left running: the descriptor on disk names
            // the one still alive, and the set holds every one that ever was.
            if (ReadDescriptor(paths) is { } surviving)
            {
                seenPids.Add(surviving.Pid);
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

    /// <summary>One CLI step that must succeed.</summary>
    private static async Task<CliResult> Ok(string root, params string[] args)
    {
        var result = await JasonAsync(root, args);
        AssertSuccess(result, root);
        return result;
    }

    /// <summary>
    /// A step that fails says what the CLI printed and what the runtime logged. The CLI's own words are often only
    /// "the runtime did not answer"; the reason is in the runtime's log, and on a CI runner nothing else survives the
    /// run to be read afterwards. The message is put together only for a step that failed: reading the log for every
    /// step that passed would cost every run a read of a file it never shows.
    /// </summary>
    private static void AssertSuccess(CliResult result, string root)
    {
        if (result.ExitCode != ExitCodes.Success || result.Error.Length != 0)
        {
            Assert.Fail($"'jason {result.Command}' exited with {result.ExitCode}.{Environment.NewLine}stdout: {result.Output}{Environment.NewLine}stderr: {result.Error}{Environment.NewLine}{RuntimeLogTail(root)}");
        }
    }

    /// <summary>
    /// The last lines of the newest runtime log under the data directory, read the way a writer still holding the file
    /// allows. Building a failure message must never replace the failure, so a log that cannot be read is reported
    /// as that instead of throwing.
    /// </summary>
    private static string RuntimeLogTail(string root)
    {
        var logs = new JasonPaths(root).LogsDirectory;
        string? newest = null;
        try
        {
            // Listing the directory can fail as reading the file can, and either is reported rather than thrown.
            newest = Directory.Exists(logs)
                ? Directory.GetFiles(logs, "runtime-*.jsonl").Order(StringComparer.Ordinal).LastOrDefault()
                : null;
            if (newest is null)
            {
                return $"no log file under {logs}";
            }

            var lines = RuntimeLogs.ReadAllText(newest)
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Length > 0)
                .TakeLast(LogTailLines);
            return $"last {LogTailLines} lines of {newest}:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
        }
        catch (IOException exception)
        {
            return $"the log {newest ?? logs} could not be read: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            return $"the log {newest ?? logs} could not be read: {exception.Message}";
        }
    }

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

        try
        {
            await process.WaitForExitAsync(Ct).WaitAsync(within, Ct);

            // The CLI has exited, so its streams must end with it. Waiting forever here is what a caller capturing
            // the output would do, and a runtime still holding those pipes open would hang every one of them.
            return new CliResult(
                process.ExitCode,
                (await output.WaitAsync(within, Ct)).Trim(),
                (await error.WaitAsync(within, Ct)).Trim(),
                command);
        }
        catch (TimeoutException timeout)
        {
            // A step that never finishes has said nothing yet, so the runtime's log is the only account there is of
            // what it was waiting for.
            throw new TimeoutException(
                $"'jason {command}' did not finish within {within} (the process {(process.HasExited ? "had" : "had not")} exited); " +
                $"its stdout and stderr were not complete and are not shown.{Environment.NewLine}{RuntimeLogTail(root)}",
                timeout);
        }
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
            // Caught mid-write, or removed between the two calls; either way there is nothing to read yet.
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
