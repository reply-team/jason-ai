using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Update;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// <c>jason update apply</c> and <c>jason update rollback</c> on an installation a package manager owns. The manager keeps
/// its own record of the version, and these verbs would replace a file it owns behind its back — so they refuse, before
/// anything is staged, drained or stopped, and say what to type instead.
/// </summary>
/// <remarks>
/// The winget facts rest on the record of a real winget install that <see cref="ManagedLayouts"/> states; the Homebrew
/// facts on a planted keg.
/// </remarks>
public sealed class UpdateManagedTests : IDisposable
{
    private readonly ManagedLayouts _layouts = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _layouts.Dispose();

    [Theory]
    [InlineData("Homebrew")]
    [InlineData("winget")]
    public async Task Apply_refuses_a_managed_installation_before_anything_is_staged_drained_or_stopped(string manager)
    {
        using var installation = new FakeInstallation().WithRuntime();
        var (env, executable) = Managed(installation, manager);
        var before = File.ReadAllText(executable);

        var exit = await CliApp.RunAsync(["update", "apply", "--feed", installation.Feed.ToString(), "--drain-seconds", "0"], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_managed_by_package_manager", refusal.Code);

        // Nothing staged, nothing fetched, no ledger, the runtime neither drained nor stopped, and the manager's file as
        // it was: the refusal came before anything at all.
        Assert.False(Directory.Exists(installation.Update.Root), "something was written under the data directory's update directory");
        Assert.Null(installation.Ledger());
        Assert.Empty(installation.Fetched);
        Assert.Empty(installation.Operations);
        Assert.True(installation.Running, "the runtime was stopped for an update that was refused");
        Assert.Equal(before, File.ReadAllText(executable));

        Assert.False(refusal.Retryable, "the same manager will own the same installation next time");
        Assert.Contains($"installed by {manager}", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("`jason update apply` would replace a file the package manager owns", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// What to type instead is three lines and never one: the runtime stopped, the manager's upgrade, the runtime started.
    /// The manager knows nothing of the runtime Jason keeps running, and replaces the image it starts plugin hosts from.
    /// </summary>
    [Theory]
    [InlineData("Homebrew", "brew upgrade jason")]
    [InlineData("winget", "winget upgrade Reply.Jason")]
    public async Task The_refusal_gives_the_sequence_to_type_as_three_lines(string manager, string upgrade)
    {
        using var installation = new FakeInstallation().WithRuntime();
        var (env, _) = Managed(installation, manager);

        await CliApp.RunAsync(["update", "apply", "--feed", installation.Feed.ToString()], env, Ct);

        var message = RuntimeVerbs.Envelope(installation.Out.ToString()).Message;
        Assert.Contains($"  jason runtime stop\n  {upgrade}\n  jason runtime start", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rollback would put back a file the manager does not know about, so it refuses the same way: a record an earlier
    /// update of Jason's own left behind stays as it is, and so does the runtime.
    /// </summary>
    [Theory]
    [InlineData("Homebrew", "brew upgrade jason")]
    [InlineData("winget", "winget upgrade Reply.Jason")]
    public async Task Rollback_refuses_a_managed_installation_and_touches_nothing(string manager, string upgrade)
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.Killed(UpdateStep.Complete);
        var ledger = File.ReadAllText(installation.Update.Ledger);
        var operations = installation.Operations.Count;
        var (env, executable) = Managed(installation, manager);
        var before = File.ReadAllText(executable);

        var exit = await CliApp.RunAsync(["update", "rollback"], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(installation.Out.ToString());
        Assert.Equal("update_managed_by_package_manager", refusal.Code);
        Assert.Contains("`jason update rollback` would replace a file the package manager owns", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"  jason runtime stop\n  {upgrade}\n  jason runtime start", refusal.Message, StringComparison.Ordinal);

        Assert.Equal(ledger, File.ReadAllText(installation.Update.Ledger));
        Assert.True(File.Exists(installation.Update.PreviousExecutable), "the executable an earlier update kept was moved");
        Assert.Equal(operations, installation.Operations.Count);
        Assert.True(installation.Running, "the runtime was stopped for a rollback that was refused");
        Assert.Equal(before, File.ReadAllText(executable));
    }

    /// <summary>The same installation, named as the manager's file, with the manager's record beside it.</summary>
    private (CliEnvironment Env, string Executable) Managed(FakeInstallation installation, string manager)
    {
        if (manager == "Homebrew")
        {
            var brew = _layouts.Homebrew();
            return (installation.Env with { InstallPath = brew.Executable, Managers = new PlantedRecords() }, brew.Executable);
        }

        var winget = _layouts.Winget();
        return (installation.Env with { InstallPath = winget.Executable, Managers = new PlantedRecords(winget.Registration()) }, winget.Executable);
    }
}
