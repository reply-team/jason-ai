using Jason.Cli.Installation;
using Jason.Cli.Status;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The <c>update</c> check where Jason Desktop is the deb on Linux, whose updates come with the system's: it says when they
/// cannot arrive, because Jason's apt source is turned off (a release upgrade of Ubuntu turns third-party sources off),
/// and when the system installed a version this installation has not been set up for yet. Run against a package folder, a
/// source file and a data directory of the test's own.
/// </summary>
public sealed class UpdateDebTests : IDisposable
{
    private readonly TempPaths _dir = new();
    private readonly TempPaths _tree = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _dir.Dispose();
        _tree.Dispose();
    }

    [Theory]
    [InlineData("on", "0.34.0", null)]
    [InlineData("turned off", "0.34.0", "Jason's apt source, ")]
    [InlineData("gone", "0.34.0", "Jason's apt source, ")]
    [InlineData("on", "0.33.0", "Jason Desktop's package is 0.34.0, and this installation was last set up for 0.33.0")]
    public async Task On_the_deb_the_update_check_says_when_the_system_s_updates_cannot_arrive_or_are_not_finished(string source, string setUpFor, string? said)
    {
        var resources = Path.Combine(_tree.Paths.Root, "opt", "Jason", "resources");
        Directory.CreateDirectory(Path.Combine(resources, "bundle"));
        File.WriteAllText(Path.Combine(resources, "package-type"), "deb\n");
        var executable = Path.Combine(resources, "bundle", "jason");
        File.WriteAllText(executable, "jason");
        CarriedRecord.Beside(executable);
        var sources = Path.Combine(_tree.Paths.Root, "jason-desktop.sources");
        var line = "Types: deb\nURIs: https://get.jasonharness.ai/apt/\nSuites: ./\nSigned-By: /usr/share/keyrings/jason-desktop.gpg\n";
        if (source != "gone")
        {
            File.WriteAllText(sources, source == "on" ? line : line + "Enabled: no\n");
        }

        Directory.CreateDirectory(Path.Combine(_dir.Paths.Root, "desktop"));
        File.WriteAllText(Path.Combine(_dir.Paths.Root, "desktop", "machine-setup.json"), $"{{\n  \"ran_for\": \"{setUpFor}\",\n  \"ran_from\": \"{executable}\",\n  \"report\": null\n}}\n");
        var output = new StringWriter();
        var env = StatusCommandTests.Machine(_dir, output, installPath: executable) with
        {
            Setup = new SetupPlaces(SetupSystem.Linux, Path.Combine(_tree.Paths.Root, "home"), null, UserPathValue.EnvironmentKey, "/bin/bash") { AptSource = sources },
        };

        await CliApp.RunAsync(["status"], env, Ct);

        var update = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "update");
        if (said is null)
        {
            Assert.Equal(CheckState.Ok, update.State);
            return;
        }

        Assert.Equal(CheckState.Failed, update.State);
        Assert.False(update.Required);
        Assert.Contains(said, update.Fact, StringComparison.Ordinal);
        Assert.Contains(source == "on" ? "jason update apply --finish" : "Software & Updates, Other Software", update.Fact, StringComparison.Ordinal);
    }
}
