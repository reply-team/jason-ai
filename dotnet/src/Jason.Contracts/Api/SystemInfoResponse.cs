namespace Jason.Contracts.Api;

/// <summary>Response of <c>system.info</c>: what the runtime says about itself. Non-business by design.</summary>
/// <param name="Update">What the last successful update check learned; null until there has been one.</param>
/// <param name="Skills">
/// The role skills this runtime will teach from; null on a runtime older than the section, which reads the
/// same way as the other sections that arrived late.
/// </param>
/// <param name="ReleasedPlugins">
/// The plugin packages this runtime's release publishes, each beside the package of that id the active snapshot holds;
/// null on a runtime older than the section.
/// </param>
/// <param name="SendModes">
/// The installation's send mode in force for a cold contact and for a warm one, each with where it comes from: the
/// preference, or the default, which is every message. Two closed words each and never a person's name or a value; a
/// campaign's own modes are the campaign's to answer. Null on a runtime older than the section.
/// </param>
public sealed record SystemInfoResponse(
    string RuntimeVersion,
    string ApiVersion,
    string InstanceId,
    int Pid,
    DateTimeOffset StartedAt,
    string DataDir,
    DatabaseInfo Database,
    DispatcherInfo Dispatcher,
    PluginsInfo Plugins,
    RoutesInfo Routes,
    UpdateInfo? Update = null,
    SkillsInfo? Skills = null,
    NotificationsInfo? Notifications = null,
    ListeningInfo? Listening = null,
    UiModuleInfo? Ui = null,
    HarnessInfo? Harness = null,
    IReadOnlyList<ReleasedPluginInfo>? ReleasedPlugins = null,
    SendModesInForce? SendModes = null);

/// <summary>
/// A plugin package this runtime's release publishes beside its archives, and the package of the same id the active
/// snapshot holds. The yardstick is the release this runtime is, never the newest one: whether a newer release exists is
/// the <c>update</c> section's to say, and the package a person installs is the one of the release they run.
/// </summary>
/// <param name="Installed">The package of this id in the active snapshot; null exactly when the state is <c>not_installed</c>.</param>
public sealed record ReleasedPluginInfo(string Id, ReleasedPluginState State, PluginPackageInfo Released, PluginPackageInfo? Installed);

/// <summary>One package as its manifest names it and as its content digest identifies it.</summary>
public sealed record PluginPackageInfo(string Version, string Digest);

/// <summary>How the installed package stands against the one this runtime's release publishes.</summary>
public enum ReleasedPluginState
{
    /// <summary>The installed package is the release's: the two content digests are equal.</summary>
    Current,

    /// <summary>The digests differ and the installed manifest names a lower version: a package an earlier release published.</summary>
    Behind,

    /// <summary>The digests differ and the installed version is the same or higher: a later release's package, or one built or edited on this machine.</summary>
    Differs,

    /// <summary>The active snapshot holds no package of this id.</summary>
    NotInstalled,
}

/// <summary>
/// The harness root in force — the settings' <c>Harness:Root</c>, or <c>Jason</c> in the home directory, the last value
/// that validated — and what stands there now. A runtime that has the section always answers it; null is a runtime older
/// than the section.
/// </summary>
public sealed record HarnessInfo(string Root, HarnessRootState RootState);

/// <summary>What stands at the harness root, as the next project or campaign made will meet it.</summary>
public enum HarnessRootState
{
    /// <summary>Nothing: the root is made, with its marker, at the first project or campaign.</summary>
    Absent,

    /// <summary>A folder with the root's marker.</summary>
    Marked,

    /// <summary>A folder with no marker: the first project or campaign takes it as the root, and changes nothing in it.</summary>
    Unmarked,

    /// <summary>A folder whose marker names something other than a root: a project or campaign is refused as <c>harness_root_taken</c>.</summary>
    Taken,

    /// <summary>A file where the folder belongs: a project or campaign is refused as <c>harness_folder_unavailable</c>.</summary>
    NotAFolder,

    /// <summary>A folder this account cannot list, or whose marker it cannot read: a project or campaign is refused as <c>harness_folder_unavailable</c>.</summary>
    Unreadable,
}

/// <summary>The port Kestrel took, and the one the settings asked for (0 when the OS chose). They differ when the configured one was taken.</summary>
public sealed record ListeningInfo(int Port, int ConfiguredPort);

/// <summary>
/// Whether this runtime tells a person when work starts waiting for them, and what each channel did with the last
/// notification. Null from a runtime older than notifications.
/// </summary>
public sealed record NotificationsInfo(
    bool Enabled,
    IReadOnlyList<NotificationChannelInfo> Channels,
    string? LastNotificationId,
    DateTimeOffset? LastAt);

/// <summary>
/// One channel: whether it is on, and its last outcome — one of <c>delivered</c>, <c>silenced</c>, <c>failed</c> and
/// <c>not_attempted</c>, or null before anything was sent. For the toast, <see cref="Presence"/> says whether this
/// runtime has a desktop to show one on at all, decided when it started: <c>available</c>, or <c>no_desktop</c> with
/// <see cref="PresenceDetail"/> saying why.
/// </summary>
public sealed record NotificationChannelInfo(
    string Channel,
    string Kind,
    bool On,
    string? Outcome,
    string? Reason,
    string? Code,
    string? Class,
    string? Detail,
    DateTimeOffset? At,
    string? Presence = null,
    string? PresenceDetail = null);

