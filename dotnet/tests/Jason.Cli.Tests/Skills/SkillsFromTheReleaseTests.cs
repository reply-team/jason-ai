using Jason.Cli.Doctor;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Update;
using Jason.Contracts.Skills;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// The skills verbs with the release feed as the default source: what <c>jason skills install</c> records when it is
/// given none, how <c>jason skills update</c> carries that record, and how it reads a record an installation made before
/// 0.33.0, when the default was a <c>git</c> clone of the public repository.
/// </summary>
/// <remarks>
/// Each run is a build of the version it names, with a <see cref="FakeRelease"/> of that version where the network would
/// be. No git reaches a network: where a git source is asked for, the runner records it and fails as an unreachable
/// remote would.
/// </remarks>
public class SkillsFromTheReleaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Given no source, both halves are deployed from the release's package at this build's tag, and both roots record the
    /// default source, the tag, that nobody chose it, and the commit the package names.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "skills install with no source records the release feed")]
    public async Task An_install_with_no_source_deploys_the_release_s_package_and_records_the_default()
    {
        using var dir = new TempPaths();

        var install = await AtBuildAsync(dir, "1.2.3", Release("1.2.3"), ["skills", "install", "--root", Harness(dir)]);

        Assert.True(install.Exit == ExitCodes.Success, install.Error);
        Assert.Contains(TaggedSource.Body("v1.2.3"), await File.ReadAllTextAsync(Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.Contains(TaggedSource.Body("v1.2.3"), await File.ReadAllTextAsync(Path.Combine(Harness(dir), "campaign-planning", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.NotEmpty(Deployments(dir));
        Assert.All(Deployments(dir), deployment =>
        {
            Assert.Equal(SkillsSource.DefaultSource, deployment.Source);
            Assert.Equal("v1.2.3", deployment.Ref);
            Assert.False(deployment.RefOverridden);
            Assert.Equal(SkillsPackage.Commit, deployment.Commit);
        });
    }

    /// <summary>A deployment from the default follows the build that runs now, from that build's own release.</summary>
    [Fact]
    public async Task An_update_of_a_deployment_from_the_default_moves_to_the_release_this_build_carries()
    {
        using var dir = new TempPaths();
        Assert.Equal(ExitCodes.Success, (await AtBuildAsync(dir, "1.2.3", Release("1.2.3"), ["skills", "install", "--root", Harness(dir)])).Exit);
        var later = Release("1.2.4");

        var update = await AtBuildAsync(dir, "1.2.4", later, ["skills", "update", "--root", Harness(dir)]);

        Assert.True(update.Exit == ExitCodes.Success, update.Error);
        Assert.Contains($"/releases/download/v1.2.4/{ReleaseAssets.Skills}", later.Requested);
        Assert.Contains(TaggedSource.Body("v1.2.4"), await File.ReadAllTextAsync(Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.All(Deployments(dir), deployment =>
        {
            Assert.Equal(SkillsSource.DefaultSource, deployment.Source);
            Assert.Equal("v1.2.4", deployment.Ref);
        });
    }

    /// <summary>
    /// A record an installation made before 0.33.0 with no source names the public repository as a git clone, at a ref
    /// nobody chose. A release build moves it to its own tag anyway, so it takes the packs from its own release, which
    /// publishes them, runs no git, and writes the default it has now into the record.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "a record of the earlier default moves to the release feed")]
    public async Task An_update_of_a_record_of_the_earlier_default_takes_this_build_s_release_and_records_the_default()
    {
        using var dir = new TempPaths();
        await RecordedEarlierAsync(dir, overridden: false);
        var later = Release("1.2.4");
        var git = Unreachable();

        var update = await AtBuildAsync(dir, "1.2.4", later, ["skills", "update", "--root", Harness(dir)], git);

        Assert.True(update.Exit == ExitCodes.Success, update.Error);
        Assert.Empty(git.Requested);
        Assert.Contains($"/releases/download/v1.2.4/{ReleaseAssets.Skills}", later.Requested);
        Assert.Contains(TaggedSource.Body("v1.2.4"), await File.ReadAllTextAsync(Path.Combine(Harness(dir), "operating", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.All(Deployments(dir), deployment =>
        {
            Assert.Equal(SkillsSource.DefaultSource, deployment.Source);
            Assert.Equal("v1.2.4", deployment.Ref);
            Assert.False(deployment.RefOverridden);
        });
    }

    /// <summary>
    /// A ref somebody chose stays where it was chosen, from the public repository as a git clone: no release need publish
    /// it, and an update is the same deployment repeated.
    /// </summary>
    [Fact]
    public async Task An_update_of_a_record_of_the_earlier_default_at_a_chosen_ref_stays_a_git_clone()
    {
        using var dir = new TempPaths();
        await RecordedEarlierAsync(dir, overridden: true);
        var later = Release("1.2.4");
        var git = Unreachable();

        var update = await AtBuildAsync(dir, "1.2.4", later, ["skills", "update", "--root", Harness(dir)], git);

        Assert.Equal(ExitCodes.ApiError, update.Exit);
        Assert.Empty(later.Requested);
        Assert.Contains(git.Requested, request => request.Arguments.Contains(SkillsSource.EarlierDefaultSource) && request.Arguments.Contains("v1.2.3"));
        Assert.All(Deployments(dir), deployment => Assert.Equal(SkillsSource.EarlierDefaultSource, deployment.Source));
    }

    /// <summary>
    /// A build that is not a release keeps a record of the earlier default as it is: its own tag is not published yet, so
    /// the update keeps the record's ref, which a release before 0.33.0 published no package for and a clone can take.
    /// </summary>
    [Fact]
    public async Task On_a_development_build_a_record_of_the_earlier_default_stays_a_git_clone_at_its_ref()
    {
        using var dir = new TempPaths();
        await RecordedEarlierAsync(dir, overridden: false);
        var later = Release("1.2.4");
        var git = Unreachable();

        var update = await AtBuildAsync(dir, "1.2.4-dev", later, ["skills", "update", "--root", Harness(dir)], git);

        Assert.Equal(ExitCodes.ApiError, update.Exit);
        Assert.Empty(later.Requested);
        Assert.Contains(git.Requested, request => request.Arguments.Contains(SkillsSource.EarlierDefaultSource) && request.Arguments.Contains("v1.2.3"));
    }

    /// <summary>
    /// Neither default is a source of the person's own, so a diagnostic file writes neither as <c>&lt;skill-source&gt;</c>;
    /// a release feed of the person's own, a mirror, still is.
    /// </summary>
    [Fact]
    public void Neither_default_is_a_skill_source_of_the_person_s_own()
    {
        using var dir = new TempPaths();
        var root = Harness(dir);
        const string Mirror = "https://mirror.example.test/jason/manifest.json";
        IReadOnlyList<DeployedFile> files = [new("SKILL.md", "sha256:x")];
        SkillsRecord.Write(root, new SkillsRecord(
            SkillsRecord.CurrentVersion,
            [
                new(SkillPacks.Runtime, SkillsSource.DefaultSource, "v1.2.3", false, SkillsPackage.Commit, DateTimeOffset.UnixEpoch, files),
                new(SkillPacks.Business, SkillsSource.EarlierDefaultSource, "v1.2.2", false, SkillsPackage.Commit, DateTimeOffset.UnixEpoch, files),
                new("sentinel-pack", Mirror, "v1.2.3", false, SkillsPackage.Commit, DateTimeOffset.UnixEpoch, files),
            ]));
        var met = new NamesMet();

        met.ReadPacks([root]);

        Assert.Equal([Mirror], met.SkillSources);
    }

    private static FakeRelease Release(string version)
    {
        var release = new FakeRelease(SemanticVersion.Parse(version));
        return release.WithSkills(SkillsPackage.Zip($"v{version}"));
    }

    /// <summary>
    /// The record an installation made before 0.33.0 at 1.2.3: the packs at v1.2.3, recorded from the public repository as
    /// a git clone. Made by installing from this build's release and writing the source those builds recorded in its place.
    /// </summary>
    private static async Task RecordedEarlierAsync(TempPaths dir, bool overridden)
    {
        string[] arguments = overridden ? ["skills", "install", "--ref", "v1.2.3", "--root", Harness(dir)] : ["skills", "install", "--root", Harness(dir)];
        var install = await AtBuildAsync(dir, overridden ? "1.2.4" : "1.2.3", Release("1.2.3"), arguments);
        Assert.True(install.Exit == ExitCodes.Success, install.Error);

        foreach (var root in (string[])[Harness(dir), dir.Paths.RoleSkillsDirectory])
        {
            var record = SkillsRecord.Read(root)!;
            SkillsRecord.Write(root, record with { Packs = [.. record.Packs.Select(pack => pack with { Source = SkillsSource.EarlierDefaultSource })] });
        }

        Assert.All(Deployments(dir), deployment =>
        {
            Assert.Equal(SkillsSource.EarlierDefaultSource, deployment.Source);
            Assert.Equal(overridden, deployment.RefOverridden);
        });
    }

    /// <summary>A runner that keeps what it was asked and answers as a remote nobody can reach.</summary>
    private static FakeProgramRunner Unreachable() =>
        new(_ => new ProgramResult(128, string.Empty, "fatal: unable to access the remote", false));

    private static IEnumerable<SkillsDeployment> Deployments(TempPaths dir) =>
        [.. SkillsRecord.Read(Harness(dir))?.Packs ?? [], .. SkillsRecord.Read(dir.Paths.RoleSkillsDirectory)?.Packs ?? []];

    private static string Harness(TempPaths dir) =>
        Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;

    /// <summary>The verb as a build of this version runs it, with this release where the network would be.</summary>
    private static async Task<(int Exit, string Output, string Error)> AtBuildAsync(TempPaths dir, string build, FakeRelease release, string[] arguments, IProgramRunner? programs = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(
            arguments,
            new CliEnvironment(
                output,
                error,
                dir.Paths,
                Processes: new FakeProcessControl(),
                Autostart: new RecordingRegistrar(),
                HttpHandler: release,
                Programs: programs ?? new FakeProgramRunner(request => throw new InvalidOperationException($"Nothing here may run {request.Program}.")),
                BuildVersion: build),
            Ct);

        return (exit, output.ToString(), error.ToString());
    }
}
