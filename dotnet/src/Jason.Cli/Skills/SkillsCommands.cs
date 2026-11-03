using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;

namespace Jason.Cli.Skills;

/// <summary>
/// <c>jason skills</c>: putting the skills this build ships where the things that read them look.
/// </summary>
/// <remarks>
/// A CLI verb rather than an API operation, because it fetches, verifies and copies files on this machine —
/// which is what the CLI already does for the product's own binary. Two destinations, because there are two
/// readers: the role halves go to the runtime's own data directory, where it reads them at every launch, and
/// the interactive and business packs go to the agent harnesses a person actually uses.
/// </remarks>
public static class SkillsCommands
{
    /// <summary>
    /// What <c>--dry-run</c> does, on both verbs: it deploys nothing, and it says that a source not here yet is
    /// fetched into staging first — which is a write into Jason's own data directory, and the reason a first look
    /// has anything to show.
    /// </summary>
    private const string DryRunHelp =
        "Print exactly what would be written where, and deploy nothing. A source not on this machine yet is fetched into Jason's staging directory first, so there is something to show.";

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var command = new Command("skills", "Install and update the skills this build ships.");
        command.Subcommands.Add(Install(env));
        command.Subcommands.Add(Update(env));
        return command;
    }

    /// <summary>
    /// <c>jason skills update</c>: the act re-run against what the record says it was installed from.
    /// </summary>
    /// <remarks>
    /// It takes no <c>--source</c> and no <c>--ref</c>, on purpose. An update is the same deployment repeated
    /// rather than a second decision about where things come from: a verb that accepted both would let
    /// somebody "update" a deployment into one from somewhere else, leaving a record saying it had always
    /// been that way.
    /// </remarks>
    private static Command Update(CliEnvironment env)
    {
        var update = new Command("update", "Re-run the deployment against the source its record names, at the ref this build carries unless the record chose another.");

        var root = new Option<string?>("--root") { Description = "Update this directory as a harness root, instead of detection." };
        var host = new Option<string?>("--host") { Description = "Update only this host's skills root: one of " + AgentHosts.Vocabulary + "." };
        var pack = new Option<string?>("--pack") { Description = "Update only this pack." };
        var dryRun = new Option<bool>("--dry-run") { Description = DryRunHelp };
        var force = new Option<bool>("--force") { Description = "Overwrite files you have edited. Without it they are reported and kept, and everything else is written." };

        foreach (var option in new Option[] { root, host, pack, dryRun, force })
        {
            update.Options.Add(option);
        }

        update.SetAction(async (parse, cancellationToken) => (await UpdateAsync(
            env,
            new InstallOptions(null, null, parse.GetValue(root), parse.GetValue(host), parse.GetValue(pack), parse.GetValue(dryRun), parse.GetValue(force), RolesOnly: false),
            null,
            cancellationToken).ConfigureAwait(false)).Exit);

        return update;
    }

    /// <summary>
    /// <c>jason skills update</c> as it is typed with no options: every recorded root, the detected harnesses' and the
    /// runtime's own, from the source each record names. The verb's own body, not a second one: <c>jason update apply</c>
    /// runs it as the build it has just installed, so the same deployment is repeated at that build's tag.
    /// </summary>
    /// <param name="progress">
    /// Where the verb says which pack group it is in and adds each group as its deployment ends, so that a caller whose
    /// call throws part way still holds the groups that had been deployed before it did, and the one it stopped in. Null
    /// where the caller only wants the outcome.
    /// </param>
    internal static Task<SkillsUpdateOutcome> UpdateRecordedAsync(CliEnvironment env, SkillsUpdateProgress? progress, CancellationToken cancellationToken) =>
        UpdateAsync(env, new InstallOptions(null, null, null, null, null, DryRun: false, Force: false, RolesOnly: false), progress, cancellationToken);

    private static async Task<SkillsUpdateOutcome> UpdateAsync(CliEnvironment env, InstallOptions options, SkillsUpdateProgress? progress, CancellationToken cancellationToken)
    {
        IReadOnlyList<HarnessRoot> harnesses;
        try
        {
            harnesses = Roots(env, options);
        }
        catch (SkillsRefused refused)
        {
            env.Error.WriteLine(refused.Message);
            return new SkillsUpdateOutcome(ExitCodes.ApiError, [], NothingRecorded: false);
        }

        // The role root is Jason's own and is never detected, but it is a root a deployment wrote into and so
        // is one an update has to carry.
        var roleRoot = env.Paths.RoleSkillsDirectory;
        var roots = harnesses.Select(harness => harness.Directory).Append(roleRoot).Distinct(StringComparer.OrdinalIgnoreCase);

        // Each pack in each root, from that pack's own record in that root. This took the newest deployment
        // anywhere and re-ran it everywhere: after a full install from one source and --roles-only from another,
        // the harness was "updated" from the second and its record rewritten to say it had always come from
        // there. And a root is carried only where its record says a deployment was made there, so an
        // installation taught with --roles-only is never written into an agent's configuration by an update.
        var groups = new SortedDictionary<(string Source, string Ref, bool Overridden, string Pack), List<string>>();
        foreach (var root in roots)
        {
            SkillsRecord? record;
            try
            {
                record = SkillsRecord.Read(root);
            }
            catch (SkillsRecordUnreadable unreadable)
            {
                env.Error.WriteLine(unreadable.Message);
                return new SkillsUpdateOutcome(ExitCodes.ApiError, [], NothingRecorded: false);
            }

            foreach (var pack in (record?.Packs ?? []).GroupBy(deployment => deployment.Pack, StringComparer.Ordinal))
            {
                if (options.Pack is { } only && !string.Equals(only, pack.Key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var own = pack.MaxBy(deployment => deployment.InstalledAt)!;
                var key = (own.Source, own.Ref, own.RefOverridden, own.Pack);
                if (!groups.TryGetValue(key, out var members))
                {
                    groups[key] = members = [];
                }

                members.Add(root);
            }
        }

        if (groups.Count == 0)
        {
            env.Error.WriteLine("Nothing here was installed by Jason, so there is nothing to update. Run 'jason skills install' first.");
            return new SkillsUpdateOutcome(ExitCodes.ApiError, [], NothingRecorded: true);
        }

        var exit = ExitCodes.Success;
        progress ??= new SkillsUpdateProgress();
        var carried = progress.Carried;
        foreach (var ((source, reference, overridden, pack), members) in groups)
        {
            var (pin, why) = UpdatePin(env, source, reference, overridden);
            var updating = $"{pack} in {string.Join(", ", members)} from {source}{why}";
            env.Out.WriteLine($"Updating {updating}");
            progress.Entered = (pack, members);

            // The line and staging are handed the same ref, decided once, above. Passing null where the record did
            // not override let staging re-derive this build's pin on its own, so on a runtime upgraded since the
            // install the line said "at v0.1.0, as the record names it" and the plan underneath it described v0.2.0.
            var (deployed, said, refused) = await DeployAsync(
                    env,
                    options with { Source = source, Ref = pin, RecordedOverride = overridden, Pack = pack },
                    [.. harnesses.Where(harness => members.Contains(harness.Directory, StringComparer.OrdinalIgnoreCase))],
                    cancellationToken,
                    members.Contains(roleRoot, StringComparer.OrdinalIgnoreCase))
                .ConfigureAwait(false);

            carried.Add(new SkillsUpdateGroup(pack, members, deployed, $"{updating} {said}".TrimEnd(), refused));
            progress.Entered = null;
            exit = exit == ExitCodes.Success ? deployed : exit;
        }

        return new SkillsUpdateOutcome(exit, [.. carried], NothingRecorded: false);
    }

    /// <summary>
    /// The ref an update stages for one recorded deployment, and the end of the line that says why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ref this build carries, for a git source whose record took the pin of the build that installed it. That
    /// pin was the older build's, and keeping it is how an installation upgraded with <c>jason update apply</c> went
    /// on teaching from the release before, while the one verb meant to move it reported it current.
    /// </para>
    /// <para>
    /// The record's ref is kept in three cases only, and the line names the one that applies. A ref the record says
    /// was chosen stays chosen: an update is the same deployment repeated, not a second decision about where things
    /// come from. A directory is read where it stands and has no ref to move to. And a build that is not a release
    /// carries the pin of a release that is not published yet, so moving to it would be a fetch that fails.
    /// </para>
    /// </remarks>
    private static (string Ref, string Why) UpdatePin(CliEnvironment env, string source, string reference, bool overridden)
    {
        if (Directory.Exists(source))
        {
            return (reference, ", a directory, which is read where it stands rather than at a ref.");
        }

        if (overridden)
        {
            return (reference, $" at {reference}, the ref its record says was chosen over this build's own.");
        }

        var build = SkillsSource.BuildVersion(env);
        if (!SkillsSource.IsRelease(build))
        {
            return (reference, $" at {reference}, as its record names it: this build, {build}, is not a release, so the ref it carries is not published yet.");
        }

        var own = SkillsSource.DefaultRef(build);
        return (own, $" at {own}, the ref this build carries; its record named {reference}.");
    }

    private static Command Install(CliEnvironment env)
    {
        var install = new Command("install", "Deploy the skill packs into this runtime and your agent harnesses.");

        var source = new Option<string?>("--source") { Description = "Where the packs come from: a directory on this machine, or a git source. Defaults to the repository this build was published from." };
        var reference = new Option<string?>("--ref") { Description = "Which ref to take. Defaults to the pin this build carries." };
        var root = new Option<string?>("--root") { Description = "Deploy into this directory as a harness root, instead of detection." };
        var host = new Option<string?>("--host") { Description = "Deploy only into this host's skills root: one of " + AgentHosts.Vocabulary + ". With --root, the host that directory is for." };
        var pack = new Option<string?>("--pack") { Description = "Deploy only this pack." };
        var dryRun = new Option<bool>("--dry-run") { Description = DryRunHelp };
        var force = new Option<bool>("--force") { Description = "Overwrite files you have edited. Without it they are reported and kept, and everything else is written." };
        var rolesOnly = new Option<bool>("--roles-only") { Description = "Deploy only the role skills, into this runtime's own directory, and nothing into any agent harness. The role half is the one a runtime cannot work without; the harness half is yours to choose." };

        foreach (var option in new Option[] { source, reference, root, host, pack, dryRun, force, rolesOnly })
        {
            install.Options.Add(option);
        }

        install.SetAction((parse, cancellationToken) => RunAsync(
            env,
            new InstallOptions(
                parse.GetValue(source),
                parse.GetValue(reference),
                parse.GetValue(root),
                parse.GetValue(host),
                parse.GetValue(pack),
                parse.GetValue(dryRun),
                parse.GetValue(force),
                parse.GetValue(rolesOnly)),
            cancellationToken));

        return install;
    }

    /// <param name="RolesOnly">
    /// The role skills alone, into the runtime's own directory. <c>role_skills</c> is a required check and the
    /// harness half is not, yet the one command that repaired it also wrote the interactive and business packs
    /// into the agent's own configuration: somebody who would not let Jason write there could not reach ready.
    /// <c>--host</c> takes harness names only, <c>--pack</c> cannot tell the role half from the rest of its
    /// pack, and <c>--root</c> moves only the harness half — so the way to deploy what a runtime needs, and
    /// nothing else, did not exist.
    /// </param>
    /// <param name="RecordedOverride">
    /// What the record being updated says about its ref, carried to staging beside it. Null for an install, where
    /// whether the ref was chosen is whether <c>--ref</c> was typed.
    /// </param>
    private sealed record InstallOptions(string? Source, string? Ref, string? Root, string? Host, string? Pack, bool DryRun, bool Force, bool RolesOnly, bool? RecordedOverride = null);

    private static async Task<int> RunAsync(CliEnvironment env, InstallOptions options, CancellationToken cancellationToken)
    {
        if (options.RolesOnly)
        {
            // Said before anything is read, because each of these asks for a harness the flag rules out. A flag
            // that quietly ignored --root would deploy somewhere other than where it was told to.
            if (options.Root is not null || options.Host is not null)
            {
                env.Error.WriteLine("--roles-only deploys into no agent harness, so it takes neither --root nor --host.");
                return ExitCodes.Usage;
            }

            if (options.Pack is { } named && !string.Equals(named, SkillPacks.Runtime, StringComparison.OrdinalIgnoreCase))
            {
                env.Error.WriteLine($"--roles-only deploys the role skills, which are part of {SkillPacks.Runtime}; '{named}' has none.");
                return ExitCodes.Usage;
            }

            return (await DeployAsync(env, options with { Pack = SkillPacks.Runtime }, [], cancellationToken).ConfigureAwait(false)).Exit;
        }

        IReadOnlyList<HarnessRoot> harnesses;
        try
        {
            harnesses = Roots(env, options);
        }
        catch (SkillsRefused refused)
        {
            env.Error.WriteLine(refused.Message);
            return ExitCodes.ApiError;
        }

        return (await DeployAsync(env, options, harnesses, cancellationToken).ConfigureAwait(false)).Exit;
    }

    /// <summary>
    /// The deployment itself: into the runtime's own directory, and into exactly the harnesses it is handed —
    /// none under <c>--roles-only</c>, the recorded ones for an update, the chosen or detected ones otherwise.
    /// </summary>
    /// <param name="withRoles">
    /// Whether the runtime's own role root is deployed into. Always for an install; for an update, only where that
    /// root's record names the source being deployed from.
    /// </param>
    private static async Task<(int Exit, string Said, string Refused)> DeployAsync(CliEnvironment env, InstallOptions options, IReadOnlyList<HarnessRoot> harnesses, CancellationToken cancellationToken, bool withRoles = true)
    {
        // Every line a refusal is said in goes to stderr as it always has, and is kept, so a caller reporting this one
        // deployment among several can say which of them refused, and why.
        var refused = new List<string>();
        void Refuse(string line)
        {
            env.Error.WriteLine(line);
            refused.Add(line);
        }

        string Refusal() => string.Join(' ', refused);

        StagedSource staged;
        try
        {
            staged = await SkillsSource.StageAsync(env, options.Source, options.Ref, options.RecordedOverride, cancellationToken).ConfigureAwait(false);
        }
        catch (SkillsSourceUnavailable unavailable)
        {
            Refuse(unavailable.Message);
            return (ExitCodes.ApiError, string.Empty, Refusal());
        }

        if (options.Pack is { } only && !SkillPacks.All.Contains(only, StringComparer.OrdinalIgnoreCase))
        {
            // A misspelled pack installed nothing and exited 0, while a misspelled --host refused. Silence
            // and success is the worst answer to a typo: the operator believes the deployment happened.
            Refuse($"'{only}' is not a pack this build ships. They are: {string.Join(", ", SkillPacks.All)}.");
            return (ExitCodes.ApiError, string.Empty, Refusal());
        }

        var (cap, fromRuntime) = await CapAsync(env, cancellationToken).ConfigureAwait(false);
        var plan = SkillsPlanner.Compose(staged, withRoles ? env.Paths.RoleSkillsDirectory : null, harnesses, cap, fromRuntime, options.Pack);

        // Printed before a byte is written, in every mode. Writing into somebody's home directory is not a
        // silent act, and a plan nobody saw is one nobody could have stopped.
        SkillsPlanner.Describe(env.Out, plan, options.DryRun);

        if (plan.Refusals.Count > 0)
        {
            foreach (var refusal in plan.Refusals)
            {
                Refuse(refusal);
            }

            return (ExitCodes.ApiError, string.Empty, Refusal());
        }

        if (options.DryRun)
        {
            return (ExitCodes.Success, string.Empty, string.Empty);
        }

        var report = SkillsDeployer.Apply(plan, env.Paths, options.Force, TimeProvider.System);
        foreach (var note in report.Notes)
        {
            env.Out.WriteLine(note);
        }

        // What --force actually did, named. The report has carried this list all along and the command path
        // never read it, so the one run that overwrites somebody's work was the one that said least about it.
        if (options.Force && report.Edited.Count > 0)
        {
            env.Out.WriteLine($"Overwrote {report.Edited.Count} file(s) you had edited: {string.Join(", ", report.Edited)}.");
        }

        foreach (var problem in report.Problems)
        {
            Refuse(problem);
        }

        // A pack left with no record is one no update will reach, and nothing else says so: `jason status` reads records.
        // The install that completes it is this one again, narrowed the way this one was.
        foreach (var (pack, roots) in report.Unrecorded.GroupBy(left => left.Pack, left => left.Root, StringComparer.Ordinal).Select(left => (left.Key, left.ToList())))
        {
            var repair = options.RolesOnly ? "jason skills install --roles-only"
                : options.Pack is { } named ? $"jason skills install --pack {named}"
                : "jason skills install";
            Refuse($"{pack} in {string.Join(", ", roots)} is left unrecorded, because a skill of it was refused. "
                + $"Run '{repair}' to complete it and record it; 'jason skills update' will not, since it moves recorded packs only.");
        }

        // And what it kept, in the same line: the files the person edited are theirs, the rest was written around them, and
        // the run succeeded. The line is also what `jason update apply` says for the pack group.
        var keptSaid = !options.Force && report.Edited.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $" Kept {report.Edited.Count} file(s) you have edited: {string.Join(", ", report.Edited)}. --force overwrites them.")
            : string.Empty;
        var said = (report.Written == 0 && report.Problems.Count == 0 && keptSaid.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"Already current: nothing to write ({report.Unchanged} files already in place).")
            : string.Create(CultureInfo.InvariantCulture, $"Written: {report.Written} files, {report.Unchanged} already in place.")) + keptSaid;
        env.Out.WriteLine(said);

        return (report.Succeeded ? ExitCodes.Success : ExitCodes.ApiError, said, Refusal());
    }

    /// <summary>
    /// Where the interactive packs go. <c>--root</c> replaces detection rather than narrowing it, and <c>--host</c> with
    /// it labels the named directory; <c>--host</c> alone narrows detection to the one root that host reads, and a host
    /// that was not detected is refused rather than deployed into nothing.
    /// </summary>
    private static IReadOnlyList<HarnessRoot> Roots(CliEnvironment env, InstallOptions options)
    {
        AgentHostKind? host = null;
        if (options.Host is { } word)
        {
            host = AgentHosts.Parse(word) ?? throw new SkillsRefused($"'{word}' is not a host this build knows. They are: {AgentHosts.Vocabulary}.");
        }

        if (options.Root is { } named)
        {
            return HarnessLocators.At(named, host).Detect();
        }

        if (env.Harnesses is not { } locator)
        {
            throw new SkillsRefused("harness_detection_unavailable: this environment does not detect agent harnesses. Name a directory with --root.");
        }

        var detected = locator.Detect();
        if (host is not { } kind)
        {
            return detected;
        }

        IReadOnlyList<HarnessRoot> chosen = [.. detected.Where(root => root.Hosts.Contains(kind))];
        return chosen.Count > 0
            ? chosen
            : throw new SkillsRefused($"'{AgentHosts.Word(kind)}' was not detected on this machine: there is no {(AgentHosts.Of(kind).MarkerKind == HomeMarkerKind.File ? "file" : "directory")} at ~/{AgentHosts.Of(kind).HomeMarker}. Install it, or name a directory with --root.");
    }

    /// <summary>
    /// The cap to validate against, from the runtime if it answers. It is a live setting, so a guessed one
    /// validates against the wrong number — and the output says which was used, because a pass against the
    /// wrong cap is not a pass.
    /// </summary>
    private static async Task<(int Cap, bool FromRuntime)> CapAsync(CliEnvironment env, CancellationToken cancellationToken)
    {
        var descriptor = new DescriptorReader(env.Paths).Read();
        if (descriptor is null)
        {
            return (SkillsPlanner.DocumentedCap, false);
        }

        try
        {
            using var client = new RuntimeClient(descriptor, env.HttpHandler);
            var response = await client.PostAsync(Operations.SystemInfo, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return (SkillsPlanner.DocumentedCap, false);
            }

            var info = JsonSerializer.Deserialize<SystemInfoResponse>(response.Body, JasonJson.Options);
            return info?.Skills is { } skills ? (skills.MaxSkillBytes, true) : (SkillsPlanner.DocumentedCap, false);
        }
        // And a descriptor no client can be made from — an address that is not one, none, or one in a scheme HTTP does not
        // speak — is no runtime to ask either.
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or FormatException or ArgumentException or NotSupportedException)
        {
            return (SkillsPlanner.DocumentedCap, false);
        }
    }
}

