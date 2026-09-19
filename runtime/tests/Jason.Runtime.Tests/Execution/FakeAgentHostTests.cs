using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The stand-in host, exercised on its own: no runtime answers it, so these tests pin down what it does when
/// the API is out of reach — which is the state every behaviour has to survive without crashing.
/// </summary>
public class FakeAgentHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_host_assembly_is_built_next_to_the_tests()
    {
        Assert.True(File.Exists(FakeAgentHost.Dll), $"'{FakeAgentHost.Dll}' is missing; the test project must reference Jason.FakeAgentHost.");
    }

    [Fact]
    public void The_entry_command_is_the_host_followed_by_the_behaviour()
    {
        Assert.Equal(["dotnet", FakeAgentHost.Dll, "succeed", "--heartbeats", "2"], FakeAgentHost.EntryCommand("succeed", "--heartbeats", "2"));
    }

    [Fact]
    public async Task Silent_says_what_it_is_and_leaves_without_completing()
    {
        using var dir = new TempDataDir();

        var run = await RunAsync(Envelope(dir.Paths.DescriptorFile), "silent");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("fake-agent-host: behaviour=silent", run.StandardError, StringComparison.Ordinal);
        Assert.Contains("attempt=att_one", run.StandardError, StringComparison.Ordinal);
        Assert.Contains("number=1", run.StandardError, StringComparison.Ordinal);
        Assert.Contains("token-in-env=n/a", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Crash_leaves_with_its_own_exit_code()
    {
        using var dir = new TempDataDir();

        var run = await RunAsync(Envelope(dir.Paths.DescriptorFile), "crash");

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("behaviour=crash", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreadable_envelope_is_refused_before_anything_else_happens()
    {
        var run = await RunAsync("this is not an envelope", "silent");

        Assert.Equal(64, run.ExitCode);
        Assert.Contains("the launch envelope could not be read", run.StandardError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, run.StandardOutput.Trim());
    }

    [Fact]
    public async Task An_unknown_behaviour_is_refused()
    {
        using var dir = new TempDataDir();

        var run = await RunAsync(Envelope(dir.Paths.DescriptorFile), "wave-at-the-camera");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("unknown behaviour", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Echo_envelope_writes_back_what_it_was_handed()
    {
        using var dir = new TempDataDir();
        var envelope = Envelope(dir.Paths.DescriptorFile);

        var run = await RunAsync(envelope, "echo-envelope");

        Assert.Equal(0, run.ExitCode);
        var echoed = JsonSerializer.Deserialize<LaunchEnvelope>(run.StandardOutput, JasonJson.Options)!;
        Assert.Equal(LaunchEnvelope.CurrentVersion, echoed.EnvelopeVersion);
        Assert.Equal("att_one", echoed.AttemptId);
        Assert.Equal("wi_one", echoed.WorkItemId);
        Assert.Equal(dir.Paths.DescriptorFile, echoed.Runtime.DescriptorFile);
        Assert.Equal(ApiVersion.Current, echoed.Runtime.ApiVersion);
    }

    [Fact]
    public async Task The_descriptor_is_read_for_the_token_check_when_it_is_there()
    {
        using var dir = new TempDataDir();
        WriteDescriptor(dir.Paths, "tok-4f3a-canary");

        var run = await RunAsync(Envelope(dir.Paths.DescriptorFile), "silent");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("token-in-env=false", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leak_puts_the_token_it_read_on_both_streams()
    {
        using var dir = new TempDataDir();
        WriteDescriptor(dir.Paths, "tok-4f3a-canary");

        // The completion goes to a port nothing listens on: the host reports the failure and still exits 0.
        var run = await RunAsync(Envelope(dir.Paths.DescriptorFile), "leak");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("token=tok-4f3a-canary", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("token=tok-4f3a-canary", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_api_call_that_cannot_be_made_is_reported_and_survived()
    {
        using var dir = new TempDataDir();
        WriteDescriptor(dir.Paths, "tok-4f3a-canary");

        var run = await RunAsync(Envelope(dir.Paths.DescriptorFile), "succeed");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("workitem.complete", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_runs_the_behaviour_named_in_its_file()
    {
        using var dir = new TempDataDir();
        var script = Path.Combine(dir.Paths.Root, "behaviour.txt");
        await File.WriteAllTextAsync(script, "  crash \n", Ct);

        var run = await RunAsync(Envelope(dir.Paths.DescriptorFile), "script", script);

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("behaviour=crash", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mute_says_nothing_and_runs_until_it_is_killed()
    {
        using var dir = new TempDataDir();
        var command = FakeAgentHost.EntryCommand("mute");
        using var process = Start(command);
        try
        {
            await process.StandardInput.WriteAsync(Serialize(Envelope(dir.Paths.DescriptorFile)));
            process.StandardInput.Close();

            // The one line it writes proves it started; nothing else ever arrives.
            var line = await process.StandardError.ReadLineAsync(Ct);
            Assert.Contains("behaviour=mute", line!, StringComparison.Ordinal);

            // And it keeps running well past the point where a host that had lost a runtime would have stopped.
            // This one never had one — the descriptor was never written — so it is bounded by the ceiling and
            // not by the two beats that mean abandonment.
            await Task.Delay(TimeSpan.FromSeconds(5), Ct);
            Assert.False(process.HasExited);

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(Ct);
        }
        finally
        {
            Stop(process);
        }
    }

    private static LaunchEnvelope Envelope(string descriptorFile) => new(
        LaunchEnvelope.CurrentVersion,
        "att_one",
        1,
        "wi_one",
        "cmp_one",
        null,
        WorkItemKind.AiRole,
        "researcher",
        null,
        new JsonObject { ["goal"] = "find the decision maker" },
        null,
        1800,
        120,
        new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero),
        Path.GetTempPath(),
        new RuntimeLocation(descriptorFile, ApiVersion.Current));

    private static string Serialize(LaunchEnvelope envelope) => JsonSerializer.Serialize(envelope, JasonJson.Options);

    private static void WriteDescriptor(JasonPaths paths, string token)
    {
        Directory.CreateDirectory(paths.RunDirectory);
        var descriptor = new RuntimeDescriptor(ApiVersion.Current, "0.1.0-dev", "ins_one", 1, "http://127.0.0.1:9", token, DateTimeOffset.UtcNow);
        File.WriteAllText(paths.DescriptorFile, JsonSerializer.Serialize(descriptor, JasonJson.Options));
    }

    private static Task<HostRun> RunAsync(LaunchEnvelope envelope, params string[] behaviour) => RunAsync(Serialize(envelope), behaviour);

    private static async Task<HostRun> RunAsync(string standardInput, params string[] behaviour)
    {
        using var process = Start(FakeAgentHost.EntryCommand(behaviour));
        try
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(Ct);
            var standardError = process.StandardError.ReadToEndAsync(Ct);
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();
            await process.WaitForExitAsync(Ct);
            return new HostRun(process.ExitCode, await standardOutput, await standardError);
        }
        finally
        {
            Stop(process);
        }
    }

    private static Process Start(IReadOnlyList<string> command)
    {
        var start = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        for (var index = 1; index < command.Count; index++)
        {
            start.ArgumentList.Add(command[index]);
        }

        return Process.Start(start) ?? throw new InvalidOperationException($"'{command[0]}' could not be started.");
    }

    /// <summary>No test leaves a child behind, whatever it asserted or threw.</summary>
    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone between the question and the answer.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The operating system refused the kill; the process is on its way out anyway.
        }
    }

    private sealed record HostRun(int ExitCode, string StandardOutput, string StandardError);
}
