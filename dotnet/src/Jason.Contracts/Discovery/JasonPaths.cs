namespace Jason.Contracts.Discovery;

/// <summary>
/// The user-scoped data directory: <c>~/.jason</c> on every platform, or the directory named by the
/// <c>JASON_DATA_DIR</c> environment variable. Exactly one runtime per OS user owns it.
/// </summary>
/// <remarks>
/// <para>
/// The parts a person sets up — the plugins, the role skills, a custom UI and the settings — are placed by a layout, which
/// the data directory's stamp names (<see cref="LayoutStamp"/>), and every member for one of them answers by it. Layout 1,
/// the layout every Jason before the stamp kept, is the one a missing stamp means; layout 2 keeps them all in
/// <c>setup/</c>, with plugins' configuration in a secrets file of its own.
/// </para>
/// <para>
/// <see cref="JasonPaths(string)"/> reads the stamp, once, and records what it found without ever throwing on it; the
/// points that touch a part that moves refuse by <see cref="LayoutStamp.Refusal"/>. <see cref="JasonPaths(string, int)"/>
/// is given its layout and reads nothing.
/// </para>
/// </remarks>
public sealed class JasonPaths
{
    public const string DataDirectoryVariable = "JASON_DATA_DIR";

    /// <summary>The data directory's folder in the home directory when <see cref="DataDirectoryVariable"/> names none.</summary>
    public const string DefaultFolder = ".jason";

    /// <summary>The newest layout this file places the parts by. A stamp naming a later one is answered by this one.</summary>
    public const int NewestTable = 2;

    /// <summary>The folder layout 2 keeps the parts a person sets up in.</summary>
    public const string SetupFolder = "setup";