/// <summary>A deployment this build will not perform, in words the operator can act on.</summary>
public sealed class SkillsRefused(string message) : Exception(message);

/// <summary>What one run of <c>jason skills update</c> did, for a caller that reports it rather than printing it.</summary>
/// <param name="Exit">The verb's own exit code.</param>
/// <param name="Carried">Each pack group it carried, in the order it carried them, with what became of each.</param>
/// <param name="NothingRecorded">
/// Whether no root here records a deployment at all — the verb's "nothing here was installed", which is a refusal
/// for somebody who typed it and nothing to do for an update.
/// </param>
internal sealed record SkillsUpdateOutcome(int Exit, IReadOnlyList<SkillsUpdateGroup> Carried, bool NothingRecorded);

/// <summary>
/// How far one run of <c>jason skills update</c> has got, kept by the verb as it goes, for a caller whose call may throw
/// part way: what it carried before it stopped, and the group it stopped in.
/// </summary>
internal sealed class SkillsUpdateProgress
{
    /// <summary>Each pack group whose deployment ended, in the order they ended.</summary>
    public List<SkillsUpdateGroup> Carried { get; } = [];

    /// <summary>The pack group being deployed, from the moment the verb enters it until its deployment ends; null between groups.</summary>
    public (string Pack, IReadOnlyList<string> Roots)? Entered { get; set; }
}

/// <summary>One pack, from one source at one ref, in the roots whose records name it so: one deployment of the update.</summary>
/// <param name="Exit">That deployment's own exit code: one group refused is not every group refused.</param>
/// <param name="Line">
/// The verb's own words for it: the pack, the roots, the source and ref and why, and what was written.
/// </param>
/// <param name="Refused">What it refused with, as stderr said it, or empty where it did not.</param>
internal sealed record SkillsUpdateGroup(string Pack, IReadOnlyList<string> Roots, int Exit, string Line, string Refused);
