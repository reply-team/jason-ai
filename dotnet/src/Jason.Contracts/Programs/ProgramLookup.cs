using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jason.Contracts.Programs;

/// <summary>
/// The directories a bare program name is looked up in, and the extensions this machine says it may resolve through.
/// A value rather than a seam: where a process looks is that process's own question — an autostarted runtime sees a
/// different search path than a shell does — so each caller reads its own, once, and hands it here.
/// </summary>
/// <param name="Path">The directories in order, joined the way <c>PATH</c> joins them.</param>
/// <param name="PathExt">The extensions a bare name may resolve through on Windows; null everywhere else.</param>
public sealed record ProgramSearchPath(string? Path, string? PathExt);

/// <summary>What a bare name came to on the search path.</summary>
public enum ProgramLookupOutcome
{
    /// <summary>
    /// A program the operating system starts by itself: an <c>.exe</c> or a <c>.com</c> on Windows, a file with an execute bit
    /// elsewhere, or the <c>.exe</c> or <c>.com</c> a package ships that npm's shim starts with no interpreter in front of it.
    /// </summary>
    Program,

    /// <summary>npm's own <c>.cmd</c> shim, read rather than started: what runs is the interpreter it names, handed the entry script.</summary>
    NpmShim,

    /// <summary>Nothing on the search path answers for the name, or npm's shim for it names an executable that is not there.</summary>
    NotFound,

    /// <summary>Only a script a shell would have to interpret answers for it, and no <c>.cmd</c> among them.</summary>
    ShellOnly,

    /// <summary>Only a <c>.cmd</c> answers for it, and that one is not npm's shim or breaks a rule the shim is held to.</summary>
    RefusedShim,
}

/// <summary>A bare name as this machine answers for it, with nothing started.</summary>
/// <param name="Path">
/// The program that is actually started — the file itself, or for npm's shim the interpreter or the executable it
/// names — so a reader who knows only this keeps telling the truth. Null where nothing may be started.
/// </param>
/// <param name="Launch">
/// What goes on the command line before anything a caller asks for: empty for a program, the entry script for npm's
/// shim, where the program is the interpreter that runs it.
/// </param>
/// <param name="File">
/// The file the name resolved to on the search path: the program, the shim (also where it starts an executable), or
/// the first script that was refused; null where nothing answered. It is what a person looks for in a directory
/// listing, which <paramref name="Path"/> is not for a shim.
/// </param>
/// <param name="Refusal">For a refused shim, the rule it broke, worded to follow "which": "is not the shim npm writes".</param>
/// <param name="InterpreterPathRead">
/// Whether the lookup went to the interpreter search path to find a shim's node.exe, because none stood beside the shim.
/// It says nothing of the search path the name itself was looked up on.
/// </param>
public sealed record FoundProgram(
    string Name, ProgramLookupOutcome Outcome, string? Path, IReadOnlyList<string> Launch, string? File, string? Refusal, bool InterpreterPathRead = false);

/// <summary>
/// Where a bare program name is on this machine, answered the one way Jason answers it for every program it starts by
/// name: a plugin's declared executable, an agent host a profile names, and the provider CLI <c>jason status</c> is asked
/// to check. Two answers to "where is this program" would be one answer too many.
/// </summary>
/// <remarks>
/// It reads the file system and nothing else, and it starts nothing. Starting a program is an act, and each caller
/// decides for itself whether it may.
/// </remarks>
public static partial class ProgramLookup
{
    private static readonly string[] WindowsRefusedExtensions = [".cmd", ".bat", ".ps1"];

    /// <summary>
    /// What a file only a shell could start is, in the words every refusal of one uses — a plugin's declared program, the
    /// provider CLI <c>jason status</c> checks, and a profile's program — so the three cannot describe it three ways.
    /// </summary>
    public const string ShellScript = "a script a shell would have to interpret";

