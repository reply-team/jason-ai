using System.Globalization;
using Jason.Cli.Discovery;
using Jason.Cli.Installation;
using Jason.Cli.Skills;
using Jason.Contracts.Discovery;
using Jason.Contracts.Skills;
using Jason.Contracts.Update;

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
        // what this repository's DEVELOPMENT.md tells a developer to do.
        //
        // And that fix covered the muxer only, while this comment said it covered running from source. The
        // DEVELOPMENT.md's way is `dotnet run`, which starts the build's own launcher rather than the muxer, and the
        // launcher was planned as the executable and its build directory as the install directory. Only a
        // single-file build is installed as a file, and SelfExecutable now says so.
        //
        // The remover seam does not catch this. It is a decision taken before the seam is reached, which is
        // why every test that substitutes the seam stayed green.
        //
        // And a file a package manager installed is the manager's, with its directory and the PATH entry that reaches
        // it: none of the three is planned for removal, and the plan names them as left to the manager instead.
        var owner = PackageOwner.Of(env);
        var executable = owner is null ? env.InstallPath ?? SelfExecutable.InstalledImage : null;

        var directory = executable is null ? null : Path.GetDirectoryName(executable);
        var descriptor = new DescriptorReader(env.Paths).Read();
        var (notificationKeys, registryUnreadable) = NotificationKeys(env);
        var (link, linkUnreadable) = Link(env);
        var extracted = Extracted(env, executable, directory);
        var (others, othersLeft) = Others(env, extracted, directory, OperatingSystem.IsWindows(), ExtractBase(env));

        return new UninstallPlan(
            autostart.Registered,
            autostart.ArtifactPath,
            descriptor?.Pid,
            descriptor?.StartedAt,
            roots,
            unknown,
            directory is { Length: > 0 } ? env.Removes?.ReadPathEntry(directory) : null,
            executable,
            directory,
            Previous(directory),
            extracted,
            purgeData,
            env.Paths.Root,
            notificationKeys,
            registryUnreadable,
            Notices(executable),
            owner,
            others,
            othersLeft,
            link,
            linkUnreadable);
    }

    /// <summary>
    /// The command this installation's toast host writes for a <c>jason:</c> link: the installed file where one is named,
    /// otherwise what starts this very process, which is what the host is started as.
    /// </summary>
    internal static string? LinkCommand(CliEnvironment env)
    {
        try
        {
            return Jason.Contracts.Notifications.LinkRegistration.Command(env.InstallPath is { } path ? [path] : SelfExecutable.Command);
        }
        catch (ArgumentException)
        {
            // A path no command can hold, a percent sign in it say: this installation's toast host writes no link.
            return null;
        }
    }

    /// <summary>
    /// The <c>jason:</c> link registration and what becomes of it; where the classes cannot be read, none and why,
    /// never taken for an account without one.
    /// </summary>
    private static (LinkRegistrationPlan? Link, string? Unreadable) Link(CliEnvironment env)
    {
        try
        {
            return (LinkRegistrationPlan.Of(env.Removes?.ReadLinkRegistration(), LinkCommand(env)), null);
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (null, exception.Message);
        }
    }

    /// <summary>
    /// The notice files the installer keeps beside the executable, by exactly their names there, where they are present.
    /// Nothing else is taken for them: a <c>LICENSE</c> in a directory other tools share is another tool's.
    /// </summary>
    private static IReadOnlyList<string> Notices(string? executable) =>
        executable is null
            ? []
            : [.. ReleaseAssets.Notices.Select(notice => ReleaseAssets.NoticeBeside(executable, notice)).Where(File.Exists)];

    /// <summary>
    /// The keys of the toast's registration present in this account; where the registry cannot be read, none and why —
    /// never taken for an account with nothing to remove.
    /// </summary>
    private static (IReadOnlyList<string> Keys, string? Unreadable) NotificationKeys(CliEnvironment env)
    {
        try
        {
            return (env.Removes?.ReadNotificationRegistration() ?? [], null);
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ([], exception.Message);
        }
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

    /// <summary>
    /// What every other build of this program unpacked beside this build's own directory: each directory beside it that
    /// is shaped as the host unpacks — a directory, not a link, holding files and no directory — and holds neither the
    /// data directory nor the installation. Planned for removal on Windows only, and only where the host unpacks into its
    /// own default base; otherwise none is, and the line that says why is returned instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host unpacks into <c>&lt;base&gt;/&lt;program&gt;/&lt;bundle id&gt;</c>, where the base is this account's and the
    /// program is the executable's name, and the bundle id is the build's own. So every build of Jason this account ever
    /// ran unpacked beside this one: the versions this installation was before an update, and any other installation of
    /// this account — a package manager's beside a script's, a build from source. Nothing in such a directory says which
    /// executable made it, so this cannot tell an earlier version of this installation from another installation, and it
    /// does not try: what it tells apart is a build's unpacked libraries from anything else, by shape.
    /// </para>
    /// <para>
    /// Removing another installation's is safe only where a copy running from it keeps it. On Windows a copy running now
    /// holds what it loaded, the removal is refused, and the runner keeps the directory and says so. Elsewhere nothing holds
    /// it: a copy running from it would lose a library it has not loaded yet, and one starting would lose the directory it
    /// is unpacking into. So there none is planned, and the plan counts them and says they are removed by hand.
    /// </para>
    /// <para>
    /// And the base is the host's default only while <c>DOTNET_BUNDLE_EXTRACT_BASE_DIR</c> is unset. Set, it can name any
    /// directory — the home directory, whose <c>jason</c> is the harness root on a file system that ignores case — and
    /// what else is under the program's directory there is not known to be any build's, so nothing beside this build's
    /// own is looked for.
    /// </para>
    /// </remarks>
    internal static (IReadOnlyList<string> Others, string? Left) Others(CliEnvironment env, string? extracted, string? installDirectory, bool windows, string? extractBase)
    {
        if (extracted is null || Path.GetDirectoryName(extracted) is not { Length: > 0 } parent)
        {
            return ([], null);
        }

        if (extractBase is { Length: > 0 })
        {
            return ([], $"{ExtractBaseVariable} is set, so the native libraries other builds of this program unpacked under '{parent}' are not looked for: it names a directory of its own, and what else is in it is not known to be theirs.");
        }

        IReadOnlyList<string> others;
        try
        {
            others = [.. Directory.EnumerateDirectories(parent)
                .Where(other => !string.Equals(Path.GetFullPath(other), Path.GetFullPath(extracted), StringComparison.OrdinalIgnoreCase))
                .Where(other => !new DirectoryInfo(other).Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Where(other => !Directory.EnumerateDirectories(other).Any())
                .Where(other => !Holds(other, env.Paths.Root) && (installDirectory is null || !Holds(other, installDirectory)))
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ([], null);
        }

        return windows || others.Count == 0
            ? (others, null)
            : ([], string.Create(
                CultureInfo.InvariantCulture,
                $"What other builds of this program unpacked under '{parent}' is left, {others.Count} director{(others.Count == 1 ? "y" : "ies")} of native libraries: a copy may be running from them, and elsewhere than Windows nothing it holds stops a removal. Remove them by hand once nothing runs from them."));
    }

    /// <summary>The variable that moves the directory a single-file build unpacks into away from the host's default.</summary>
    internal const string ExtractBaseVariable = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";

    /// <summary>Where this process's host unpacks, where the variable moves it; null where the host's default holds.</summary>
    private static string? ExtractBase(CliEnvironment env) =>
        env.Variables is { } variables
            ? variables.GetValueOrDefault(ExtractBaseVariable)
            : Environment.GetEnvironmentVariable(ExtractBaseVariable);

    /// <summary>Whether <paramref name="directory"/> is <paramref name="path"/> or one of its ancestors.</summary>
    private static bool Holds(string directory, string path)
    {
        var outer = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var inner = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return inner.StartsWith(outer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every root a deployment writes into: the harnesses a person chose, every folder the table of agent hosts keeps skills in
    /// under the home whether or not its host is still there, and the runtime's own, two of them one root by the comparer
    /// detection and the status check read roots by. Removal stays by receipt, so a folder no record names loses nothing.
    /// </summary>
    /// <remarks>
    /// The role skills root lives inside the data directory and is read anyway, because it holds a deployment
    /// Jason made and a record saying so — it is not the operator's own work, the way the database and the
    /// settings beside it are. So it goes by receipt even when the data directory is kept.
    /// </remarks>
    private static IEnumerable<string> Roots(CliEnvironment env) =>
        (env.Harnesses is { } locator ? locator.Detect().Concat(locator.Recorded()).Select(root => root.Directory) : [])
            .Append(env.Paths.RoleSkillsDirectory)
            .Distinct(HarnessLocators.SamePath);

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
