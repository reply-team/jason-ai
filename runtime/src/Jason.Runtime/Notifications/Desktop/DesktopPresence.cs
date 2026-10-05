using System.Collections;
using System.Diagnostics;
using Jason.Contracts.Notifications;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>The three platforms a toast is composed for.</summary>
public enum DesktopPlatform
{
    Windows,
    MacOs,
    Linux,
}

/// <summary>
/// What this process knows about where it runs: the platform, the session it belongs to, its environment, and how to
/// find a program or a file. Handed in, so that every answer below is a function of facts a test can write.
/// </summary>
public sealed record DesktopFacts(
    DesktopPlatform Platform,
    int SessionId,
    IReadOnlyDictionary<string, string> Environment,
    Func<string, string?> Which,
    Func<string, bool> FileExists)
{
    /// <summary>This process's own facts, read without launching anything and without touching WinRT.</summary>
    public static DesktopFacts Current()
    {
        var environment = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                environment[name] = value;
            }
        }

        using var self = Process.GetCurrentProcess();
        return new DesktopFacts(
            OperatingSystem.IsWindows() ? DesktopPlatform.Windows : OperatingSystem.IsMacOS() ? DesktopPlatform.MacOs : DesktopPlatform.Linux,
            self.SessionId,
            environment,
            program => Find(program, environment),
            File.Exists);
    }

    /// <summary>A program on this environment's PATH, the way a shell would find it, or null.</summary>
    private static string? Find(string program, Dictionary<string, string> environment)
    {
        if (!environment.TryGetValue("PATH", out var path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>Whether a toast can be shown at all: a desktop, or none and why.</summary>
public sealed record Presence(bool Available, string? Detail)
{
    public static Presence Desktop { get; } = new(true, null);

    public static Presence NoDesktop(string detail) => new(false, detail);
}

/// <summary>
/// Whether there is a desktop to show a toast on, decided per platform before anything is launched.
/// </summary>
/// <remarks>
/// <para>
/// On Windows the session decides it, and nothing else is asked: a process in session 0 is a service's, which has no
/// desktop — a 0.1.0 logon task's runtime runs there — and a toast shown from it goes nowhere. The answer comes from
/// the session id alone, before any process starts and before any WinRT call is made.
/// </para>
/// <para>
/// On Linux a notification travels over the user's session bus to a notification daemon, so there is no desktop
/// without a bus — the variable that names it, or its socket in the runtime directory — or without a program to speak
/// to it. Whether a daemon is on the bus is asked when a toast is sent. A runtime started by a systemd user unit has
/// the user's bus while the user is signed in; without lingering, the unit and its bus exist only while they are.
/// </para>
/// <para>
/// On macOS nothing can be known before trying: <c>osascript</c> is always there, and one that fails says so when it
/// is run.
/// </para>
/// </remarks>
public static class DesktopPresence
{
    public static Presence Find(DesktopFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.Platform switch
        {
            DesktopPlatform.Windows => facts.SessionId == 0 ? Presence.NoDesktop(ChannelPresence.SessionZero) : Presence.Desktop,
            DesktopPlatform.Linux => Linux(facts),
            _ => Presence.Desktop,
        };
    }

    private static Presence Linux(DesktopFacts facts)
    {
        var bus = Has(facts, "DBUS_SESSION_BUS_ADDRESS")
            || (facts.Environment.TryGetValue("XDG_RUNTIME_DIR", out var runtime) && !string.IsNullOrEmpty(runtime) && facts.FileExists(runtime.TrimEnd('/') + "/bus"));
        if (!bus)
        {
            return Presence.NoDesktop(ChannelPresence.NoSessionBus);
        }

        return facts.Which("notify-send") is not null || facts.Which("gdbus") is not null
            ? Presence.Desktop
            : Presence.NoDesktop(ChannelPresence.NoNotificationProgram);
    }

    private static bool Has(DesktopFacts facts, string name) =>
        facts.Environment.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value);
}
