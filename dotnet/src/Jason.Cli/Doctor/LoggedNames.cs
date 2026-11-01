using System.Collections.Frozen;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Doctor;

/// <summary>
/// Where a line of the runtime's log names a plugin: by its id under <see cref="PluginIdProperty"/> whatever the line, and
/// under another property in the lines whose message templates <see cref="PluginTemplates"/> lists. A bundle reads every
/// event it carries for these, so a package that only the log still names — a folder the loader refused, a package used
/// and then removed — is written as its alias like any other. The client cannot reference the runtime, so the templates are
/// typed here, and the runtime's tests hold this list to the templates the runtime writes.
/// </summary>
public static class LoggedNames
{
    /// <summary>The property every line of the runtime's that names a plugin by its id carries it under.</summary>
    public const string PluginIdProperty = "PluginId";

    /// <summary>
    /// The templates of the runtime's lines that name a plugin under another property, each with that property: a
    /// candidate the loader refused or found unavailable, by its folder's name; a plugin's grants, by its id; and a
    /// notification channel that failed, by its name, which is a plugin's id unless it is the runtime's own
    /// <see cref="ChannelKinds.Toast"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PluginTemplates { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Plugin candidate {Directory} is not loadable: {Codes}"] = "Directory",
        ["Plugin {Directory} is unavailable on this machine: {Codes}"] = "Directory",
        ["Plugin {Id} granted {Exec} executables, {Http} hosts, {Env} variables"] = "Id",
        ["Notification channel {Channel} failed ({Code}, {Class})"] = "Channel",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The values those properties carry that name no plugin: the runtime's own channel.</summary>
    public static IReadOnlySet<string> NotAPlugin { get; } = new HashSet<string>(StringComparer.Ordinal) { ChannelKinds.Toast }.ToFrozenSet(StringComparer.Ordinal);
}
