using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// What the machine answers for a program a manifest declares. Everything here is a problem of the environment,
/// never of the package: a missing vendor CLI holds its own plugin back and leaves every other plugin alone.
/// </summary>
public class ExecutableResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyDictionary<string, string> NoEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void A_declared_program_resolves_to_the_file_the_search_path_holds()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });

        var resolved = resolver.Resolve(new ExecutableRequest(FakeProviderCli.ExecutableName, null, null), 0);

        Assert.Null(resolved.Problem);
        Assert.Equal(FakeProviderCli.ExecutablePath, resolved.Path);

        // Presence is all a resolution answers: running the program is a separate decision the user makes.
        Assert.Null(resolved.Version);
    }

    [Fact]
    public void A_program_nobody_installed_is_a_problem_of_the_machine()
    {
        using var programs = new TestPrograms();
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });

        var resolved = resolver.Resolve(new ExecutableRequest("not-installed-anywhere", null, null), 2);

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableMissing, resolved.Problem!.Code);
        Assert.Equal("capabilities.exec.executables[2].name", resolved.Problem.Path);
        Assert.True(resolved.Problem.Environmental);
    }

    [Fact]
    public void An_empty_search_path_holds_nothing()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = null });

        Assert.Equal(ProblemCodes.ExecutableMissing, resolver.Resolve(new ExecutableRequest("reply", null, null), 0).Problem!.Code);
    }

    [Fact]
    public void The_search_path_is_walked_in_order()
    {
        using var first = new TestPrograms();
        using var second = new TestPrograms();
        var wanted = first.AddProgram("reply");
        second.AddProgram("reply");
        var resolver = new ExecutableResolver(new TestSearchPath { Path = first.Root + Path.PathSeparator + second.Root });

        Assert.Equal(wanted, resolver.Resolve(new ExecutableRequest("reply", null, null), 0).Path);
    }

    [Fact]
    public void A_file_that_only_a_shell_could_start_is_refused()
    {
        using var programs = new TestPrograms();
        programs.Add("tool.cmd");
        programs.Add("tool.bat");
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });

        var resolved = resolver.Resolve(new ExecutableRequest("tool", null, null), 0);

        Assert.Null(resolved.Path);

        // Starting a .cmd means cmd.exe interprets the arguments — the very shell the invariant excludes. On
        // every other system the name simply does not exist.
        Assert.Equal(
            OperatingSystem.IsWindows() ? ProblemCodes.ExecutableNotRunnable : ProblemCodes.ExecutableMissing,
            resolved.Problem!.Code);
    }

    [Fact]
    public void A_file_nobody_may_execute_is_not_a_program()
    {
        using var programs = new TestPrograms();
        programs.Add("tool" + (OperatingSystem.IsWindows() ? ".txt" : string.Empty));
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });

        Assert.Equal(ProblemCodes.ExecutableMissing, resolver.Resolve(new ExecutableRequest("tool", null, null), 0).Problem!.Code);
    }

    [Fact]
    public async Task A_version_command_reports_the_version_the_program_prints()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, null, ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, NoEnvironment, TimeSpan.FromSeconds(30), Ct);

        Assert.Null(result.Problem);
        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public async Task A_program_older_than_the_manifest_asks_for_is_incompatible()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, "2.0.0", ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 1), request, 1, NoEnvironment, TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(ProblemCodes.ExecutableIncompatible, result.Problem!.Code);
        Assert.Equal("capabilities.exec.executables[1].name", result.Problem.Path);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("2.0.0", result.MinVersion);
    }

    [Fact]
    public async Task A_program_new_enough_passes()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, "1.0.0", ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, NoEnvironment, TimeSpan.FromSeconds(30), Ct);

        Assert.Null(result.Problem);
        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public async Task A_program_that_answers_with_no_version_fails_the_check()
    {
        using var programs = new TestPrograms();
        programs.AddProgram("tool");
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });
        var request = new ExecutableRequest("tool", null, ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, NoEnvironment, TimeSpan.FromSeconds(30), Ct);

        // The file is not a program at all; whether starting it fails or it answers with nothing, the check did
        // not produce a version and the plugin is held back rather than the package blamed.
        Assert.Equal(ProblemCodes.ExecutableVersionCheckFailed, result.Problem!.Code);
        Assert.True(result.Problem.Environmental);
    }

    [Fact]
    public async Task A_check_that_runs_out_of_time_answers_rather_than_throws()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, null, ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, NoEnvironment, TimeSpan.FromMilliseconds(1), Ct);

        // A fast machine may still finish inside a millisecond; what matters is that a reload is never held up
        // by a program that does not answer.
        Assert.True(result.Problem is null || result.Problem.Code == ProblemCodes.ExecutableVersionCheckFailed);
    }

    [Fact]
    public async Task A_program_that_was_never_found_is_never_started()
    {
        using var programs = new TestPrograms();
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });
        var request = new ExecutableRequest("not-installed-anywhere", null, ["--version"]);
        var missing = resolver.Resolve(request, 0);

        var result = await resolver.CheckVersionAsync(missing, request, 0, NoEnvironment, TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(ProblemCodes.ExecutableMissing, result.Problem!.Code);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task The_resultprogram_sees_only_the_environment_it_was_handed()
    {
        var record = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, null, ["-v"]);
        var environment = BaseEnvironment.Build(PluginLoader.CurrentEnvironment(), []);
        environment["FAKE_CLI_RECORD_HANDED"] = record;

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, environment, TimeSpan.FromSeconds(30), Ct);

        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("argv=-v", (await File.ReadAllLinesAsync(record, Ct))[0].Split(' ')[0]);
        File.Delete(record);
    }
}