    /// <summary>Why such a file is refused, where the refusal is Jason's rather than one program's.</summary>
    public const string ProgramsNotShells = "Jason starts programs, never shells";

    /// <summary>
    /// Whether a program as written names a file only a shell could start: a <c>.cmd</c>, a <c>.bat</c> or a <c>.ps1</c>,
    /// in any case. Read from the name alone, on every platform.
    /// </summary>
    public static bool IsShellScript(string program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var extension = Path.GetExtension(program.Trim());
        return WindowsRefusedExtensions.Any(refused => refused.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether a program as written is a path rather than a plain file name: it holds a directory separator, it is
    /// rooted, or on Windows it holds a colon. The colon is the form the separators miss: <c>C:acme</c> names
    /// <c>acme</c> in the current folder of drive C, and joined to any directory it stays itself. A path is looked for
    /// in the directory it names, never on a search path; every caller that tells the two apart asks this.
    /// </summary>
    public static bool IsPath(string program)
    {
        ArgumentNullException.ThrowIfNull(program);

        return program.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || program.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(program)
            || (OperatingSystem.IsWindows() && program.Contains(':', StringComparison.Ordinal));
    }

    /// <summary>
    /// Walks the search path in order. On Windows a name resolves to an <c>.exe</c> or a <c>.com</c>, or — only
    /// when the whole path yielded neither — to the cmd shim npm writes, which is read rather than started:
    /// starting a <c>.cmd</c> or a <c>.bat</c> means <c>cmd.exe</c> interprets the arguments, which is the shell
    /// the argument-array invariant exists to exclude, so what runs is what the shim starts: the interpreter and entry
    /// script it names, or, in the form npm writes for a target with no shebang, the executable the package ships, by
    /// itself. Every other <c>.cmd</c>, and every <c>.bat</c> and <c>.ps1</c>, is refused as it always was.
    /// </summary>
    /// <param name="interpreterSearchPath">
    /// Where a shim's interpreter is looked for when npm placed none beside it. A caller looking a bare name up passes
    /// the same value as <paramref name="searchPath"/>; one looking in a single directory a person named passes the
    /// process's own search path, which is where the shim itself would find node.exe.
    /// </param>
    public static FoundProgram Find(string name, ProgramSearchPath searchPath, ProgramSearchPath interpreterSearchPath)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(searchPath);
        ArgumentNullException.ThrowIfNull(interpreterSearchPath);

        // The walk never leaves the entry it walks: a name that is a path would be joined to no entry at all, so it is
        // not found, before any directory is read.
        if (IsPath(name))
        {
            return new FoundProgram(name, ProgramLookupOutcome.NotFound, null, [], null, null);
        }

        // The first file that only a shell could start, which is what a refusal names when no .cmd turns up at all.
        string? script = null;

        // Collected as the walk goes, never tried inside it: a shim is the last resort rather than the first
        // hit, so a `.cmd` in an earlier directory must lose to an `.exe` in a later one. Reading one during
        // the walk would make installing npm's shim silently change which program a name already answered with.
        var shims = new List<string>();

        foreach (var directory in Directories(searchPath))
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var candidate in WindowsCandidates(name))
                {
                    var file = Path.Combine(directory, candidate);
                    if (File.Exists(file))
                    {
                        return new FoundProgram(name, ProgramLookupOutcome.Program, file, [], file, null);
                    }
                }

                foreach (var extension in RefusedExtensions(searchPath))
                {
                    var refused = Path.Combine(directory, name + extension);
                    if (!File.Exists(refused))
                    {
                        continue;
                    }

                    script ??= refused;
                    if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
                    {
                        shims.Add(refused);
                    }
                }
            }
            else
            {
                var file = Path.Combine(directory, name);
                if (File.Exists(file) && IsExecutable(file))
                {
                    return new FoundProgram(name, ProgramLookupOutcome.Program, file, [], file, null);
                }
            }
        }

        if (shims.Count > 0)
        {
            // The whole search path yielded neither an .exe nor a .com, so the first `.cmd` it did yield is
            // read — and only now.
            var shim = ReadNpmShim(shims[0], name, interpreterSearchPath, out var rule, out var pathRead, out var missing);
            return shim switch
            {
                // An executable the package ships starts by itself, with nothing in front of what a caller asks for.
                { Entry: null } => new FoundProgram(name, ProgramLookupOutcome.Program, shim.Program, [], shims[0], null),
                { Entry: { } entry } => new FoundProgram(name, ProgramLookupOutcome.NpmShim, shim.Program, [entry], shims[0], null, pathRead),
                null when missing => new FoundProgram(name, ProgramLookupOutcome.NotFound, null, [], shims[0], null),
                null => new FoundProgram(name, ProgramLookupOutcome.RefusedShim, null, [], shims[0], rule, pathRead),
            };
        }

        return script is not null
            ? new FoundProgram(name, ProgramLookupOutcome.ShellOnly, null, [], script, null)
            : new FoundProgram(name, ProgramLookupOutcome.NotFound, null, [], null, null);
    }

    // ---------------------------------------------------------------------------------------------------------
    // npm's cmd shim, and nothing else.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// What a shim resolved to: the program that is started, and the one argument it is handed first — the entry
    /// script, for the interpreter the shim names, or nothing, for an executable the package ships.
    /// </summary>
    private sealed record NpmShimTarget(string Program, string? Entry);

    /// <summary>A shim npm wrote is a few hundred bytes; anything larger is not the file this reader knows.</summary>
    public const int MaxShimBytes = 8 * 1024;

    /// <summary>A package manifest large enough for any real one, and small enough that reading it costs nothing.</summary>
    private const int MaxPackageJsonBytes = 1024 * 1024;

    /// <summary>
    /// The lines npm's current cmd-shim writes above the call. Older templates had none of them, and a file
    /// that is missing one is not the file this reader was written against.
    /// </summary>
    private static readonly string[] ShimMarkers =
    [
        ":find_dp0",
        "SET dp0=%~dp0",
        "IF EXIST \"%dp0%\\node.exe\"",
        "SET \"_prog=%dp0%\\node.exe\"",
    ];

    /// <summary>
    /// Every line cmd-shim writes above the call when the target has no shebang: an executable the package ships, which
    /// the shim starts with no interpreter. Nothing else stands in that file, so it is read only as exactly these, each
    /// compared once its trailing blanks are dropped and with the empty lines between them dropped too. That is loose
    /// enough for a file an editor touched and costs nothing, since the shim itself is never started.
    /// </summary>
    private static readonly string[] NativeShimHead =
    [
        "@ECHO off",
        "GOTO start",
        ":find_dp0",
        "SET dp0=%~dp0",
        "EXIT /b",
        ":start",
        "SETLOCAL",
        "CALL :find_dp0",
    ];

    /// <summary>
    /// npm's own shim, and nothing else. A <c>.cmd</c> is refused because starting it means <c>cmd.exe</c>
    /// parses the arguments; the way out is not to escape them but to start what the shim would have started.
    /// The file is read, held to npm's exact template, held inside the package directory it names, and
    /// cross-checked against that package's own <c>bin</c> entry — so "resolve a program out of a file" never
    /// becomes "run whatever the file says". cmd-shim writes two forms: one that hands an entry script to node,
    /// and, for a target with no shebang, one that starts the target itself, which counts only where it is a
    /// program image by its name. Nothing here is remembered: a file that decides what runs is read
    /// again at every lookup rather than trusted from last time. The answer is null and <paramref name="rule"/>
    /// names the rule that was broken, because a refusal an operator cannot act on is barely a refusal at all;
    /// <paramref name="missing"/> says instead that the executable a shim of the second form names is not there.
    /// </summary>
    private static NpmShimTarget? ReadNpmShim(
        string shim, string name, ProgramSearchPath interpreterSearchPath, out string rule, out bool pathRead, out bool missing)
    {
        rule = "is not the shim npm writes";
        pathRead = false;
        missing = false;
        var directory = Path.GetDirectoryName(shim);
        if (directory is null)
        {
            return null;
        }

        var text = ReadCapped(shim, MaxShimBytes);
        if (text is null)
        {
            return null;
        }

        var lines = text.Split('\n').Select(line => line.TrimEnd('\r', ' ', '\t')).Where(line => line.Length > 0).ToList();
        var last = lines.LastOrDefault() ?? string.Empty;
        Match call;
        bool native;
        if (ShimMarkers.All(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            call = NpmShimCall().Match(last);
            native = false;
        }
        else if (lines.Count == NativeShimHead.Length + 1 && lines.Take(NativeShimHead.Length).SequenceEqual(NativeShimHead, StringComparer.Ordinal))
        {
            call = NpmNativeShimCall().Match(last);
            native = true;
        }
        else
        {
            return null;
        }

        if (!call.Success)
        {
            return null;
        }

        // A scoped install is two directories under node_modules, `@scope` and the name, and an unscoped one is one;
        // the entry is what follows them.
        string? scope = null;
        var package = call.Groups["package"].Value;
        var rest = call.Groups["entry"].Value;
        if (package.StartsWith('@'))
        {
            if (!PackageName().IsMatch(package[1..]))
            {
                // The scope reads as a name before any path is built from it, for the reason the package does below;
                // a bare `@` is no scope at all.
                rule = "names a scope whose name is not one npm could have written";
                return null;
            }

            var separator = rest.IndexOf('\\', StringComparison.Ordinal);
            if (separator < 0)
            {
                rule = "names a scope and no package under it";
                return null;
            }

            scope = package;
            package = rest[..separator];
            rest = rest[(separator + 1)..];
        }

        if (!PackageName().IsMatch(package))
        {
            // A segment is a package name or it is nothing: `..` would climb out of node_modules and a drive
            // letter would leave the tree altogether, both while still reading as one segment. A scope under a
            // scope fails here too, since a name never begins with `@`.
            rule = "names a package whose name is not one npm could have written";
            return null;
        }

        string[] segments = scope is null ? [package] : [scope, package];
        var relative = NormalizeEntry(rest);
        var modules = Path.Combine(directory, "node_modules");
        var root = Path.Combine([modules, .. segments]);

        // The entry as the shim means it, and the package root as the disk really holds it. A junction at
        // node_modules, at the scope or at the package leaves the entry lexically inside while the file lives
        // anywhere at all, so the two sides are computed differently on purpose and then have to agree.
        var realRoot = RealDirectory(modules, segments);
        string entry;
        try
        {
            entry = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            rule = "names an entry that points outside the package";
            return null;
        }

        if (realRoot is null)
        {
            rule = "names a package that is not installed beside it";
            return null;
        }

        if (!Contains(realRoot, entry) || EntryUnder(realRoot, relative) is null)
        {
            rule = "names an entry that points outside the package";
            return null;
        }

        if (native && !IsProgramImage(relative))
        {
            // Asked only once the target is held inside the package, so a target that climbs or names a drive is refused
            // by the rule the other form's entry is. What the shim would start is then a file only a shell, or nothing,
            // could run; it never counts by its content.
            rule = "names a target that is not an .exe or a .com, and " + ProgramsNotShells;
            return null;
        }

        if (!File.Exists(entry))
        {
            // An executable that is not there is a program that is not there; an entry script that is not there is a
            // shim that broke a rule, since its program, node, may well be.
            missing = native;
            rule = "names an entry that is not there";
            return null;
        }

        var manifest = ReadCapped(Path.Combine(root, "package.json"), MaxPackageJsonBytes);
        if (manifest is null || !Declares(manifest, name, relative, scope))
        {
            rule = "names an entry its package does not declare";
            return null;
        }

        if (native)
        {
            // The executable itself starts, never the shim, so neither its text nor its `%*` reaches a child.
            rule = string.Empty;
            return new NpmShimTarget(entry, null);
        }

        var interpreter = FindInterpreter(directory, interpreterSearchPath, out pathRead);
        if (interpreter is null)
        {
            rule = "needs node.exe, which is neither beside it nor on the search path";
            return null;
        }

        rule = string.Empty;
        return new NpmShimTarget(interpreter, entry);
    }

    /// <summary>
    /// The shim's last line, exactly as cmd-shim writes it. The package is one segment — a scoped install
    /// therefore captures its scope here, and the name under the scope is taken from the entry above. No <c>%</c>
    /// and no <c>!</c> stands in the entry, in this form or the other: cmd.exe would expand a variable there before it
    /// started anything, a <c>!</c> one where delayed expansion is on, so the line would name a file other than the one
    /// it spells, and npm never writes one.
    /// </summary>
    [GeneratedRegex("""^endLocal & goto #_undefined_# 2>NUL \|\| title %COMSPEC% & "%_prog%" +"%dp0%\\node_modules\\(?<package>[^"\\/]+)\\(?<entry>[^"%!]+)" %\*\z""")]
    private static partial Regex NpmShimCall();

    /// <summary>
    /// The call of the form with no interpreter, exactly as cmd-shim writes it: the target, quoted, then <c>%*</c>, and
    /// nothing in front of the target.
    /// </summary>
    [GeneratedRegex("""^"%dp0%\\node_modules\\(?<package>[^"\\/]+)\\(?<entry>[^"%!]+)" +%\*\z""")]
    private static partial Regex NpmNativeShimCall();

    /// <summary>Whether a target is a file the search path itself counts as a program image, read by its name alone.</summary>
    private static bool IsProgramImage(string target) =>
        Path.GetExtension(target) is var extension
        && (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".com", StringComparison.OrdinalIgnoreCase));

    /// <summary>An npm package name as npm itself allows one, which is also everything a directory may be here.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,213}\z")]
    private static partial Regex PackageName();

    /// <summary>
    /// The package's own <c>bin</c>, in the two shapes npm publishes: a map from command name to script, and a
    /// bare string for a package whose single command is its own name, without its scope where it has one, so the
    /// package's own name is then the scope and the command. Anything else is a shape this reader does not know,
    /// which is a refusal rather than a guess.
    /// </summary>
    private static bool Declares(string manifest, string name, string entry, string? scope)
    {
        var packageNamed = scope is null ? name : scope + "/" + name;
        try
        {
            using var document = JsonDocument.Parse(manifest);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("bin", out var bin))
            {
                return false;
            }

            if (bin.ValueKind == JsonValueKind.Object)
            {
                return bin.TryGetProperty(name, out var declared)
                    && declared.ValueKind == JsonValueKind.String
                    && SameEntry(declared.GetString(), entry);
            }

            return bin.ValueKind == JsonValueKind.String
                && document.RootElement.TryGetProperty("name", out var packageName)
                && packageName.ValueKind == JsonValueKind.String
                && string.Equals(packageName.GetString(), packageNamed, StringComparison.Ordinal)
                && SameEntry(bin.GetString(), entry);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool SameEntry(string? declared, string entry) =>
        declared is not null && NormalizeEntry(declared).Equals(entry, StringComparison.OrdinalIgnoreCase);

    /// <summary>One spelling of the same relative path: npm writes <c>/</c> in a manifest and <c>\</c> in a shim.</summary>
    private static string NormalizeEntry(string value)
    {
        var normalized = value.Replace('/', '\\');
        while (normalized.StartsWith(@".\", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    /// <summary>
    /// The package's directory as the disk really holds it: <c>node_modules</c>, then each directory under it, every
    /// one followed to its end before the next is joined to it, so a link at any level is where the walk goes on from.
    /// Null where a directory on the way is not there.
    /// </summary>
    private static string? RealDirectory(string modules, IEnumerable<string> segments)
    {
        string? current = FinalDirectory(modules) ?? modules;
        foreach (var segment in segments)
        {
            current = FinalDirectory(Path.Combine(current, segment));
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>A directory as the disk really holds it, with every link on the way followed to its end.</summary>
    private static string? FinalDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            var target = Directory.ResolveLinkTarget(directory, returnFinalTarget: true);
            return Path.GetFullPath(target?.FullName ?? directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// The entry inside the package, with every link on the way from the package root followed to its end and
    /// every step held inside that root — null the moment one leaves it. Resolving the last component alone is
    /// not enough: a junction at any directory along the way leaves the path lexically inside the package while
    /// the file itself lives anywhere on the disk, and it is the file that runs.
    /// </summary>
    private static string? EntryUnder(string root, string relative)
    {
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
            {
                return null;
            }

            current = Path.Combine(current, segment);
            var final = Directory.Exists(current) ? FinalDirectory(current) : FinalFile(current);
            if (final is null || !Contains(root, final))
            {
                return null;
            }

            current = final;
        }

        return ReferenceEquals(current, root) ? null : current;
    }

    /// <summary>The same for a file, which may itself be a link to somewhere the package never reaches.</summary>
    private static string FinalFile(string file)
    {
        try
        {
            var target = File.ResolveLinkTarget(file, returnFinalTarget: true);
            return target is null ? file : Path.GetFullPath(target.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return file;
        }
    }

    private static bool Contains(string root, string file) =>
        file.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The interpreter the shim itself would have picked: the one beside it when npm placed one there, else
    /// the first on the interpreter's search path the caller passed. Always an absolute path, so what is
    /// recorded is the file that runs rather than a name something else could answer for later.
    /// </summary>
    private static string? FindInterpreter(string directory, ProgramSearchPath interpreterSearchPath, out bool pathRead)
    {
        pathRead = false;
        var beside = Path.Combine(directory, "node.exe");
        if (File.Exists(beside))
        {
            return Path.GetFullPath(beside);
        }

        pathRead = true;
        foreach (var candidate in Directories(interpreterSearchPath))
        {
            var file = Path.Combine(candidate, "node.exe");
            if (File.Exists(file))
            {
                return Path.GetFullPath(file);
            }
        }

        return null;
    }

    /// <summary>A file the lookup is willing to read, or null — too large, missing or unreadable are one answer.</summary>
    private static string? ReadCapped(string file, int maxBytes)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > maxBytes)
            {
                return null;
            }

            // Asked twice on purpose: a file may have grown between the two, and nothing this reader decides
            // may depend on how much of it somebody chose to write after its size was read.
            var text = File.ReadAllText(file);
            return text.Length > maxBytes ? null : text;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Directories(ProgramSearchPath searchPath)
    {
        foreach (var entry in (searchPath.Path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = entry.Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(directory);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            yield return full;
        }
    }

    private static IEnumerable<string> WindowsCandidates(string name)
    {
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".com", StringComparison.OrdinalIgnoreCase))
        {
            yield return name;
        }

        yield return name + ".exe";
        yield return name + ".com";
    }

    /// <summary>
    /// What a bare name could otherwise resolve to on this machine, minus the two forms that can be started
    /// without a shell. The search path says which extensions exist here, so the refusal is honest about it.
    /// </summary>
    private static IEnumerable<string> RefusedExtensions(ProgramSearchPath searchPath) =>
        (searchPath.PathExt ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static extension =>
                !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".com", StringComparison.OrdinalIgnoreCase))
            .Concat(WindowsRefusedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsExecutable(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            const UnixFileMode anyExecuteBit = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(file) & anyExecuteBit) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
