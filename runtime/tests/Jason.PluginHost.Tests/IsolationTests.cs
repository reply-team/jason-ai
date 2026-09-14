using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;

namespace Jason.PluginHost.Tests;

/// <summary>
/// What a plugin cannot reach. This is interpreter isolation inside process isolation, not a sandbox — the
/// honest claim is that a plugin cannot reach the host program, its data directory or anything the user did
/// not grant it, and each of those is a test here.
/// </summary>
[Collection(ModeCollection.Name)]
public sealed class IsolationTests : IDisposable
{
    private readonly TempPackage _package = new();

    public void Dispose() => _package.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<PluginOutcome> FixtureAsync(string operation, Action<InvocationBuilder>? configure = null)
    {
        var (exit, stdout, _) = await ModeRun.RunAsync(
            InvocationFactory.Create(ModeRun.FakeProviderRoot, operation, configure: configure),
            Ct);

        Assert.Equal(HostExitCodes.Completed, exit);
        return ModeRun.Outcome(stdout);
    }

    private async Task<PluginOutcome> WrittenAsync(string mainJs, Action<InvocationBuilder>? configure = null)
    {
        InvocationFactory.WritePackage(_package.Root, mainJs);
        var (exit, stdout, _) = await ModeRun.RunAsync(InvocationFactory.Create(_package.Root, "escape.attempt", configure: configure), Ct);

        Assert.Equal(HostExitCodes.Completed, exit);
        return ModeRun.Outcome(stdout);
    }

    [Fact]
    public async Task The_host_program_is_not_there_to_be_reached()
    {
        var outcome = await FixtureAsync("escape.clr");

        var result = outcome.Result!;
        Assert.False(result["system"]!["reached"]!.GetValue<bool>());
        Assert.Equal("ReferenceError", result["import_namespace"]!["error"]!.GetValue<string>());
        Assert.False(result["clr"]!["reached"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("escape.eval")]
    [InlineData("escape.function")]
    public async Task Compiling_a_string_at_runtime_is_not_possible(string operation)
    {
        var outcome = await FixtureAsync(operation);

        Assert.Null(outcome.Result!["reached"]);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Result!["error"]!.GetValue<string>()));
    }

    [Fact]
    public async Task The_function_constructor_is_no_way_out_either()
    {
        var outcome = await FixtureAsync("escape.constructor");

        var reached = outcome.Result!["reached"]?.GetValue<string>();
        Assert.NotEqual("object", reached);
    }

    [Theory]
    [InlineData("import fs from \"fs\";")]
    [InlineData("import \"../../outside.js\";")]
    [InlineData("import \"./package.json\";")]
    public async Task Nothing_outside_the_package_can_be_imported(string import)
    {
        var outcome = await WrittenAsync(import + "\nexport function invoke() { return { result: 1 }; }");

        Assert.Equal(OutcomeStatus.Failed, outcome.Status);
        Assert.Equal(OutcomeCodes.ModuleNotAllowed, outcome.Error!.Code);
    }

    [Fact]
    public async Task There_is_no_filesystem_beyond_what_the_sdk_offers()
    {
        var outcome = await WrittenAsync("""
            export function invoke() {
              return {
                result: {
                  require: typeof require,
                  process: typeof process,
                  file: typeof File,
                  reader: typeof FileReader,
                  fetch: typeof fetch,
                  xhr: typeof XMLHttpRequest,
                  worker: typeof Worker,
                  host: Object.keys(host).join(","),
                },
              };
            }
            """);

        var result = outcome.Result!;
        Assert.Equal("undefined", result["require"]!.GetValue<string>());
        Assert.Equal("undefined", result["process"]!.GetValue<string>());
        Assert.Equal("undefined", result["file"]!.GetValue<string>());
        Assert.Equal("undefined", result["reader"]!.GetValue<string>());
        Assert.Equal("undefined", result["fetch"]!.GetValue<string>());
        Assert.Equal("undefined", result["xhr"]!.GetValue<string>());
        Assert.Equal("undefined", result["worker"]!.GetValue<string>());
        Assert.Equal("exec,http,env,log,fail", result["host"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_shell_is_not_something_a_plugin_can_start()
    {
        var outcome = await WrittenAsync(
            "export function invoke() { return { result: host.exec({ executable: \"sh\", args: [\"-c\", \"id\"] }) }; }",
            builder => builder.Grants = new InvocationGrants(
                new ExecGrants([new ExecutableGrant("dotnet", Executables.Dotnet)]),
                null,
                null));

        Assert.Equal(OutcomeStatus.Failed, outcome.Status);
        Assert.Equal(OutcomeCodes.ExecutableNotAllowed, outcome.Error!.Code);
    }

    [Fact]
    public async Task The_data_directory_cannot_even_be_located()
    {
        var variable = "JASON_DATA_DIR";
        var original = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, Path.Combine(Path.GetTempPath(), "jason-should-not-be-found"));
        try
        {
            var outcome = await WrittenAsync(
                """
                export function invoke() {
                  return { result: { data_dir: host.env("JASON_DATA_DIR") ?? null, path: host.env("PATH") ?? null } };
                }
                """,
                builder => builder.Grants = new InvocationGrants(null, null, new EnvGrants(["JASON_DATA_DIR"])));

            // Even a grant naming it cannot help: a reserved variable is never readable, and everything the
            // plugin was not granted — PATH among it — is invisible as well.
            Assert.Null(outcome.Result!["data_dir"]);
            Assert.Null(outcome.Result!["path"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    [Fact]
    public async Task A_plugin_cannot_reach_another_invocation_s_engine_or_leave_state_behind()
    {
        var mainJs = """
            let counter = 0;
            export function invoke() {
              counter++;
              return { result: { counter, global: typeof globalThis.leaked } };
            }
            """;

        var first = await WrittenAsync(mainJs);
        var second = await WrittenAsync(mainJs);

        Assert.Equal(1, first.Result!["counter"]!.GetValue<int>());
        Assert.Equal(1, second.Result!["counter"]!.GetValue<int>());
        Assert.Equal("undefined", second.Result!["global"]!.GetValue<string>());
    }
}
