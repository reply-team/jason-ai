using System.Globalization;
using Jason.Cli.Process;
using Jason.Contracts;
using Jason.Contracts.Update;

namespace Jason.Cli.Skills;

/// <summary>
/// The packs could not be got at: no release has published the pinned ref, the source is a remote one and this
/// environment has no program runner, or <c>git</c> refused.
/// </summary>
/// <remarks>
/// Its message names what to do next. A git error on its own is not something an operator can act on, and the
/// commonest case here is the most confusing one: the pin is a tag that does not exist yet.
/// </remarks>
public sealed class SkillsSourceUnavailable(string message) : Exception(message);

/// <summary>Where the packs are, once they are somewhere this machine can read them.</summary>
/// <param name="Commit">
/// The commit staged, or null for a directory read where it stands: a working tree has no commit, and
/// recording one would say a deployment came from a state nothing can go back to.
/// </param>
public sealed record StagedSource(string Directory, string Source, string Ref, bool RefOverridden, string? Commit);

/// <summary>
/// Where skills come from: git, never the release bundle.
/// </summary>
/// <remarks>
/// <para>
/// A release archive is frozen at a version, and the business pack is refreshed on a cadence that has nothing
/// to do with runtime releases — a person who installed in March should be able to take this month's practice
/// without updating the runtime. Pulling from git is also what makes installation work before any release
/// exists, which today is the only way it can work at all.
/// </para>
/// <para>
/// From a pinned ref and never a moving branch, so that two people installing on the same day get the same
/// texts and "is this current?" has an answer.
/// </para>
/// </remarks>
public static class SkillsSource
{
    /// <summary>The repository this build's skills come from when nobody says otherwise.</summary>
    public const string DefaultSource = "https://github.com/reply-team/jason-ai.git";

    /// <summary>How long a fetch is given before it is stopped.</summary>
    public static TimeSpan FetchTimeout => TimeSpan.FromMinutes(2);

    /// <summary>
    /// The ref this build is pinned to: <c>v</c> and its own release version.
    /// </summary>
    /// <remarks>
    /// Derived rather than written down, because a literal goes stale one release after somebody stops thinking
    /// about it. Through <see cref="SemanticVersion"/>, which is the rule this reuses rather than restates: it
    /// is what already decides that <c>0.1.0-dev</c> is older than <c>0.1.0</c>, and the pre-release it parses
    /// off is the same suffix dropped here — so a development build points at the release it is on the way to.
    /// </remarks>
    public static string DefaultRef(string runtimeVersion) =>
        SemanticVersion.TryParse(runtimeVersion, out var version)
            ? string.Create(CultureInfo.InvariantCulture, $"v{version.Major}.{version.Minor}.{version.Patch}")
            : throw new SkillsSourceUnavailable(
                $"This build reports its version as '{runtimeVersion}', which is not a version, so it carries no pinned ref. Name one with --ref, or a directory with --source.");

    /// <summary>
    /// The source, resolved to a directory on this machine. A directory is read where it stands; anything else
    /// is cloned by <c>git</c> at the ref, into the data directory, through the program seam — so an
    /// environment with no runner refuses rather than reaching for the network.
    /// </summary>
    public static async Task<StagedSource> StageAsync(
        CliEnvironment env,
        string? source,
        string? reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var overridden = !string.IsNullOrWhiteSpace(reference);
        var where = string.IsNullOrWhiteSpace(source) ? DefaultSource : source;
        var pin = overridden ? reference! : DefaultRef(JasonVersion.Current);

        if (Directory.Exists(where))
        {
            var local = Path.GetFullPath(where);
            RequirePacks(local, where);
            return new StagedSource(local, where, overridden ? pin : "(a directory, read where it stands)", overridden, null);
        }

        if (env.Programs is not { } runner)
        {
            throw new SkillsSourceUnavailable(
                $"'{where}' is not a directory on this machine and this environment cannot run git, so it cannot be fetched. Name a directory with --source.");
        }

        var staged = env.Paths.SkillsStagedSourceDirectory(Sanitized(pin));
        if (Directory.Exists(staged))
        {
            Directory.Delete(staged, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);

        var clone = await runner.RunAsync(
            "git",
            ["clone", "--depth", "1", "--branch", pin, "--", where, staged],
            null,
            FetchTimeout,
            cancellationToken).ConfigureAwait(false);

        if (clone.TimedOut)
        {
            throw new SkillsSourceUnavailable($"Fetching '{where}' at '{pin}' did not finish in {FetchTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes.");
        }

        if (clone.ExitCode != 0)
        {
            // Its own words, because "git failed" is not something anybody can act on -- and the commonest
            // case is the one this build creates: the pin is a release tag that has not been published yet.
            throw new SkillsSourceUnavailable(
                $"'{where}' could not be fetched at '{pin}'. git said: {clone.StandardError.Trim()}"
                + (overridden ? string.Empty : $" This build's pinned ref is '{pin}'; if no release has published it yet, install from a directory with --source."));
        }

        RequirePacks(staged, where);

        var head = await runner.RunAsync("git", ["rev-parse", "HEAD"], staged, FetchTimeout, cancellationToken).ConfigureAwait(false);
        var commit = head.ExitCode == 0 ? head.StandardOutput.Trim() : null;
        return new StagedSource(staged, where, pin, overridden, commit);
    }

    /// <summary>Whether what was staged is a thing with packs in it, said before anything downstream looks.</summary>
    private static void RequirePacks(string directory, string source)
    {
        var skills = Path.Combine(directory, "skills");
        if (!Directory.Exists(skills) || Directory.GetDirectories(skills).Length == 0)
        {
            throw new SkillsSourceUnavailable($"'{source}' holds no skills directory, so there is nothing here to install.");
        }
    }

    /// <summary>
    /// A ref as a directory name. A ref can hold slashes, and this composes a path from it — so what is kept is
    /// the characters a ref is allowed to have that a path also is, and nothing that could walk anywhere.
    /// </summary>
    private static string Sanitized(string reference) =>
        new([.. reference.Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-')]);
}