/// <summary>
/// The role skills this runtime will teach from, as they are on disk at the moment it is asked, and the cap
/// it will enforce.
/// </summary>
/// <remarks>
/// It reports the directory the runtime owns and nothing about the person's own agent harness: where somebody
/// keeps their own skills is not the runtime's to read. The cap is here because it is a live setting — an
/// installer that guessed it would validate a deployment against the wrong number, and be wrong in the
/// direction that refuses every launch of a role.
/// </remarks>
/// <param name="Problem">
/// Why this section could not be composed, and null when it was. A field rather than an exception because
/// this operation cannot fail: an applier resuming an update asks it precisely when a machine is in a bad
/// state, and a 500 here reads to that caller as a runtime that is not there at all.
/// </param>
/// <param name="ProblemCode">The problem's code from <see cref="Skills.SkillProblemCodes"/>; null when there is none.</param>
/// <param name="ProblemException">
/// Where the operating system refused, the closed word that names the refusal, from
/// <see cref="Skills.SkillProblemCodes.Exceptions"/>; null otherwise.
/// </param>
public sealed record SkillsInfo(
    string RoleSkillsDirectory,
    int MaxSkillBytes,
    IReadOnlyList<DeployedRoleSkill> Roles,
    string? Problem = null,
    string? ProblemCode = null,
    string? ProblemException = null);

/// <summary>One directory under the role root, as the launcher would find it.</summary>
/// <param name="Bytes">
/// Null when it could not be measured at all — a directory renamed away mid-walk, which a deployment causes.
/// Zero would read as an empty skill, which is a different thing and a survivable one.
/// </param>
/// <param name="Problem">
/// Why a launch would not be taught this, and null when it would. It covers both refusals the launcher makes
/// and the case it does not refuse but nobody meant: a directory with no <c>SKILL.md</c>, which is copied and
/// teaches a host nothing.
/// </param>
/// <param name="ProblemCode">The problem's code from <see cref="Skills.SkillProblemCodes"/>; null when there is none.</param>
/// <param name="ProblemException">
/// Where the operating system refused, the closed word that names the refusal, from
/// <see cref="Skills.SkillProblemCodes.Exceptions"/>; null otherwise.
/// </param>
public sealed record DeployedRoleSkill(string Role, long? Bytes, string? Problem, string? ProblemCode = null, string? ProblemException = null);

/// <summary>
/// What the database is, and what this start did to it: every migration that has been applied, the ones this
/// start applied itself, and the file it copied the database to before it did.
/// </summary>
/// <remarks>
/// The last two are for one caller. An applier that has just started a new binary has to know whether that
/// start migrated — because that is what decides whether rolling the binary back also means putting a database
/// back — and which file to put back. It never opens the database to find out; it asks.
/// </remarks>
public sealed record DatabaseInfo(
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> NewlyApplied,
    string? BackupFile);

/// <summary>
/// What the last successful update check learned: whether the version it found is newer than the one running,
/// which version that was, when it looked, and where the notes are. Absent until a check has succeeded, so a
/// runtime that has not looked says so rather than saying "up to date".
/// </summary>
public sealed record UpdateInfo(bool Available, string Version, DateTimeOffset CheckedAt, string? ReleaseNotesUrl);

/// <summary>What the dispatcher is doing right now: the cheapest way for an operator or a test to see the loop is alive.</summary>
public sealed record DispatcherInfo(
    DispatcherState State,
    int TickSeconds,
    int MaxParallel,
    int RunningAttempts,
    DateTimeOffset? LastScanAt,
    long Scans,
    long Summons);

/// <summary>
/// Whether the plugin registry is alive and what it holds, in four values: the cheapest way to see the active
/// snapshot without a second call. The registry itself is read through <c>plugin.list</c>.
/// </summary>
public sealed record PluginsInfo(
    int ActiveCount,
    string SnapshotId,
    DateTimeOffset LoadedAt,
    bool? LastReloadActivated);

/// <summary>
/// Where work is being sent, in six values. It sits beside <see cref="PluginsInfo"/> because "what is running"
/// is only half of the question an operator is asking: the other half is which plugin each operation reaches,
/// and the two are frozen by the same act. The routes themselves are read through <c>route.list</c>.
/// <para>
/// <c>GlobalOverrideCount</c> counts the global routes under an operation, the channel routes among them, and
/// <c>CampaignRouteCount</c> every campaign's routes, the channel routes among them too. <c>ChannelRouteCount</c> says how
/// many of those, global and campaign together, name a channel; null is a runtime older than channel routes, which is how a
/// client learns that it would ignore a channel in a request.
/// </para>
/// </summary>
public sealed record RoutesInfo(
    string SnapshotId,
    DateTimeOffset ActivatedAt,
    string? GlobalDefaultPlugin,
    int GlobalOverrideCount,
    int CampaignRouteCount,
    int? ChannelRouteCount = null);
