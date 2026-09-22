using System.Globalization;
using Jason.Contracts.Discovery;
using Jason.Contracts.Skills;

namespace Jason.Cli.Skills;

/// <summary>What a deployment did, so the verb can report it rather than claim it.</summary>
/// <param name="Notes">
/// Things worth telling the operator that are not failures — a root whose record never landed, which this run
/// is completing. A root that silently repaired itself would hide that something went wrong once.
/// </param>
public sealed record DeploymentReport(
    int Written,
    int Unchanged,
    IReadOnlyList<string> Edited,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Notes)
{
    public bool Succeeded => Problems.Count == 0;
}

/// <summary>Writes a plan, one whole skill at a time, and writes the record last.</summary>
public static class SkillsDeployer
{
    /// <summary>
    /// Applies the plan. Each skill is assembled beside its destination and moved into place, so a launch
    /// reading a role's tree sees the tree that was there or the tree that arrived and never a blend of the
    /// two. The record goes in last, so a crash half-way through reads as incomplete next time rather than as
    /// a deployment that is current.
    /// </summary>
    /// <param name="force">
    /// Overwrite files the operator has edited. Without it they are reported and kept, and nothing is written
    /// at all: an installer that quietly reverts somebody's edit is a data-loss bug wearing a convenience
    /// label, and one that half-reverts is worse.
    /// </param>
    public static DeploymentReport Apply(SkillsPlan plan, JasonPaths paths, bool force, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(clock);

        var problems = new List<string>();
        var notes = new List<string>();
        var records = new Dictionary<string, SkillsRecord>(StringComparer.Ordinal);
        foreach (var root in plan.Roots)
        {
            if (Existing(root, problems) is not { } record)
            {
                continue;
            }

            records[root] = record;

            // A root holding skills that no record accounts for is a deployment whose record never landed --
            // it is written last, so a crash half-way through reads exactly like this. Such a root is
            // completed rather than trusted, and said out loud: one that silently repaired itself would hide
            // that something went wrong once.
            if (record.Packs.Count == 0 && Directory.Exists(root) && Directory.GetDirectories(root).Length > 0)
            {
                notes.Add($"'{root}' holds skills and there is no record of what is there, so this deployment is being completed rather than trusted.");
            }
        }

        if (problems.Count > 0)
        {
            return new DeploymentReport(0, 0, [], problems, notes);
        }

        // Everything is judged before anything is written, for the same reason the plan itself is: a partly
        // applied deployment is a state nobody asked for and nothing reports.
        var edited = Edited(plan, records);
        if (edited.Count > 0 && !force)
        {
            return new DeploymentReport(
                0,
                0,
                edited,
                [string.Create(
                    CultureInfo.InvariantCulture,
                    $"{edited.Count} file(s) you have edited would be overwritten and nothing was written: {string.Join(", ", edited)}. Re-run with --force to overwrite them.")],
                notes);
        }

        var written = 0;
        var unchanged = 0;

        foreach (var root in plan.Roots)
        {
            var staging = SkillsSwap.StagingFor(paths, root);
            SkillsSwap.Collect(staging);

            var deployments = new List<SkillsDeployment>(records[root].Packs);
            var rewrite = false;

            foreach (var pack in plan.Packs.Where(pack => string.Equals(pack.Root, root, StringComparison.Ordinal)))
            {
                var mine = new List<DeployedFile>();
                foreach (var unit in pack.Units)
                {
                    mine.AddRange(unit.Files.Select(file => new DeployedFile(Relative(root, file.Target), file.Sha256)));

                    if (Current(unit))
                    {
                        unchanged += unit.Files.Count;
                        continue;
                    }

                    if (Swap(unit, staging, clock) is { } refusal)
                    {
                        problems.Add(refusal);
                        continue;
                    }

                    written += unit.Files.Count;
                }

                var recorded = deployments.FirstOrDefault(deployment => string.Equals(deployment.Pack, pack.Pack, StringComparison.Ordinal));
                var next = new SkillsDeployment(
                    pack.Pack,
                    plan.Source.Source,
                    plan.Source.Ref,
                    plan.Source.RefOverridden,
                    plan.Source.Commit,
                    clock.GetUtcNow(),
                    mine);

                // A run that changed nothing rewrites nothing, not even the time it ran. "It wrote nothing"
                // has to mean the bytes under this root are the bytes that were there, and a record whose
                // timestamp moved is a byte that changed.
                if (recorded is not null && Same(recorded, next))
                {
                    continue;
                }

                deployments.RemoveAll(deployment => string.Equals(deployment.Pack, pack.Pack, StringComparison.Ordinal));
                deployments.Add(next);
                rewrite = true;
            }

            if (!rewrite)
            {
                continue;
            }

            try
            {
                SkillsRecord.Write(root, new SkillsRecord(SkillsRecord.CurrentVersion, deployments));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"The record in '{root}' could not be written: {exception.Message}");
            }
        }

