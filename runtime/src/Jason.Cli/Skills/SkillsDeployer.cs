using System.Globalization;

namespace Jason.Cli.Skills;

/// <summary>What a deployment did, so the verb can report it rather than claim it.</summary>
public sealed record DeploymentReport(int Written, int Unchanged, IReadOnlyList<string> Edited, IReadOnlyList<string> Problems)
{
    public bool Succeeded => Problems.Count == 0;
}

/// <summary>Writes a plan, and writes the record last.</summary>
public static class SkillsDeployer
{
    /// <summary>
    /// Applies the plan. The record is written last, so a crash half-way through always reads as incomplete
    /// next time rather than as a deployment that is current.
    /// </summary>
    /// <param name="force">
    /// Overwrite a file the operator has edited. Without it such a file is reported and kept: an installer
    /// that quietly reverts somebody's edit is a data-loss bug wearing a convenience label.
    /// </param>
    public static DeploymentReport Apply(SkillsPlan plan, bool force, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(clock);

        var written = 0;
        var unchanged = 0;
        var edited = new List<string>();
        var problems = new List<string>();

        foreach (var root in plan.Roots)
        {
            var existing = Existing(root, problems);
            if (existing is null)
            {
                continue;
            }

            var deployments = new List<SkillsDeployment>(existing.Packs);

            foreach (var pack in plan.Packs.Where(pack => string.Equals(pack.Root, root, StringComparison.Ordinal)))
            {
                var recorded = deployments.FirstOrDefault(deployment => string.Equals(deployment.Pack, pack.Pack, StringComparison.Ordinal));
                var known = recorded?.Files.ToDictionary(file => file.Path, file => file.Sha256, StringComparer.Ordinal) ?? [];

                var mine = new List<DeployedFile>();
                foreach (var unit in pack.Units)
                {
                    foreach (var file in unit.Files)
                    {
                        var relative = Path.GetRelativePath(root, file.Target).Replace('\\', '/');
                        mine.Add(new DeployedFile(relative, file.Sha256));

                        if (File.Exists(file.Target))
                        {
                            var onDisk = SkillsRecord.Digest(file.Target);
                            if (string.Equals(onDisk, file.Sha256, StringComparison.Ordinal))
                            {
                                unchanged++;
                                continue;
                            }

                            // Differing from what the record says was written is the operator's own edit. A
                            // file that matches the record and differs from the source is simply out of date.
                            if (known.TryGetValue(relative, out var wrote)
                                && !string.Equals(onDisk, wrote, StringComparison.Ordinal)
                                && !force)
                            {
                                edited.Add(file.Target);
                                continue;
                            }
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                        File.Copy(file.Source, file.Target, overwrite: true);
                        written++;
                    }
                }

                deployments.RemoveAll(deployment => string.Equals(deployment.Pack, pack.Pack, StringComparison.Ordinal));
                deployments.Add(new SkillsDeployment(
                    pack.Pack,
                    plan.Source.Source,
                    plan.Source.Ref,
                    plan.Source.RefOverridden,
                    plan.Source.Commit,
                    clock.GetUtcNow(),
                    mine));
            }

            if (edited.Count > 0 && !force)
            {
                // Nothing is recorded for a root whose deployment is not what the record would claim it is.
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

        if (edited.Count > 0 && !force)
        {
            problems.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{edited.Count} file(s) you have edited were left as they are: {string.Join(", ", edited)}. Re-run with --force to overwrite them."));
        }

        return new DeploymentReport(written, unchanged, edited, problems);
    }

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
