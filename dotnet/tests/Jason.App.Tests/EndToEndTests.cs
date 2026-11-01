using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.App.Tests;

public class EndToEndTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Runtime_process_and_cli_talk_through_the_descriptor_and_survive_a_crash()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new JasonPaths(root);

        // The first runtime's port is read again once it is gone, so it is one no operating system hands out of its own
        // choosing: a port the OS chose for a runtime another test started could otherwise be the dead one, and that
        // runtime would answer the stale descriptor.
        using var first = new RuntimeProcess(root, PortNobodyIsGiven());
        try
        {
            var descriptor = await WaitForDescriptorAsync(paths, expectedNot: null, first);
            var listening = new Uri(descriptor.BaseUrl).Port;
            Assert.True(listening < FirstPortAnOsGives, $"the first runtime listens on {listening}, which an operating system may give another program");

            var (exit, output) = await StatusAsync(paths);
            Assert.Equal(ExitCodes.Success, exit);
            var info = JsonSerializer.Deserialize<SystemInfoResponse>(output, JasonJson.Options)!;
            Assert.Equal(descriptor.InstanceId, info.InstanceId);
            Assert.Equal(first.Id, info.Pid);
            Assert.True(File.Exists(paths.DatabaseFile));
        }
        finally
        {
            await first.StopAsync();
        }

        // The descriptor is now stale: the port is dead. The CLI must say so with exit code 3.
        var (staleExit, staleOutput) = await StatusAsync(paths);
        Assert.Equal(ExitCodes.RuntimeUnavailable, staleExit);
        Assert.Contains("\"code\":\"runtime_unreachable\"", staleOutput, StringComparison.Ordinal);

        // A restart is an ordinary start: the lock was released by the OS, the descriptor is replaced.
        var previousInstance = (await ReadDescriptorAsync(paths))!.InstanceId;
        using var second = new RuntimeProcess(root);
        try
        {
            var replaced = await WaitForDescriptorAsync(paths, expectedNot: previousInstance, second);
            var (exit, output) = await StatusAsync(paths);
            Assert.Equal(ExitCodes.Success, exit);
            Assert.Contains(replaced.InstanceId, output, StringComparison.Ordinal);
        }
        finally
        {
            await second.StopAsync();
        }
    }

    /// <summary>
    /// A runtime in the foreground with no dispatch loop, whose roles are edited into a value of the wrong type before
    /// anything has asked it a thing. Nothing had read the roles at start, so the edit found nothing to keep: the
    /// runtime's answer about itself was a server error, and <c>runtime status</c> said the runtime was unreachable
    /// while it ran. It answers now, and the refusal is logged by the setting's path and type.
    /// </summary>
    [Fact]
    public async Task A_refused_roles_edit_before_anything_asked_the_runtime_leaves_it_answering()
    {
        const string sentinel = "SENTINEL_E_ROLES";
        var root = Path.Combine(Path.GetTempPath(), "jason-e2e", Guid.NewGuid().ToString("N"));
        var paths = new JasonPaths(root);
        Directory.CreateDirectory(paths.ConfigDirectory);
        await File.WriteAllTextAsync(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Roles":{"MaxSkillBytes":8192}}""", Ct);

        using var runtime = new RuntimeProcess(root);
        try
        {
            var descriptor = await WaitForDescriptorAsync(paths, expectedNot: null, runtime);
            await File.WriteAllTextAsync(paths.UserSettingsFile, "{\"Dispatcher\":{\"Enabled\":false},\"Roles\":{\"MaxSkillBytes\":\"" + sentinel + "\"}}", Ct);

            await AnsweringUntilRefusedAsync(paths, descriptor.InstanceId);
        }
        finally
        {
            await runtime.StopAsync();
        }

        var logs = Logs(paths);
        Assert.Contains(RolesRefusal, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, logs, StringComparison.Ordinal);
    }

    /// <summary>
    /// The lowest port an operating system hands out by default when a program asks for any port: 32768 on Linux, 49152 on
    /// macOS and Windows.
    /// </summary>
    private const int FirstPortAnOsGives = 32768;

    /// <summary>
    /// A free port below <see cref="FirstPortAnOsGives"/>, from a drawn start, which a runtime configured with it listens
    /// on or moves up from within its span, and stays below. A start whose whole span cannot be bound is drawn again.
    /// </summary>
    private static int PortNobodyIsGiven()
    {
        var tried = new List<int>();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var start = 20000 + Random.Shared.Next(12000);
            var port = Jason.Runtime.Hosting.PortProbe.FirstFree(start);
            if (port != 0)
            {
                return port;
            }

            tried.Add(start);
        }

        Assert.Fail($"No start below {FirstPortAnOsGives} found a free port; the starts were {string.Join(", ", tried)}.");
        return 0;
    }

    /// <summary>What the runtime logs when the roles edit above is refused.</summary>
    private const string RolesRefusal = "Roles:MaxSkillBytes could not be read as System.Int32.";

    /// <summary>
    /// Asks the runtime about itself until its log says the roles edit was refused, within
    /// <see cref="EndToEnd.GoldenPath.Step"/>, and holds every answer to being this runtime's. Asking is what reads the
    /// roles, and the edit reaches them only once the settings file's reload has run, so the refusal is said on the first
    /// question after that. A fixed two seconds before one question stood here: long past the reload's own delay on an idle
    /// machine, and no more than that on a loaded one.
    /// </summary>
    private static async Task AnsweringUntilRefusedAsync(JasonPaths paths, string instanceId)
    {
        var deadline = DateTime.UtcNow.Add(EndToEnd.GoldenPath.Step);
        while (true)
        {
            var (exit, output) = await StatusAsync(paths);
            Assert.Equal(ExitCodes.Success, exit);
            Assert.Equal(instanceId, JsonSerializer.Deserialize<SystemInfoResponse>(output, JasonJson.Options)!.InstanceId);
            if (Logs(paths).Contains(RolesRefusal, StringComparison.Ordinal))
            {
                return;
            }

            Assert.True(
                DateTime.UtcNow < deadline,
                $"The runtime's log did not say the roles edit was refused within {EndToEnd.GoldenPath.Step.TotalSeconds:F0} s.");
            await Task.Delay(100, Ct);
        }
    }

    /// <summary>Every runtime log file in the data directory, read beside the runtime still writing it.</summary>
    private static string Logs(JasonPaths paths) =>
        Directory.Exists(paths.LogsDirectory)
            ? string.Join('\n', Directory.EnumerateFiles(paths.LogsDirectory, "runtime-*.jsonl").Select(RuntimeLogs.ReadAllText))
            : string.Empty;

    private static async Task<RuntimeDescriptor> WaitForDescriptorAsync(JasonPaths paths, string? expectedNot, RuntimeProcess process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            var descriptor = await ReadDescriptorAsync(paths);
            if (descriptor is not null && descriptor.InstanceId != expectedNot)
            {
                return descriptor;
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException($"The runtime process exited with code {process.ExitCode} before publishing a descriptor. Its output was:{Environment.NewLine}{process.Diagnostics}");
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException($"The runtime did not publish a descriptor in time. Its output was:{Environment.NewLine}{process.Diagnostics}");
    }

    private static async Task<RuntimeDescriptor?> ReadDescriptorAsync(JasonPaths paths)
    {
        if (!File.Exists(paths.DescriptorFile))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RuntimeDescriptor>(await File.ReadAllBytesAsync(paths.DescriptorFile, Ct), JasonJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<(int Exit, string Output)> StatusAsync(JasonPaths paths)
    {
        var output = new StringWriter();
        var exit = await CliApp.RunAsync(["runtime", "status"], new CliEnvironment(output, new StringWriter(), paths), Ct);
        return (exit, output.ToString());
    }

    /// <summary>
    /// The runtime under test as a real child process, with its console output drained so a full pipe can
    /// never wedge it and a failing test can show what the runtime said.
    /// </summary>
    /// <summary>
    /// A spawned runtime really is offline: it says so itself, in its own log, from inside the child process.
    /// </summary>
    /// <remarks>
    /// The eight places that start a real runtime turn the update check off by putting
    /// <c>JASON_Update__CheckEnabled=false</c> in the child's environment, and until now nothing checked that
    /// this arrives: that a double underscore binds to a nested key, that the <c>JASON_</c> prefix is read at
    /// all, and that the setting reaches the checker rather than being bound too late to matter are three
    /// facts about the configuration system, and a suite's promise not to reach the network should not rest on
    /// somebody's reading of a contract. The runtime writes one line when the check is off; this reads it out
    /// of the child's own logs directory.
    /// </remarks>
    [Fact]
    public async Task A_spawned_runtime_says_in_its_own_log_that_it_will_not_ask_the_release_feed()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new JasonPaths(root);

        using var runtime = new RuntimeProcess(root);
        try
        {
            await WaitForDescriptorAsync(paths, expectedNot: null, runtime);
        }
        finally
        {
            await runtime.StopAsync();
        }

        var logs = Directory.EnumerateFiles(paths.LogsDirectory, "runtime-*.jsonl").ToList();
        var written = string.Join('\n', logs.Select(RuntimeLogs.ReadAllText));

        Assert.Contains("Update check is disabled by configuration", written, StringComparison.Ordinal);

        // And it notifies nobody: the notifications switch travels the same way and says so in the same log.
        Assert.Contains("Notifications are off by configuration; nothing will be sent", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Update check failed", written, StringComparison.Ordinal);
        Assert.DoesNotContain("releases/latest/download", written, StringComparison.Ordinal);
    }

    private sealed class RuntimeProcess : IDisposable
    {
        private readonly StringBuilder _diagnostics = new();
        private readonly Process _process;

        /// <param name="dataDirectory">The runtime's data directory.</param>
        /// <param name="port">The port it is configured with; null for one the OS chooses, as every spawned test runtime has.</param>
        public RuntimeProcess(string dataDirectory, int? port = null)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "jason.dll"));
            start.ArgumentList.Add("runtime");
            start.ArgumentList.Add("run");
            TestRuntimeEnvironment.Offline(start.Environment, dataDirectory);
            if (port is { } configured)
            {
                start.Environment[TestRuntimeEnvironment.PortVariable] = configured.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the runtime process.");
            _process.OutputDataReceived += Collect;
            _process.ErrorDataReceived += Collect;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public int Id => _process.Id;

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public string Diagnostics
        {
            get
            {
                lock (_diagnostics)
                {
                    return _diagnostics.ToString();
                }
            }
        }

        public async Task StopAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between the check and the kill; nothing left to stop.
            }

            await _process.WaitForExitAsync(Ct);
        }

        public void Dispose() => _process.Dispose();

        private void Collect(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                return;
            }

            lock (_diagnostics)
            {
                _diagnostics.AppendLine(e.Data);
            }
        }
    }
}
