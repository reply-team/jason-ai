using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Invocation;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Tests.Plugins;

public class TestPluginsTests
{
    [Fact]
    public void The_checked_in_package_is_installed_whole()
    {
        using var dir = new TempDataDir();

        var root = TestPlugins.InstallFakeProvider(dir.Paths);

        Assert.Equal(dir.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), root);
        Assert.True(File.Exists(Path.Combine(root, "plugin.yaml")));
        Assert.True(File.Exists(Path.Combine(root, "main.js")));
        Assert.True(File.Exists(Path.Combine(root, "modules", "helper.js")));
        Assert.Contains("id: fake-provider", File.ReadAllText(Path.Combine(root, "plugin.yaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_package_installed_twice_has_the_same_digest()
    {
        using var first = new TempDataDir();
        using var second = new TempDataDir();

        var one = PackageDigest.Compute(TestPlugins.InstallFakeProvider(first.Paths));
        var two = PackageDigest.Compute(TestPlugins.InstallFakeProvider(second.Paths));

        Assert.Equal(one.Digest, two.Digest);
        Assert.Equal(3, one.FileCount);
    }

    [Fact]
    public void A_package_can_be_written_by_hand_with_its_modules()
    {
        using var dir = new TempDataDir();

        var root = TestPlugins.Write(
            dir.Paths,
            "hand-written",
            TestPlugins.Manifest("hand-written"),
            "export function invoke() { return { result: {} }; }",
            new Dictionary<string, string> { ["modules/helper.js"] = "export const x = 1;" });

        Assert.Equal("export const x = 1;", File.ReadAllText(Path.Combine(root, "modules", "helper.js")));
        Assert.Contains("id: hand-written", File.ReadAllText(Path.Combine(root, "plugin.yaml")), StringComparison.Ordinal);
        Assert.Equal(3, PackageDigest.Compute(root).FileCount);
    }

    [Fact]
    public void A_manifest_carries_the_required_fields_and_whatever_the_test_appends()
    {
        var manifest = TestPlugins.Manifest("x", "[echo.run, other.run]", "limits:\n  timeout_ms: 5000\n");

        Assert.Contains("manifest_version: 1", manifest, StringComparison.Ordinal);
        Assert.Contains("id: x", manifest, StringComparison.Ordinal);
        Assert.Contains("kind: provider", manifest, StringComparison.Ordinal);
        Assert.Contains("operations: [echo.run, other.run]", manifest, StringComparison.Ordinal);
        Assert.EndsWith("limits:\n  timeout_ms: 5000\n", manifest.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_grant_is_merged_into_the_settings_the_runtime_reads()
    {
        using var dir = new TempDataDir();

        TestPlugins.Grant(dir.Paths, "fake", env: ["A"]);
        TestPlugins.Grant(dir.Paths, "other", exec: ["dotnet"], http: ["localhost:5555"]);

        var configuration = JasonConfiguration.Build(dir.Paths, null);
        Assert.Equal(["A"], configuration.GetSection("Plugins:Grants:fake:Env").Get<string[]>()!);
        Assert.Equal(["dotnet"], configuration.GetSection("Plugins:Grants:other:Exec").Get<string[]>()!);
        Assert.Equal(["localhost:5555"], configuration.GetSection("Plugins:Grants:other:Http").Get<string[]>()!);
        Assert.Empty(configuration.GetSection("Plugins:Grants:other:Env").Get<string[]>()!);

        // The dispatcher stays off: a grant must never quietly switch the loop on under a test.
        Assert.False(configuration.GetValue<bool>("Dispatcher:Enabled"));
    }

    [Fact]
    public void A_grant_binds_into_the_options_the_runtime_resolves()
    {
        using var dir = new TempDataDir();
        TestPlugins.Grant(dir.Paths, TestPlugins.FakeProviderId, exec: ["*"], env: ["FAKE_TOKEN"]);

        var options = new PluginsOptions();
        JasonConfiguration.Build(dir.Paths, null).GetSection(PluginsOptions.Section).Bind(options);

        Assert.Equal(["*"], options.Grants[TestPlugins.FakeProviderId].Exec);
        Assert.Equal(["FAKE_TOKEN"], options.Grants[TestPlugins.FakeProviderId].Env);
        Assert.True(new PluginsOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void The_shipped_executable_and_the_stand_in_cli_are_both_next_to_the_tests()
    {
        Assert.Equal(["dotnet", Path.Combine(AppContext.BaseDirectory, "jason.dll")], new JasonDllLocator().Command);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "jason.dll")));
        Assert.True(File.Exists(FakeProviderCli.ExecutablePath), FakeProviderCli.ExecutablePath);
        Assert.Equal(AppContext.BaseDirectory, TestPlugins.FakeCliDirectory);
    }

    [Fact]
    public void A_test_search_path_answers_for_the_machine_without_being_it()
    {
        var path = new TestSearchPath { Path = TestPlugins.FakeCliDirectory };

        Assert.Equal(TestPlugins.FakeCliDirectory, path.Path);
        Assert.Equal(OperatingSystem.IsWindows(), path.PathExt is not null);
    }
}
