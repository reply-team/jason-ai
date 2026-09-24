namespace Jason.Contracts.Discovery;

/// <summary>
/// The user-scoped data directory: <c>~/.jason</c> on every platform, or the directory named by the
/// <c>JASON_DATA_DIR</c> environment variable. Exactly one runtime per OS user owns it.
/// </summary>
public sealed class JasonPaths
{
    public const string DataDirectoryVariable = "JASON_DATA_DIR";

    public JasonPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public static JasonPaths FromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        return new JasonPaths(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jason")
            : configured);
    }

    public string Root { get; }
    public string StateDirectory => Path.Combine(Root, "state");
    public string BackupsDirectory => Path.Combine(StateDirectory, "backups");
    public string DatabaseFile => Path.Combine(StateDirectory, "jason.db");
    public string ConfigDirectory => Path.Combine(Root, "config");
    public string UserSettingsFile => Path.Combine(ConfigDirectory, "settings.json");
    public string PluginsDirectory => Path.Combine(Root, "plugins");
    public string RunDirectory => Path.Combine(Root, "run");
    public string DescriptorFile => Path.Combine(RunDirectory, "runtime.json");
    public string LockFile => Path.Combine(RunDirectory, "runtime.lock");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string WorkDirectory => Path.Combine(Root, "work");

    /// <summary>The directory one attempt runs in; created by the launcher, never cleaned up in this version.</summary>
    public string AttemptWorkDirectory(string workItemId, string attemptId) => Path.Combine(WorkDirectory, workItemId, attemptId);

    /// <summary>
    /// Where the skills that teach a role its job are kept: one directory per role, named as the role is. The
    /// launcher copies a role's own into the attempt's work directory, so an agent is taught by what was there
    /// when its attempt started rather than by whatever a host happens to find on the machine.
    /// </summary>
    public string RoleSkillsDirectory => Path.Combine(Root, "skills", "roles");

    /// <summary>
    /// Where a skills deployment assembles a role's tree before it is renamed into place, and where the tree
    /// it replaces is kept until the replacement is complete.
    /// </summary>
    /// <remarks>
    /// Deliberately a sibling of <see cref="RoleSkillsDirectory"/> rather than a directory inside it: the
    /// runtime reports every directory under the role root as a deployed role, so staging inside it would be
    /// reported as one — by the operation an operator asks whether this installation is ready. A sibling under
    /// the same parent also keeps both renames on one volume, so both stay metadata operations rather than
    /// turning into a copy.
    /// </remarks>
    public string SkillsStagingDirectory => Path.Combine(Root, "skills", ".staging");

    /// <summary>The parent of every staged source, and the only directory a staged one may be inside.</summary>
    public string SkillsStagedSourcesDirectory => Path.Combine(Root, "skills", "staged");

    /// <summary>
    /// Where a skills source fetched from git is unpacked, one directory per ref. Here rather than composed
    /// wherever the fetching happens: this file is what knows the layout, and a second place that knows where
    /// Jason writes is a second place to change when the layout moves.
    /// </summary>
    /// <remarks>
    /// <b>The name is refused unless it is one ordinary directory name.</b> This composes a path out of text
    /// that arrives from a command line, and the caller deletes what it composes before unpacking into it — so
    /// a name that could climb is a way to delete somewhere else. <c>..</c> is the whole attack and it is
    /// ordinary enough to be typed by accident: it survives a filter that keeps dots, and this path then
    /// resolves to the directory holding every deployed skill. Refused here rather than upstream, because this
    /// is where the promise that it cannot walk anywhere has to be true.
    /// </remarks>
    public string SkillsStagedSourceDirectory(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // All dots is its own case, and not a theoretical one: Windows strips trailing dots from a segment,
        // so "..." resolves to this staging root itself -- which the caller would then delete whole. It is
        // refused by name here and caught again by the property below, because a rule that only knows the
        // spellings somebody thought of is a rule against those spellings.
        if (name.All(character => character == '.')
            || name.Contains('/', StringComparison.Ordinal)
            || name.Contains('\\', StringComparison.Ordinal)
            || name.Contains(':', StringComparison.Ordinal)
            || name != Path.GetFileName(name)
            || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
        {
            throw new ArgumentException(
                $"'{name}' is not an ordinary directory name, so it is not somewhere this may unpack a source.",
                nameof(name));
        }

        var staged = Path.Combine(SkillsStagedSourcesDirectory, name);

        // Composed and then checked, because a name this lets through is worse than useless: the caller
        // deletes this path before it unpacks. Two ways of being wrong are cheaper than one when the failure
        // is somebody's data. The length is part of it -- resolving to the staging root itself passes a
        // prefix test and is exactly what "..." does.
        var parent = Path.GetFullPath(SkillsStagedSourcesDirectory) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(staged);
        if (resolved.Length <= parent.Length || !resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{name}' composes '{resolved}', which is not a directory inside '{SkillsStagedSourcesDirectory}'.", nameof(name));
        }

        return staged;
    }

    /// <summary>Where one installed plugin lives: the directory name is the plugin's id, and the manifest must agree.</summary>
    public string PluginPackageDirectory(string pluginId) => Path.Combine(PluginsDirectory, pluginId);

    /// <summary>The parent of every plugin invocation's working directory.</summary>
    public string PluginWorkDirectory => Path.Combine(WorkDirectory, "plugins");

    /// <summary>The directory one invocation runs in; the child's cwd and the cwd of everything it starts.</summary>
    public string PluginInvocationDirectory(string invocationId) => Path.Combine(PluginWorkDirectory, invocationId);

    /// <summary>The directories the runtime creates on start.</summary>
    public IEnumerable<string> Layout => [StateDirectory, ConfigDirectory, PluginsDirectory, RunDirectory, LogsDirectory, WorkDirectory];

    /// <summary>
    /// The name of every entry this product ever puts directly in the data directory: the runtime's layout, the
    /// skills it deploys, what an update keeps and the document a Windows logon registration is made from.
    /// </summary>
    /// <remarks>
    /// What <c>jason uninstall --purge-data</c> removes, and nothing else. <c>JASON_DATA_DIR</c> may name any
    /// directory at all — a home directory, a shared folder, one somebody keeps their own files in — and a purge
    /// that deleted the directory it names with everything in it would delete those as well. A test reads every
    /// path this product composes under the data directory and holds each to a name on this list.
    /// </remarks>
    public static IReadOnlyList<string> OwnEntries { get; } = ["state", "config", "plugins", "run", "logs", "work", "skills", "update", "autostart"];
}
