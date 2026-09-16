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
        // Where a child's own configuration lives when the user moved it: the counterpart of APPDATA above, and
        // the difference between a vendor CLI finding its credential store and looking in an empty directory.
        "XDG_CONFIG_HOME",
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
    /// Whole families a plugin may never set for a program it starts: the operating system's dynamic loaders and
    /// the .NET profiler hooks. Every name under these prefixes exists to make a program load code chosen by
    /// whoever set the variable, so the family is refused rather than the handful of names anyone remembers.
    /// </summary>
    public static IReadOnlyList<string> InjectionPrefixes { get; } = ["LD_", "DYLD_", "COMPLUS_", "CORECLR_"];

    /// <summary>
    /// Individual names a plugin may never set. Each one loads code, or decides which program a name resolves
    /// to, before the started program's own first line runs — so setting one turns "start a program the user
    /// granted" into "run whatever the plugin brought with it, inside that program". The families whose every
    /// member does this are in <see cref="InjectionPrefixes"/>; the runtimes below have benign variables too
    /// (<c>NODE_ENV</c>, <c>DOTNET_NOLOGO</c>), so those are named one by one rather than by prefix.
    /// </summary>
    public static IReadOnlySet<string> InjectionNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // What a name resolves to, and what interprets it.
        "PATH", "PATHEXT", "COMSPEC", "SHELL", "BASH_ENV", "ENV",

        // .NET.
        "DOTNET_STARTUP_HOOKS", "DOTNET_ROOT", "DOTNET_ROOT(x86)", "DOTNET_ADDITIONAL_DEPS",
        "DOTNET_SHARED_STORE", "DOTNET_BUNDLE_EXTRACT_BASE_DIR",

        // Node.
        "NODE_OPTIONS", "NODE_PATH", "NODE_REPL_EXTERNAL_MODULE",

        // Python.
        "PYTHONPATH", "PYTHONSTARTUP", "PYTHONHOME", "PYTHONINSPECT",

        // Ruby and Perl.
        "RUBYOPT", "RUBYLIB", "PERL5OPT", "PERL5LIB", "PERL5DB",

        // Java.
        "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS", "JAVA_OPTIONS", "CLASSPATH",
    };

    /// <summary>
    /// Whether a plugin may set <paramref name="name"/> for a child it starts: not a reserved <c>JASON_*</c>
    /// name, not one of the loader or interpreter hooks. The grants are the trust boundary, and a plugin that
    /// could set these would be choosing the code inside a program the user granted, which is a different
    /// decision from the one the user made.
    /// </summary>
    public static bool MayAPluginSet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase) || InjectionNames.Contains(name))
        {
            return false;
        }

        foreach (var prefix in InjectionPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

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