        return new DeploymentReport(written, unchanged, edited, problems, notes);
    }

    /// <summary>
    /// Whether a recorded deployment says the same thing as the one about to replace it, the time it ran
    /// aside. Every field but that one is about what is on disk; that one is about when somebody typed.
    /// </summary>
    private static bool Same(SkillsDeployment recorded, SkillsDeployment next) =>
        (recorded with { InstalledAt = default, Files = [] }) == (next with { InstalledAt = default, Files = [] })
        && recorded.Files.SequenceEqual(next.Files);

    /// <summary>
    /// Whether what is on disk is already exactly this skill: the same files, each with the same digest, and
    /// nothing besides. A deployment that would change nothing renames nothing, which is what makes installing
    /// twice cost nothing and closes the window entirely in the commonest case of all.
    /// </summary>
    private static bool Current(PlannedUnit unit)
    {
        if (!Directory.Exists(unit.Unit))
        {
            return false;
        }

        var onDisk = Directory.GetFiles(unit.Unit, "*", SearchOption.AllDirectories);
        if (onDisk.Length != unit.Files.Count)
        {
            return false;
        }

        foreach (var file in unit.Files)
        {
            if (!File.Exists(file.Target) || !string.Equals(SkillsRecord.Digest(file.Target), file.Sha256, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The whole skill, assembled beside its destination and moved into place.</summary>
    private static string? Swap(PlannedUnit unit, string staging, TimeProvider clock)
    {
        var staged = Path.Combine(staging, $"{unit.Name}.staging-{Guid.NewGuid():N}");
        try
        {
            foreach (var file in unit.Files)
            {
                var target = Path.Combine(staged, Path.GetRelativePath(unit.Unit, file.Target));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file.Source, target, overwrite: true);
            }

            return SkillsSwap.Replace(staged, unit.Unit, staging, clock);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"'{unit.Name}' could not be written: {exception.Message}";
        }
        finally
        {
            if (Directory.Exists(staged))
            {
                try
                {
                    Directory.Delete(staged, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Files on disk that differ from what the record says was written there. Differing from the record is the
    /// operator's own edit; differing from the source while matching the record is simply out of date.
    /// </summary>
    private static IReadOnlyList<string> Edited(SkillsPlan plan, Dictionary<string, SkillsRecord> records)
    {
        var edited = new List<string>();
        foreach (var pack in plan.Packs)
        {
            var known = records[pack.Root].Packs
                .FirstOrDefault(deployment => string.Equals(deployment.Pack, pack.Pack, StringComparison.Ordinal))
                ?.Files.ToDictionary(file => file.Path, file => file.Sha256, StringComparer.Ordinal) ?? [];

            foreach (var file in pack.Files)
            {
                if (!File.Exists(file.Target)
                    || !known.TryGetValue(Relative(pack.Root, file.Target), out var wrote))
                {
                    continue;
                }

                var onDisk = SkillsRecord.Digest(file.Target);
                if (!string.Equals(onDisk, wrote, StringComparison.Ordinal)
                    && !string.Equals(onDisk, file.Sha256, StringComparison.Ordinal))
                {
                    edited.Add(file.Target);
                }
            }
        }

        return edited;
    }

    private static string Relative(string root, string target) =>
        Path.GetRelativePath(root, target).Replace('\\', '/');

    private static SkillsRecord? Existing(string root, List<string> problems)
    {
        try
        {
            return SkillsRecord.Read(root) ?? new SkillsRecord(SkillsRecord.CurrentVersion, []);
        }
        catch (SkillsRecordUnreadable unreadable)
        {
            // Never guessed at: an uninstall reading a record this build wrote over would remove the paths it
            // understands and leave the rest, while reporting that it removed everything.
            problems.Add(unreadable.Message);
            return null;
        }
    }
}
