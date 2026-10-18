namespace Jason.Contracts.Api;

/// <summary>Response of <c>system.info</c>: what the runtime says about itself. Non-business by design.</summary>
/// <param name="Update">What the last successful update check learned; null until there has been one.</param>
/// <param name="Skills">
/// The role skills this runtime will teach from; null on a runtime older than the section, which reads the
/// same way as the other sections that arrived late.
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
    UiModuleInfo? Ui = null);

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
/// Where work is being sent, in five values. It sits beside <see cref="PluginsInfo"/> because "what is running"
/// is only half of the question an operator is asking: the other half is which plugin each operation reaches,
/// and the two are frozen by the same act. The routes themselves are read through <c>route.list</c>.
/// </summary>
public sealed record RoutesInfo(
    string SnapshotId,
    DateTimeOffset ActivatedAt,
    string? GlobalDefaultPlugin,
    int GlobalOverrideCount,
    int CampaignRouteCount);
