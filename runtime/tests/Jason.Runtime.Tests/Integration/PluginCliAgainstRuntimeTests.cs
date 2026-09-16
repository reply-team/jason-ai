using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// Installing a plugin is copying a directory; everything after that is the command line. What is asserted here
/// is the whole of what a person or an agent sees while doing it: the package listed with its digest, a reload
/// that refuses a broken package and changes nothing, the diagnostics that say why, the repaired package
/// activated under a new snapshot, and the chronicle that remembers both activations.
/// </summary>
public class PluginCliAgainstRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_package_is_listed_refused_and_activated_again_from_the_command_line()
    {
        await using var fixture = await StartAsync();

        // The package was already on disk when the runtime started, so the startup load is what is listed.
        var listed = await OkAsync(fixture, "plugin", "list");
        var plugin = Assert.Single(listed["plugins"]!.AsArray())!;
        Assert.Equal(TestPlugins.FakeProviderId, (string?)plugin["id"]);
        Assert.Equal("1.0.0", (string?)plugin["version"]);
        Assert.Equal("provider", (string?)plugin["kind"]);
        Assert.Equal("valid", (string?)plugin["status"]);
        Assert.Empty(plugin["problems"]!.AsArray());
        var digest = (string)plugin["digest"]!;
        Assert.StartsWith("sha256:", digest, StringComparison.Ordinal);
        Assert.Equal(FakeProviderCli.ExecutableName, (string?)Assert.Single(plugin["capabilities"]!["exec"]!["granted"]!.AsArray()));
        var startupSnapshot = (string)listed["snapshot"]!["id"]!;
        Assert.StartsWith("snp_", startupSnapshot, StringComparison.Ordinal);

        // A package edited into nonsense. Nothing notices until somebody asks for a reload.
        var manifest = Path.Combine(fixture.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), "plugin.yaml");
        var original = await File.ReadAllTextAsync(manifest, Ct);
        await File.WriteAllTextAsync(manifest, original.Replace("kind: provider", "kind: bogus", StringComparison.Ordinal), Ct);

        var refused = await ErrorAsync(fixture, "plugin", "reload");
        Assert.Equal("plugin_reload_rejected", (string?)refused["error"]!["code"]);
        Assert.False((bool)refused["error"]!["retryable"]!);
        var detail = Assert.Single(refused["error"]!["details"]!.AsArray())!;
        Assert.Equal("fake-provider/plugin.yaml#kind", (string?)detail["field"]);
        Assert.Equal("kind_invalid", (string?)detail["code"]);

        // A refused reload changes nothing at all: the same snapshot, still usable, and a report saying why.
        var unchanged = await OkAsync(fixture, "plugin", "list");
        Assert.Equal(startupSnapshot, (string?)unchanged["snapshot"]!["id"]);
        Assert.Equal("valid", (string?)Assert.Single(unchanged["plugins"]!.AsArray())!["status"]);
        Assert.False((bool)unchanged["activated"]!);
        Assert.False((bool)unchanged["last_reload"]!["activated"]!);

        await File.WriteAllTextAsync(manifest, original, Ct);
        var activated = await OkAsync(fixture, "plugin", "reload", "--reason", "fixed");
        var reloadedSnapshot = (string)activated["snapshot"]!["id"]!;
        Assert.True((bool)activated["activated"]!);
        Assert.NotEqual(startupSnapshot, reloadedSnapshot);
        Assert.Equal(digest, (string?)Assert.Single(activated["plugins"]!.AsArray())!["digest"]);

        // Which package, at which digest, was active from when — asked without naming a campaign, because a
        // snapshot belongs to the installation rather than to any one piece of work.
        var entries = (await OkAsync(fixture, "journal", "list", "--kind", "plugins_reloaded"))["items"]!.AsArray();
        Assert.Equal(2, entries.Count);
        var reload = entries[0]!;
        Assert.Equal(reloadedSnapshot, (string?)reload["key"]);
        Assert.Equal("reload", (string?)reload["new"]!["source"]);
        Assert.Equal("fixed", (string?)reload["reason"]);
        Assert.Equal("human", (string?)reload["actor"]!["type"]);
        Assert.Equal(digest, (string?)reload["new"]!["plugins"]!.AsArray()[0]!["digest"]);
        var startup = entries[1]!;
        Assert.Equal(startupSnapshot, (string?)startup["key"]);
        Assert.Equal("startup", (string?)startup["new"]!["source"]);
        Assert.Equal("system", (string?)startup["actor"]!["type"]);
        Assert.Null((string?)startup["reason"]);
    }

    [Fact]
    public async Task The_rendering_for_people_shortens_the_digest_and_spells_out_a_refusal()
    {
        await using var fixture = await StartAsync();
        var digest = (string)Assert.Single((await OkAsync(fixture, "plugin", "list"))["plugins"]!.AsArray())!["digest"]!;
        var shown = digest["sha256:".Length..][..12];

        var status = await HumanAsync(fixture, "runtime", "status", "--human");
        Assert.Contains("Plugins:    1 active · snapshot snp_", status, StringComparison.Ordinal);

        var table = await HumanAsync(fixture, "plugin", "list", "--human");
        Assert.Contains(TestPlugins.FakeProviderId, table, StringComparison.Ordinal);
        Assert.Contains("valid", table, StringComparison.Ordinal);
        Assert.Contains("echo.run", table, StringComparison.Ordinal);

        // Enough of the digest to tell two packages apart, and no more: the whole value is in the JSON.
        Assert.Contains(shown, table, StringComparison.Ordinal);
        Assert.DoesNotContain(digest, table, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", table, StringComparison.Ordinal);

        var manifest = Path.Combine(fixture.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), "plugin.yaml");
        var original = await File.ReadAllTextAsync(manifest, Ct);
        await File.WriteAllTextAsync(manifest, original.Replace("kind: provider", "kind: bogus", StringComparison.Ordinal), Ct);
        Assert.Equal("plugin_reload_rejected", (string?)(await ErrorAsync(fixture, "plugin", "reload"))["error"]!["code"]);

        // The question a refused reload leaves behind — why is my plugin not there — is answered in the listing.
        var afterRefusal = await HumanAsync(fixture, "plugin", "list", "--human");
        Assert.Contains("last reload rejected at ", afterRefusal, StringComparison.Ordinal);
        Assert.Contains("fake-provider/plugin.yaml#kind: kind_invalid — ", afterRefusal, StringComparison.Ordinal);
    }

    private static Task<RuntimeApiFixture> StartAsync() =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.InstallFakeProvider(paths);

                // The fixture package declares the stand-in vendor CLI by its own name; the search path is the
                // real one with that program's directory in front, so what is listed as valid is valid on this
                // machine and not on a stub.
                TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"], env: ["FAKE_TOKEN"]);
            },
            configureServices: services => services.AddSingleton(TestPlugins.SearchPath));

    private static async Task<JsonObject> OkAsync(RuntimeApiFixture fixture, params string[] args)
    {
        var (exit, output, error) = await RunAsync(fixture, args);
        Assert.Equal(string.Empty, error);
        Assert.Equal(ExitCodes.Success, exit);
        return JsonNode.Parse(output)!.AsObject();
    }

    private static async Task<JsonObject> ErrorAsync(RuntimeApiFixture fixture, params string[] args)
    {
        var (exit, output, _) = await RunAsync(fixture, args);
        Assert.Equal(ExitCodes.ApiError, exit);
        return JsonNode.Parse(output)!.AsObject();
    }

    private static async Task<string> HumanAsync(RuntimeApiFixture fixture, params string[] args)
    {
        var (exit, output, error) = await RunAsync(fixture, args);
        Assert.Equal(string.Empty, error);
        Assert.Equal(ExitCodes.Success, exit);
        return output;
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(RuntimeApiFixture fixture, string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(args, new CliEnvironment(output, error, fixture.Paths), Ct);
        return (exit, output.ToString().Trim(), error.ToString().Trim());
    }
}
