using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Contracts.Api;
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
        var update = new Command("update", "Re-run the deployment against the source and ref its record names.");

        var root = new Option<string?>("--root") { Description = "Update this directory as a harness root, instead of detection." };
        var host = new Option<string?>("--host") { Description = "Update only this detected harness." };
        var pack = new Option<string?>("--pack") { Description = "Update only this pack." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print exactly what would be written where, and change nothing. A source not already on this machine is refused rather than fetched." };
        var force = new Option<bool>("--force") { Description = "Overwrite files you have edited. Without it they are reported and kept." };

        foreach (var option in new Option[] { root, host, pack, dryRun, force })
        {
            update.Options.Add(option);
        }

        update.SetAction((parse, cancellationToken) => UpdateAsync(
            env,
            new InstallOptions(null, null, parse.GetValue(root), parse.GetValue(host), parse.GetValue(pack), parse.GetValue(dryRun), parse.GetValue(force), RolesOnly: false),
            cancellationToken));

        return update;
    }

    private static async Task<int> UpdateAsync(CliEnvironment env, InstallOptions options, CancellationToken cancellationToken)
    {
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

        // The role root is Jason's own and is never detected, but it is a root a deployment wrote into and so
        // is one an update has to carry.
        var roots = harnesses.Select(harness => harness.Directory).Append(env.Paths.RoleSkillsDirectory);

        // And a harness is carried only where a record says a deployment was made there. An update is the same
        // act repeated, and it wrote the interactive and business packs into every harness it detected -- so an
        // installation made with --roles-only, by somebody who will not have Jason write into their agent's
        // configuration, would have been written into it by the first update.
        var recorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        SkillsDeployment? newest = null;
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
                return ExitCodes.ApiError;
            }

            if (record is { Packs.Count: > 0 })
            {
                recorded.Add(root);
            }

            foreach (var deployment in record?.Packs ?? [])
            {
                if (newest is null || deployment.InstalledAt > newest.InstalledAt)
                {
                    newest = deployment;
                }
            }
        }

        if (newest is null)
        {
            env.Error.WriteLine("Nothing here was installed by Jason, so there is nothing to update. Run 'jason skills install' first.");
            return ExitCodes.ApiError;
        }

        env.Out.WriteLine($"Updating from {newest.Source} at {newest.Ref}, as the record names it.");
        // The record's ref, always. Passing null where the record did not override re-derived this build's
        // own pin, so on a runtime upgraded since the install the line above said "at v0.1.0, as the record
        // names it" and the plan underneath it described v0.2.0. Every update test used a directory source,
        // where the ref never reaches staging at all.
        return await DeployAsync(
                env,
                options with { Source = newest.Source, Ref = newest.Ref },
                [.. harnesses.Where(harness => recorded.Contains(harness.Directory))],
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static Command Install(CliEnvironment env)
    {
        var install = new Command("install", "Deploy the skill packs into this runtime and your agent harnesses.");

        var source = new Option<string?>("--source") { Description = "Where the packs come from: a directory on this machine, or a git source. Defaults to the repository this build was published from." };
        var reference = new Option<string?>("--ref") { Description = "Which ref to take. Defaults to the pin this build carries." };
        var root = new Option<string?>("--root") { Description = "Deploy into this directory as a harness root, instead of detection." };
        var host = new Option<string?>("--host") { Description = "Deploy only into this detected harness." };
        var pack = new Option<string?>("--pack") { Description = "Deploy only this pack." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print exactly what would be written where, and change nothing. A source not already on this machine is refused rather than fetched." };
        var force = new Option<bool>("--force") { Description = "Overwrite files you have edited. Without it they are reported and kept." };
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
    private sealed record InstallOptions(string? Source, string? Ref, string? Root, string? Host, string? Pack, bool DryRun, bool Force, bool RolesOnly);

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

            return await DeployAsync(env, options with { Pack = SkillPacks.Runtime }, [], cancellationToken).ConfigureAwait(false);
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

        return await DeployAsync(env, options, harnesses, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The deployment itself: into the runtime's own directory, and into exactly the harnesses it is handed —
    /// none under <c>--roles-only</c>, the recorded ones for an update, the chosen or detected ones otherwise.
    /// </summary>
    private static async Task<int> DeployAsync(CliEnvironment env, InstallOptions options, IReadOnlyList<HarnessRoot> harnesses, CancellationToken cancellationToken)
    {
        StagedSource staged;
        try
        {
            staged = await SkillsSource.StageAsync(env, options.Source, options.Ref, options.DryRun, cancellationToken).ConfigureAwait(false);
        }
        catch (SkillsSourceUnavailable unavailable)
        {
            env.Error.WriteLine(unavailable.Message);
            return ExitCodes.ApiError;
        }

        if (options.Pack is { } only && !SkillPacks.All.Contains(only, StringComparer.OrdinalIgnoreCase))
        {
            // A misspelled pack installed nothing and exited 0, while a misspelled --host refused. Silence
            // and success is the worst answer to a typo: the operator believes the deployment happened.
            env.Error.WriteLine($"'{only}' is not a pack this build ships. They are: {string.Join(", ", SkillPacks.All)}.");
            return ExitCodes.ApiError;
        }

        var (cap, fromRuntime) = await CapAsync(env, cancellationToken).ConfigureAwait(false);
        var plan = SkillsPlanner.Compose(staged, env.Paths.RoleSkillsDirectory, harnesses, cap, fromRuntime, options.Pack);

        // Printed before a byte is written, in every mode. Writing into somebody's home directory is not a
        // silent act, and a plan nobody saw is one nobody could have stopped.
        SkillsPlanner.Describe(env.Out, plan, options.DryRun);

        if (plan.Refusals.Count > 0)
        {
            foreach (var refusal in plan.Refusals)
            {
                env.Error.WriteLine(refusal);
            }

            return ExitCodes.ApiError;
        }

        if (options.DryRun)
        {
            return ExitCodes.Success;
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
            env.Error.WriteLine(problem);
        }

        env.Out.WriteLine(report.Written == 0 && report.Problems.Count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"Already current: nothing to write ({report.Unchanged} files already in place).")
            : string.Create(CultureInfo.InvariantCulture, $"Written: {report.Written} files, {report.Unchanged} already in place."));

        return report.Succeeded ? ExitCodes.Success : ExitCodes.ApiError;
    }

    /// <summary>Where the interactive packs go. <c>--root</c> replaces detection rather than narrowing it.</summary>
    private static IReadOnlyList<HarnessRoot> Roots(CliEnvironment env, InstallOptions options)
    {
        if (options.Root is { } named)
        {
            return HarnessLocators.At(named).Detect();
        }

        if (env.Harnesses is not { } locator)
        {
            throw new SkillsRefused("harness_detection_unavailable: this environment does not detect agent harnesses. Name a directory with --root.");
        }

        var detected = locator.Detect();
        if (options.Host is { } only)
        {
            if (!Enum.TryParse<AgentHostKind>(only.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var kind))
            {
                throw new SkillsRefused($"'{only}' is not a harness this build knows.");
            }

            detected = [.. detected.Where(root => root.Host == kind)];
        }

        return detected;
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
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (SkillsPlanner.DocumentedCap, false);
        }
    }
}

/// <summary>A deployment this build will not perform, in words the operator can act on.</summary>
public sealed class SkillsRefused(string message) : Exception(message);
