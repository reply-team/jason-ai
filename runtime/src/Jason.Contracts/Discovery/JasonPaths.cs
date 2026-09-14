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

    /// <summary>The directories the runtime creates on start.</summary>
    public IEnumerable<string> Layout => [StateDirectory, ConfigDirectory, PluginsDirectory, RunDirectory, LogsDirectory, WorkDirectory];
}
