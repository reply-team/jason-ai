using Jason.Cli.Discovery;
using Jason.Contracts.Skills;

namespace Jason.Cli.Uninstall;

/// <summary>
/// Reads what an uninstall would do. It changes nothing, which is the whole of its job: the plan is printed
/// before a byte is removed, in every mode rather than only under <c>--dry-run</c>.
/// </summary>
public static class UninstallReader
{
    public static UninstallPlan Read(CliEnvironment env, bool purgeData)
    {
        ArgumentNullException.ThrowIfNull(env);

        var autostart = ReadAutostart(env);
        var roots = new List<RootRemoval>();
        var unknown = new List<UnknownRoot>();

        foreach (var root in Roots(env))
        {
            SkillsRecord? record;
            try
            {
                record = SkillsRecord.Read(root);
            }
            catch (SkillsRecordUnreadable unreadable)
            {
                unknown.Add(new UnknownRoot(root, unreadable.Message));
                continue;
            }

            if (record is null || record.Packs.Count == 0)
            {
                // Jason put nothing here, so there is nothing of Jason's to take away. A directory full of
                // somebody else's skills is exactly this case, and it stays whole.
                continue;
            }

            roots.Add(new RootRemoval(
                root,
                [.. record.Packs.Select(pack => pack.Pack).Distinct(StringComparer.Ordinal)],
                [.. record.Packs.SelectMany(pack => pack.Files).Select(file => Measure(root, file))]));
        }

        // The file this Jason is installed as, the same way `jason update apply` works it out. Reading it is
        // safe anywhere: what makes this verb fail closed is that removing anything goes through the remover
        // seam, which a test supplies and which refuses when nothing named one.
        var executable = env.InstallPath ?? Environment.ProcessPath;

        return new UninstallPlan(
            autostart.Registered,
            autostart.ArtifactPath,
            new DescriptorReader(env.Paths).Read()?.Pid,
            roots,
            unknown,
            executable,
            executable is null ? null : Path.GetDirectoryName(executable),
            purgeData,
            env.Paths.Root);
    }

    /// <summary>
    /// Every root a deployment writes into: the harnesses a person chose, and the runtime's own.
    /// </summary>
    /// <remarks>
    /// The role skills root lives inside the data directory and is read anyway, because it holds a deployment
    /// Jason made and a record saying so — it is not the operator's own work, the way the database and the
    /// settings beside it are. So it goes by receipt even when the data directory is kept.
    /// </remarks>
    private static IEnumerable<string> Roots(CliEnvironment env) =>
        (env.Harnesses?.Detect().Select(root => root.Directory) ?? [])
            .Append(env.Paths.RoleSkillsDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static FileRemoval Measure(string root, DeployedFile file)
    {
        var path = Path.GetFullPath(Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar)));

        if (!File.Exists(path))
        {
            return new FileRemoval(path, Present: false, Edited: false);
        }

        try
        {
            return new FileRemoval(path, Present: true, Edited: !string.Equals(SkillsRecord.Digest(path), file.Sha256, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cannot be read, so cannot be shown to be ours. Kept, and reported — the same answer an edit
            // gets, and the safe one: this verb removes what it can prove Jason wrote.
            return new FileRemoval(path, Present: true, Edited: true);
        }
    }

    private static Autostart.AutostartState ReadAutostart(CliEnvironment env)
    {
        if (env.Autostart is not { } registrar || registrar.Platform is Autostart.AutostartPlatform.Unsupported)
        {
            return new Autostart.AutostartState(false, [], null);
        }

        try
        {
            return registrar.Read();
        }
        catch (Autostart.AutostartException)
        {
            // Reading a plan may not fail because a registration could not be asked after. The removal step
            // meets the same refusal and is where it is reported, with the machine's own words.
            return new Autostart.AutostartState(false, [], null);
        }
    }
}
