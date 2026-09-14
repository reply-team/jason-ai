using System.Diagnostics;
using System.Globalization;
using Jason.PluginHost.Tests.Fixtures;

namespace Jason.PluginHost.Tests;

public class FakeProviderCliTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProcessStartInfo Start(params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(FakeProviderCli.Dll);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static async Task<(int ExitCode, string Out, string Error)> RunAsync(ProcessStartInfo info, string? stdin = null)
    {
        using var process = Process.Start(info)!;
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
        }

        process.StandardInput.Close();
        var stdout = await process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = await process.StandardError.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);
        return (process.ExitCode, stdout, stderr);
    }

    [Fact]
    public async Task Arguments_come_back_one_to_a_line_exactly_as_they_were_passed()
    {
        var (exit, stdout, _) = await RunAsync(Start("echo-args", "a", "b c"));

        Assert.Equal(0, exit);
        Assert.Equal(["a", "b c"], stdout.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public async Task An_exit_code_is_whatever_the_test_asked_for()
    {
        var (exit, _, stderr) = await RunAsync(Start("exit", "7"));

        Assert.Equal(7, exit);
        Assert.Contains("exiting", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_variable_is_printed_when_the_child_can_see_it_and_named_unset_when_it_cannot()
    {
        var present = Start("print-env", "FAKE_CLI_PROBE");
        present.Environment["FAKE_CLI_PROBE"] = "1";
        Assert.Equal("1", (await RunAsync(present)).Out.Trim());

        var absent = Start("print-env", "FAKE_CLI_PROBE");
        absent.Environment.Remove("FAKE_CLI_PROBE");
        Assert.Equal("<unset>", (await RunAsync(absent)).Out.Trim());
    }

    [Fact]
    public async Task Stdin_is_read_to_the_end_and_measured()
    {
        var (exit, stdout, _) = await RunAsync(Start("stdin-length"), "hello");

        Assert.Equal(0, exit);
        Assert.Equal("5", stdout.Trim());
    }

    [Fact]
    public async Task A_flood_can_be_asked_for_on_either_stream()
    {
        Assert.Equal(1000, (await RunAsync(Start("spew", "1000"))).Out.Length);
        Assert.Equal(1000, (await RunAsync(Start("spew", "1000", "stderr"))).Error.Length);
    }

    [Fact]
    public async Task Text_can_be_written_to_stdout_verbatim_so_a_stand_in_child_can_misbehave()
    {
        var (exit, stdout, _) = await RunAsync(Start("stdout", "{not an outcome"));

        Assert.Equal(0, exit);
        Assert.Equal("{not an outcome", stdout);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    [InlineData("-V")]
    [InlineData("version")]
    public async Task Every_version_command_answers_with_a_version(string command)
    {
        var (exit, stdout, _) = await RunAsync(Start(command));

        Assert.Equal(0, exit);
        Assert.Equal("fake-cli 1.2.3", stdout.Trim());
    }

    [Fact]
    public async Task A_version_check_can_be_recorded_so_a_test_knows_it_ran()
    {
        var record = Path.Combine(Path.GetTempPath(), "jason-fake-cli-" + Guid.NewGuid().ToString("N") + ".log");
        var info = Start("--version");
        info.Environment["FAKE_CLI_RECORD_ONE"] = record;
        try
        {
            Assert.Equal(0, (await RunAsync(info)).ExitCode);

            Assert.Single(File.ReadAllLines(record));
            Assert.Contains("argv=--version", File.ReadAllText(record), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(record);
        }
    }

    [Fact]
    public async Task A_behaviour_it_does_not_know_is_a_usage_error()
    {
        var (exit, _, stderr) = await RunAsync(Start("dance"));

        Assert.Equal(2, exit);
        Assert.Contains("unknown behaviour", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void The_apphost_sits_next_to_the_tests_so_a_search_path_can_find_it()
    {
        Assert.True(File.Exists(FakeProviderCli.ExecutablePath), FakeProviderCli.ExecutablePath);
        Assert.True(File.Exists(FakeProviderCli.Dll), FakeProviderCli.Dll);
        Assert.Equal(
            Path.Combine(FakeProviderCli.Directory, FakeProviderCli.ExecutableName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty)),
            FakeProviderCli.ExecutablePath);
    }

    [Fact]
    public async Task A_slow_program_is_slow_on_purpose()
    {
        var started = Stopwatch.StartNew();

        var (exit, stdout, _) = await RunAsync(Start("sleep", "150"));

        Assert.Equal(0, exit);
        Assert.Equal("slept", stdout.Trim());
        Assert.True(started.ElapsedMilliseconds >= 100, started.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
    }
}
