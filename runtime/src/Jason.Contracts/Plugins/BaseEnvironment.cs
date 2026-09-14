namespace Jason.Contracts.Plugins;

/// <summary>
/// The environment a plugin-host process — and therefore every program it starts — is given. It is built from
/// nothing rather than inherited: only the machine configuration a child plausibly needs, plus the variables the
/// user granted this plugin, by name. Never a <c>JASON_*</c> variable, so a child cannot even find the data
/// directory, let alone the database inside it.
/// </summary>
public static class BaseEnvironment
{
    public const string ReservedPrefix = "JASON_";

    /// <summary>
    /// Copied on every operating system. The proxy variables are machine configuration, not plugin secrets: the
    /// host's own HTTP client honours them and a vendor CLI behind a corporate proxy needs them equally.
    /// </summary>
    public static IReadOnlyList<string> Common { get; } =
    [
        "PATH",
        "DOTNET_ROOT",
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "NO_PROXY",
        "http_proxy",
        "https_proxy",
        "no_proxy",
    ];

    public static IReadOnlyList<string> Windows { get; } =
    [
        "PATHEXT",
        "SystemRoot",
        "SystemDrive",
        "windir",
        "ComSpec",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "HOMEDRIVE",
        "HOMEPATH",
        "APPDATA",
        "LOCALAPPDATA",
        "ProgramData",
        "ProgramFiles",
        "ProgramFiles(x86)",
        "ProgramW6432",
        "NUMBER_OF_PROCESSORS",
        "PROCESSOR_ARCHITECTURE",
        "OS",
        "USERNAME",
    ];

    public static IReadOnlyList<string> Unix { get; } =
    [
        "HOME",
        "TMPDIR",
        "LANG",
        "LC_ALL",
        "LC_CTYPE",
        "USER",
        "LOGNAME",
        "SHELL",
        "TERM",
    ];

    public static IReadOnlyList<string> ForCurrentOs { get; } =
        [.. Common, .. OperatingSystem.IsWindows() ? Windows : Unix];

    /// <summary>
    /// Copies from <paramref name="source"/> the names of <see cref="ForCurrentOs"/> and the granted ones that
    /// are present there. Names are compared the way the operating system compares them: case-insensitively on
    /// Windows, exactly everywhere else. A reserved name is never copied, granted or not.
    /// </summary>
    public static Dictionary<string, string> Build(IDictionary<string, string> source, IEnumerable<string> granted)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(granted);

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var available = new Dictionary<string, string>(comparer);
        foreach (var entry in source)
        {
            available[entry.Key] = entry.Value;
        }

        var built = new Dictionary<string, string>(comparer);
        foreach (var name in ForCurrentOs.Concat(granted))
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!built.ContainsKey(name) && available.TryGetValue(name, out var value))
            {
                built[name] = value;
            }
        }

        return built;
    }
}
