using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.App.Tests;

/// <summary>
/// The whole wave through the shipped executable: every step is a real <c>jason</c> process against an isolated
/// data directory, exactly as an agent or a script would run it. Nothing here is in-process, because
/// <c>runtime start</c> resolves "the same executable" from the running process — in-process it would launch
/// the test host instead of the runtime.
/// </summary>
public class CampaignFlowEndToEndTests
{
    /// <summary>Generous: a cold CLI process on a loaded machine is still far quicker than this.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

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
            var started = await JasonAsync(root, "runtime", "start");
            AssertSuccess(started);
            var descriptor = ReadDescriptor(paths) ?? throw new InvalidOperationException("runtime start returned success without leaving a descriptor behind.");
            seenPids.Add(descriptor.Pid);
            Assert.Equal(descriptor.InstanceId, (string?)Json(started)["instance_id"]);
            Assert.NotEqual(Environment.ProcessId, descriptor.Pid);
            Assert.True(IsRunning(descriptor.Pid), "The runtime the descriptor names is not in the process table.");

            var campaign = (string)Json(await Ok(JasonAsync(root, "campaign", "create", "--name", "Flow")))["id"]!;

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

            var batch = Json(await Ok(JasonAsync(root, "campaign", "add-contacts", campaign, "--file", contacts, "--match-by", "email")));
            Assert.Equal(2, (int)batch["summary"]!["added"]!);
            Assert.Equal("active", (string?)Json(await Ok(JasonAsync(root, "campaign", "start", campaign)))["status"]);

            // Stopping is graceful and complete: the acknowledgement names the instance, and the verb returns
            // only once the descriptor is gone and the process has left the process table.
            var stopped = Json(await Ok(JasonAsync(root, "runtime", "stop")));
            Assert.True((bool)stopped["stopping"]!);
            Assert.Equal(descriptor.InstanceId, (string?)stopped["instance_id"]);
            Assert.False(File.Exists(paths.DescriptorFile));
            Assert.False(IsRunning(descriptor.Pid), "The runtime process was still alive after runtime stop returned.");

            var status = await JasonAsync(root, "runtime", "status");
            Assert.Equal(ExitCodes.RuntimeUnavailable, status.ExitCode);
            Assert.Contains("\"code\":\"no_descriptor\"", status.Output, StringComparison.Ordinal);

            // Everything the campaign knew was on disk, so a fresh instance picks it up unchanged.
            var again = await Ok(JasonAsync(root, "runtime", "start"));
            var restarted = ReadDescriptor(paths)!;
            seenPids.Add(restarted.Pid);
            Assert.NotEqual(descriptor.InstanceId, restarted.InstanceId);
            Assert.Equal(restarted.InstanceId, (string?)Json(again)["instance_id"]);

            Assert.Equal("active", (string?)Json(await Ok(JasonAsync(root, "campaign", "get", campaign)))["status"]);
            var members = Json(await Ok(JasonAsync(root, "campaign", "list-contacts", campaign)));
            Assert.Equal(2, members["items"]!.AsArray().Count);

            await Ok(JasonAsync(root, "runtime", "stop"));

            var logs = string.Concat(await Task.WhenAll(Directory.GetFiles(paths.LogsDirectory).Select(file => ReadSharedAsync(file, Ct))));
            Assert.Contains("Shutdown requested through the API", logs, StringComparison.Ordinal);
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

        // The CLI has exited, so its streams must end with it. Waiting forever here is what a caller capturing
        // the output would do, and a runtime still holding those pipes open would hang every one of them.
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
            // Caught mid-write, or removed between the two calls; either way there is nothing to read yet.
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The runtime holds its log file open while it runs, so reading it has to accept that.</summary>
    private static async Task<string> ReadSharedAsync(string file, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
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
