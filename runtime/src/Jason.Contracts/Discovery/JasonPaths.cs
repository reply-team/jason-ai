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

    /// <summary>
    /// Where a skills source fetched from git is unpacked, one directory per ref. Here rather than composed
    /// wherever the fetching happens: this file is what knows the layout, and a second place that knows where
    /// Jason writes is a second place to change when the layout moves.
    /// </summary>
    public string SkillsStagedSourceDirectory(string reference) => Path.Combine(Root, "skills", "staged", reference);

    /// <summary>Where one installed plugin lives: the directory name is the plugin's id, and the manifest must agree.</summary>
    public string PluginPackageDirectory(string pluginId) => Path.Combine(PluginsDirectory, pluginId);

    /// <summary>The parent of every plugin invocation's working directory.</summary>
    public string PluginWorkDirectory => Path.Combine(WorkDirectory, "plugins");

    /// <summary>The directory one invocation runs in; the child's cwd and the cwd of everything it starts.</summary>
    public string PluginInvocationDirectory(string invocationId) => Path.Combine(PluginWorkDirectory, invocationId);

    /// <summary>The directories the runtime creates on start.</summary>
    public IEnumerable<string> Layout => [StateDirectory, ConfigDirectory, PluginsDirectory, RunDirectory, LogsDirectory, WorkDirectory];
}
