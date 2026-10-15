using System.Globalization;
using Jason.Cli.Process;
using Jason.Contracts;
using Jason.Contracts.Update;
using Jason.Contracts.Skills;

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
/// without updating the runtime. And a build from source has a working tree of its own to take them from, so
/// its skills never depend on a release existing at all: <c>--source</c> names that tree.
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
    /// is what already puts a <c>-dev</c> build before the release of its number, and the pre-release it parses
    /// off is the same suffix dropped here — so a development build points at the release it is on the way to.
    /// </remarks>
    public static string DefaultRef(string runtimeVersion) =>
        SemanticVersion.TryParse(runtimeVersion, out var version)
            ? string.Create(CultureInfo.InvariantCulture, $"v{version.Major}.{version.Minor}.{version.Patch}")
            : throw new SkillsSourceUnavailable(
                $"This build reports its version as '{runtimeVersion}', which is not a version, so it carries no pinned ref. Name one with --ref, or a directory with --source.");

    /// <summary>
    /// The version this build goes by: its own stamp, or the one a test named through the environment.
    /// </summary>
    public static string BuildVersion(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        return env.BuildVersion ?? JasonVersion.Current;
    }

    /// <summary>
    /// Whether a version is a release: one that parses, with no pre-release after it. A development build's pin names
    /// the release it is on its way to, which is not published yet, so nothing moves a deployment to it or calls a
    /// deployment stale against it.
    /// </summary>
    public static bool IsRelease(string version) =>
        SemanticVersion.TryParse(version, out var parsed) && parsed.PreRelease is null;

    /// <summary>
    /// The source, resolved to a directory on this machine. A directory is read where it stands; anything else
    /// is cloned by <c>git</c> at the ref, into the data directory, through the program seam — so an
    /// environment with no runner refuses rather than reaching for the network.
    /// </summary>
    /// <remarks>
    /// The same for a dry run as for a real one. A dry run promises that nothing is deployed, and staging deploys
    /// nothing: it refused instead, so the look before the act — the first skills command the install page prints —
    /// exited 1 on every new installation, with nothing to show. The clone it makes is a write into Jason's own
    /// staging directory, and the flag's help says so.
    /// </remarks>
    /// <param name="reference">The ref to take, or null for the pin this build carries.</param>
    /// <param name="overriddenByRecord">
    /// Whether the ref was chosen over this build's own pin, where a record already says so: an update hands back
    /// its record's flag beside its record's ref. Null for an install, where the answer is whether a ref was named.
    /// </param>
    public static async Task<StagedSource> StageAsync(
        CliEnvironment env,
        string? source,
        string? reference,
        bool? overriddenByRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        // Never decided from the ref alone. An update passes its record's ref back, and nobody typed that one: deciding
        // from it wrote every record the first update touched as made with --ref, and a deployment at this build's own
        // pin could no longer be told from a deliberate choice.
        var overridden = overriddenByRecord ?? !string.IsNullOrWhiteSpace(reference);
        var where = string.IsNullOrWhiteSpace(source) ? DefaultSource : source;
        var pin = string.IsNullOrWhiteSpace(reference) ? DefaultRef(BuildVersion(env)) : reference;

        if (Directory.Exists(where))
        {
            // Recorded as the full path, never as typed. `--source .` was recorded as ".", so `jason skills
            // update` -- which re-runs against the record's source -- worked only from the directory the install
            // had been run in, and anywhere else answered that "." held no skills.
            var local = Path.GetFullPath(where);
            RequirePacks(local, where);
            return new StagedSource(local, local, overridden ? pin : "(a directory, read where it stands)", overridden, null);
        }

        if (env.Programs is not { } runner)
        {
            throw new SkillsSourceUnavailable(
                $"'{where}' is not a directory on this machine and this environment cannot run git, so it cannot be fetched. Name a directory with --source.");
        }

        string staged;
        try
        {
            staged = env.Paths.SkillsStagedSourceDirectory(DirectoryNameFor(pin));
        }
        catch (ArgumentException refused)
        {
            throw new SkillsSourceUnavailable($"'{pin}' cannot be used as a ref: {refused.Message} Name a tag or a branch.");
        }

        // A tree already fetched at this ref is used as it is. It was cloned at a pinned ref, which is one
        // commit and cannot have moved, so re-fetching buys nothing -- and a dry run after the first one spends
        // no network fetch and destroys no cache.
        if (Directory.Exists(staged) && Directory.Exists(Path.Combine(staged, "skills")))
        {
            RequirePacks(staged, where);
            var known = await runner.RunAsync("git", ["rev-parse", "HEAD"], staged, FetchTimeout, cancellationToken).ConfigureAwait(false);
            return new StagedSource(staged, where, pin, overridden, known.ExitCode == 0 ? known.StandardOutput.Trim() : null);
        }

        if (Directory.Exists(staged))
        {
            Directory.Delete(staged, recursive: true);
        }

        Directory.CreateDirectory(env.Paths.SkillsStagedSourcesDirectory);

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
    /// A ref as a directory name: every character a directory name may not carry replaced, and then the whole
    /// thing refused if what is left is not an ordinary name.
    /// </summary>
    /// <remarks>
    /// Replacing on its own is what made this dangerous. A filter that keeps dots leaves <c>..</c> exactly as
    /// it was, and the path composed from it is the directory holding every deployed skill — which the caller
    /// deletes before unpacking. So the refusal is the guard and the replacement is only a convenience for the
    /// refs that are fine; <see cref="JasonPaths.SkillsStagedSourceDirectory"/> refuses again on its own
    /// account, because that is where the path is made.
    /// </remarks>
    private static string DirectoryNameFor(string reference)
    {
        var name = new string([.. reference.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-')]);

        if (name.Length == 0 || name.All(character => character == '.'))
        {
            throw new SkillsSourceUnavailable(
                $"'{reference}' cannot be used as a ref: it leaves no ordinary directory name to unpack into. Name a tag or a branch.");
        }

        return name;
    }
}
