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

        using var first = new RuntimeProcess(root);
        try
        {
            var descriptor = await WaitForDescriptorAsync(paths, expectedNot: null, first);

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
        var written = string.Join('\n', logs.Select(File.ReadAllText));

        Assert.Contains("Update check is disabled by configuration", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Update check failed", written, StringComparison.Ordinal);
        Assert.DoesNotContain("releases/latest/download", written, StringComparison.Ordinal);
    }

    private sealed class RuntimeProcess : IDisposable
    {
        private readonly StringBuilder _diagnostics = new();
        private readonly Process _process;

        public RuntimeProcess(string dataDirectory)
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
