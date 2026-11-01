namespace Jason.Contracts.Notifications;

/// <summary>
/// What happened to a notification on one channel. Three of these are what any delivery reports; <see cref="Silenced"/>
/// is the toast's alone, because only the operating system's own notification can be switched off by the person it
/// is for.
/// </summary>
public static class ChannelResults
{
    /// <summary>The channel took it: a toast shown, a message accepted by the server a plugin talks to.</summary>
    public const string Delivered = "delivered";

    /// <summary>
    /// Shown into nothing: the person switched Jason's notifications off in their operating system's settings. Nothing
    /// broke and nothing was skipped, so it is neither a failure nor "not attempted".
    /// </summary>
    public const string Silenced = "silenced";

    /// <summary>Tried, and it did not get there; a code and a failure class say why.</summary>
    public const string Failed = "failed";

    /// <summary>Not tried, for a reason decided before anything was launched.</summary>
    public const string NotAttempted = "not_attempted";

    public static IReadOnlyList<string> All { get; } = [Delivered, Silenced, Failed, NotAttempted];
}

/// <summary>Why a channel was not tried.</summary>
public static class NotAttemptedReasons
{
    /// <summary>The channel is turned off in the settings.</summary>
    public const string Off = "off";

    /// <summary>There is no desktop to show a toast on: a service session, or no session bus or notifier on Linux.</summary>
    public const string NoDesktop = "no_desktop";

    /// <summary>The plugin named in the settings does not declare the delivery contract.</summary>
    public const string NotDeclared = "not_declared";

    /// <summary>The plugin named in the settings is not loaded, or cannot run on this machine.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>The binding in the settings does not satisfy the plugin's own schema.</summary>
    public const string BindingInvalid = "binding_invalid";

    public static IReadOnlyList<string> All { get; } = [Off, NoDesktop, NotDeclared, Unavailable, BindingInvalid];
}

/// <summary>Why a toast was shown into nothing, as the operating system reports the person's own setting.</summary>
public static class SilencedReasons
{
    /// <summary>Notifications from Jason are switched off.</summary>
    public const string DisabledForApplication = "disabled_for_application";

    /// <summary>Notifications are switched off for this account altogether.</summary>
    public const string DisabledForUser = "disabled_for_user";

    /// <summary>A group policy on this machine switched notifications off.</summary>
    public const string DisabledByGroupPolicy = "disabled_by_group_policy";

    /// <summary>The application's own manifest declares no notifications.</summary>
    public const string DisabledByManifest = "disabled_by_manifest";

    public static IReadOnlyList<string> All { get; } = [DisabledForApplication, DisabledForUser, DisabledByGroupPolicy, DisabledByManifest];
}

/// <summary>The two kinds of channel: the operating system's own notification, and a notification plugin.</summary>
public static class ChannelKinds
{
    public const string Toast = "toast";

    public const string Plugin = "plugin";
}

/// <summary>Whether the toast has a desktop to be shown on, as <c>system.info</c> says it.</summary>
public static class ChannelPresence
{
    public const string Available = "available";

    public const string NoDesktop = "no_desktop";

    /// <summary>
    /// The detail of a Windows runtime in session 0, where nothing it shows reaches a desktop. The runtime writes it, and
    /// <c>jason status</c> keys its advice on it.
    /// </summary>
    public const string SessionZero = "session 0";

    /// <summary>The detail of a Linux runtime with no session bus to send a notification over.</summary>
    public const string NoSessionBus = "no session bus";

    /// <summary>The detail of a Linux runtime with a session bus and no program to talk to the notifier with.</summary>
    public const string NoNotificationProgram = "no notification program";

    /// <summary>
    /// Every detail the runtime itself gives a missing desktop: a closed set of its own words. A detail in it is the
    /// runtime's fact and may be shown anywhere; anything else is text nobody controls, which a diagnostic bundle leaves out.
    /// </summary>
    public static IReadOnlyList<string> Details { get; } = [SessionZero, NoSessionBus, NoNotificationProgram];
}
