using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

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
            start.Environment[JasonPaths.DataDirectoryVariable] = dataDirectory;

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
