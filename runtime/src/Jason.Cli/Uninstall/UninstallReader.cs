using Jason.Cli.Discovery;
using Jason.Contracts.Discovery;
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

        // The file this Jason is installed as, the same way `jason update apply` works it out -- which is
        // what this comment said while the line below read `Environment.ProcessPath`. Under `dotnet jason.dll`
        // that is the muxer, so this verb planned to remove `C:\Program Files\dotnet\dotnet.exe`, take its
        // directory off this account's PATH, and on Windows -- where the image of a running process cannot be
        // deleted but can be renamed -- move it aside, which succeeds. Running from source through `dotnet` is
        // what this repository's own README tells a developer to do.
        //
        // The remover seam does not catch this. It is a decision taken before the seam is reached, which is
        // why every test that substitutes the seam stayed green.
        var executable = env.InstallPath ?? SelfExecutable.InstalledImage;

        var directory = executable is null ? null : Path.GetDirectoryName(executable);

        return new UninstallPlan(
            autostart.Registered,
            autostart.ArtifactPath,
            new DescriptorReader(env.Paths).Read()?.Pid,
            roots,
            unknown,
            directory is { Length: > 0 } ? env.Removes?.ReadPathEntry(directory) : null,
            executable,
            directory,
            Previous(directory),
            Extracted(env, executable, directory),
            purgeData,
            env.Paths.Root);
    }

    /// <summary>
    /// The directory this build unpacked its native libraries into, when the remover can name it — and never
    /// one that holds the data directory or the installation, whatever the remover said.
    /// </summary>
    /// <remarks>
    /// The belt on a recursive delete. The remover already names only a directory the host unpacked into; a
    /// plan that could ever carry <c>~/.jason</c> in this field would be one bug away from purging it without
    /// the word, so the reader refuses the shape outright.
    /// </remarks>
    private static string? Extracted(CliEnvironment env, string? executable, string? installDirectory)
    {
        if (executable is null || env.Removes is not { } remover)
        {
            return null;
        }

        string? extracted;
        try
        {
            extracted = remover.ReadExtractedLibraries(executable);
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return extracted is null || Holds(extracted, env.Paths.Root) || (installDirectory is not null && Holds(extracted, installDirectory))
            ? null
            : extracted;
    }

    /// <summary>
    /// The one other file the installer writes into an install directory: the executable an upgrade replaced
    /// while it was running, which Windows would not let it delete.
    /// </summary>
    private static string? Previous(string? installDirectory)
    {
        if (installDirectory is not { Length: > 0 })
        {
            return null;
        }

        var previous = Path.Combine(installDirectory, PreviousExecutableName);
        return File.Exists(previous) ? previous : null;
    }

    /// <summary>What <c>install.ps1</c> calls the executable it moved aside, spelled as it spells it.</summary>
    public const string PreviousExecutableName = "jason.previous.exe";

    /// <summary>Whether <paramref name="directory"/> is <paramref name="path"/> or one of its ancestors.</summary>
    private static bool Holds(string directory, string path)
    {
        var outer = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var inner = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return inner.StartsWith(outer, StringComparison.OrdinalIgnoreCase);
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
