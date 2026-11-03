using System.Globalization;
using System.Security;
using Jason.Contracts.Notifications;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>
/// The per-user <c>jason:</c> link registration, <see cref="LinkRegistration.Key"/>, ensured by the toast host right
/// after the AppUserModelID key and before the toast is made: so a toast's click starts the executable that showed it.
/// Written only where it is absent, or ours and saying something else; never over another program's key, never where a
/// machine-wide key of another program would be shadowed, and never where this account chose another program for the
/// scheme.
/// </summary>
/// <remarks>
/// The marker is written first. A write refused part-way leaves a key that is still ours, which the next toast
/// finishes; written last, it would leave a key no later toast could tell from another program's.
/// </remarks>
public static class SchemeRegistration
{
    public const string Present = "present";

    public const string Written = "written";

    /// <summary>Another program owns <c>jason:</c>: nothing is written, and the toast carries no link.</summary>
    public const string Foreign = "foreign";

    public const string Failed = "failed";

    /// <param name="self">How this executable is started, which the command names.</param>
    /// <returns>
    /// <c>present</c>, <c>written</c>, <c>foreign</c>, or <c>failed</c> with the write's HRESULT: a refused write is an
    /// answer, not an exception.
    /// </returns>
    public static (string State, string? Hr) Ensure(IUserRegistry registry, IReadOnlyList<string> self)
    {
        ArgumentNullException.ThrowIfNull(registry);
        try
        {
            var command = LinkRegistration.Command(self);
            (string Key, string Name, string Value)[] values =
            [
                (LinkRegistration.Key, LinkRegistration.MarkerValue, LinkRegistration.Marker),
                (LinkRegistration.Key, LinkRegistration.DefaultValue, LinkRegistration.Description),
                (LinkRegistration.Key, LinkRegistration.ProtocolValue, string.Empty),
            ];

            var marker = registry.ReadString(LinkRegistration.Key, LinkRegistration.MarkerValue);
            if (marker is null ? Claimed(registry) : !string.Equals(marker, LinkRegistration.Marker, StringComparison.Ordinal) || ChosenElsewhere(registry))
            {
                return (Foreign, null);
            }

            if (values.All(value => string.Equals(registry.ReadString(value.Key, value.Name), value.Value, StringComparison.Ordinal))
                && LinkRegistration.SameCommand(registry.ReadString(LinkRegistration.CommandKey, LinkRegistration.DefaultValue), command))
            {
                return (Present, null);
            }

            values = [.. values, (LinkRegistration.CommandKey, LinkRegistration.DefaultValue, command)];

            foreach (var (key, name, value) in values)
            {
                registry.WriteString(key, name, value);
            }

            return (Written, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException or ArgumentException)
        {
            return (Failed, "0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Whether something without our marker already answers for <c>jason:</c>: a per-user key of another program, a
    /// machine-wide one the merged view shows, which a per-user key of ours would shadow, or another program this account
    /// chose for the scheme.
    /// </summary>
    private static bool Claimed(IUserRegistry registry) =>
        ChosenElsewhere(registry)
        || registry.ReadString(LinkRegistration.Key, LinkRegistration.DefaultValue) is not null
        || registry.ReadString(LinkRegistration.Key, LinkRegistration.ProtocolValue) is not null
        || registry.ReadString(LinkRegistration.CommandKey, LinkRegistration.DefaultValue) is not null
        || registry.ReadClassesString(LinkRegistration.ClassesKey, LinkRegistration.DefaultValue) is not null
        || registry.ReadClassesString(LinkRegistration.ClassesKey, LinkRegistration.ProtocolValue) is not null
        || registry.ReadClassesString(LinkRegistration.ClassesCommandKey, LinkRegistration.DefaultValue) is not null;

    /// <summary>
    /// Whether this account chose a program other than Jason's own class for <c>jason:</c> links: Windows sends a link
    /// there whatever the class key says, so it is read whether or not the key is ours. A class name ignores case.
    /// </summary>
    private static bool ChosenElsewhere(IUserRegistry registry) =>
        registry.ReadString(LinkRegistration.UserChoiceKey, LinkRegistration.ProgIdValue) is { } chosen
        && !string.Equals(chosen, AttentionLinks.Scheme, StringComparison.OrdinalIgnoreCase);
}