    /// <summary>The data directory at <paramref name="root"/>, laid out as its stamp says: read here, once.</summary>
    public JasonPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        Stamp = LayoutStamp.Read(Root);
        Layout = Stamp.Answered;
    }

    /// <summary>
    /// The data directory at <paramref name="root"/> in <paramref name="layout"/>, which is given rather than read: nothing
    /// is read, and nothing is refused by it. A test's, or the code that moves the parts.
    /// </summary>
    public JasonPaths(string root, int layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentOutOfRangeException.ThrowIfLessThan(layout, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(layout, NewestTable);
        Root = Path.GetFullPath(root);
        Layout = layout;
    }

    public static JasonPaths FromEnvironment() => new(RootFromEnvironment());

    /// <summary>The data directory this environment names: <see cref="DataDirectoryVariable"/>, else <see cref="DefaultFolder"/> in the home directory. Nothing is read.</summary>
    public static string RootFromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), DefaultFolder)
            : configured;
    }

    public string Root { get; }

    /// <summary>The layout the parts are placed by: the stamp's, 1 where there is none or it cannot be read, or the one given.</summary>
    public int Layout { get; }

    /// <summary>What the stamp was found to say when it was read; null when the layout was given.</summary>
    public LayoutStamp? Stamp { get; }

    /// <summary>The layout stamp, in every layout.</summary>
    public string LayoutFile => Path.Combine(Root, LayoutStamp.FileName);

    public string StateDirectory => Path.Combine(Root, "state");
    public string BackupsDirectory => Path.Combine(StateDirectory, "backups");
    public string DatabaseFile => Path.Combine(StateDirectory, "jason.db");
    public string ConfigDirectory => Path.Combine(Root, "config");

    /// <summary>The settings file a person may change: <c>config/settings.json</c> in layout 1, <c>setup/settings.json</c> in layout 2.</summary>
    public string UserSettingsFile => Layout < 2 ? Path.Combine(ConfigDirectory, "settings.json") : Path.Combine(SetupDirectory, "settings.json");

    /// <summary>
    /// Where plugins' configuration (<c>Plugins:Config</c>), their secrets among it, is kept: in layout 1 the settings file
    /// itself, which holds both; in layout 2 <c>config/secrets.json</c>. Where the two are one file it is one source of
    /// settings, so a reader that adds both adds this one only while <see cref="UserSettingsFileHoldsSecrets"/> is false.
    /// </summary>
    public string SecretsFile => Layout < 2 ? UserSettingsFile : Path.Combine(ConfigDirectory, "secrets.json");

    /// <summary>
    /// Whether this layout keeps plugins' configuration (<c>Plugins:Config</c>), their secrets among it, in
    /// <see cref="UserSettingsFile"/> as well: in layout 1 there is one settings file, and it does; in layout 2 they are in
    /// <see cref="SecretsFile"/>. A fact of the layout, never of the file's content, so it holds for a file with no such
    /// key and for no file at all.
    /// </summary>
    public bool UserSettingsFileHoldsSecrets => Layout < 2;
    public string PluginsDirectory => Layout < 2 ? Path.Combine(Root, "plugins") : Path.Combine(SetupDirectory, "plugins");
    public string UiDirectory => Path.Combine(Root, "ui");
    public string UiDefaultDirectory => Path.Combine(UiDirectory, "default");
    public string UiCustomDirectory => Layout < 2 ? Path.Combine(UiDirectory, "custom") : Path.Combine(SetupDirectory, "ui");
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
    public string RoleSkillsDirectory => Layout < 2 ? Path.Combine(Root, "skills", "roles") : Path.Combine(SetupDirectory, "roles");

    /// <summary>
    /// Where a skills deployment assembles a role's tree before it is renamed into place, and where the tree
    /// it replaces is kept until the replacement is complete.
    /// </summary>
    /// <remarks>
    /// Deliberately a sibling of <see cref="RoleSkillsDirectory"/> rather than a directory inside it: the
    /// runtime reports every directory under the role root as a deployed role, so staging inside it would be
    /// reported as one — by the operation an operator asks whether this installation is ready. A sibling under
    /// the same parent also keeps both renames on one volume, so both stay metadata operations rather than
    /// turning into a copy. In layout 2 it is <c>setup/.staging</c>, a sibling there too.
    /// </remarks>
    public string SkillsStagingDirectory => Layout < 2 ? Path.Combine(Root, "skills", ".staging") : Path.Combine(SetupDirectory, ".staging");

    /// <summary>The parent of every staged source, and the only directory a staged one may be inside.</summary>
    public string SkillsStagedSourcesDirectory => Path.Combine(Root, "skills", "staged");

    /// <summary>
    /// Where a skills source fetched from git or a release is unpacked, one directory per ref. Here rather than composed
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
    public IEnumerable<string> StartDirectories => [StateDirectory, ConfigDirectory, PluginsDirectory, RunDirectory, LogsDirectory, WorkDirectory];

    /// <summary><c>setup/</c>, where layout 2 keeps the parts a person sets up; composed by the members that answer by it alone.</summary>
    private string SetupDirectory => Path.Combine(Root, SetupFolder);

    /// <summary>
    /// The name of every entry this product ever puts directly in the data directory: the runtime's layout, the
    /// skills it deploys, what an update keeps, the document a Windows logon registration is made from, the desktop
    /// application's own folder, the layout stamp and the folder layout 2 keeps a person's parts in — named in every
    /// layout, so that a purge removes a folder a later build made and a fence knows it before it exists.
    /// </summary>
    /// <remarks>
    /// What <c>jason uninstall --purge-data</c> removes, and nothing else. <c>JASON_DATA_DIR</c> may name any
    /// directory at all — a home directory, a shared folder, one somebody keeps their own files in — and a purge
    /// that deleted the directory it names with everything in it would delete those as well. A test reads every
    /// path this product composes under the data directory and holds each to a name on this list.
    /// </remarks>
    public static IReadOnlyList<string> OwnEntries { get; } = ["state", "config", "plugins", "run", "logs", "work", "skills", "update", "autostart", "ui", "desktop", LayoutStamp.FileName, SetupFolder];
}
