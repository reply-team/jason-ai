using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// A registry held in two dictionaries: this account's own keys, and the merged view Windows reads a class from, where
/// another program's machine-wide key shows. Nothing here reaches the real registry.
/// </summary>
internal sealed class FakeUserRegistry : IUserRegistry
{
    /// <summary>Values under <c>HKEY_CURRENT_USER</c>.</summary>
    public Dictionary<(string Key, string Name), string> Values { get; } = [];

    /// <summary>Values the merged <c>HKEY_CLASSES_ROOT</c> view shows beside this account's.</summary>
    public Dictionary<(string Key, string Name), string> Classes { get; } = [];

    /// <summary>Every write, in order.</summary>
    public List<(string Key, string Name, string Value)> Writes { get; } = [];

    /// <summary>Thrown by every write, where set.</summary>
    public Exception? Refuse { get; init; }

    /// <summary>How many writes succeed before <see cref="Refuse"/> is thrown.</summary>
    public int RefuseAfter { get; init; }

    public string? ReadString(string key, string name) => Values.GetValueOrDefault((key, name));

    public string? ReadClassesString(string key, string name) => Classes.GetValueOrDefault((key, name));

    public void WriteString(string key, string name, string value)
    {
        if (Refuse is not null && Writes.Count >= RefuseAfter)
        {
            throw Refuse;
        }

        Writes.Add((key, name, value));
        Values[(key, name)] = value;
    }
}
