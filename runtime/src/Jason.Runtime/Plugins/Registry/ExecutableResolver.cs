using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// A declared executable as this machine answers for it. A problem here is always environmental: it holds its
/// own plugin back and never invalidates a package.
/// </summary>
/// <param name="Path">The program that is actually started, so a reader who knows only this keeps telling the truth.</param>
/// <param name="Launch">
/// What goes on the command line before anything the plugin asks for: empty for an ordinary program, the entry
/// script for a resolved npm shim, where the program is the interpreter that runs it.
/// </param>
public sealed record ResolvedExecutable(
    string Name, string? Path, IReadOnlyList<string> Launch, string? Version, string? MinVersion, ManifestProblem? Problem);

/// <summary>
/// Turns a declared program name into a path, and — only where the user granted it — asks the program for its
/// version. Two steps on purpose: presence is a fact about the machine that costs nothing to establish, while
/// starting a program is an act the user has to have consented to.
/// </summary>
public sealed partial class ExecutableResolver(ISearchPath searchPath)
{
    /// <summary>Enough of an answer to find a version in; a program that says more than this is not asked again.</summary>
    public const int MaxVersionOutputChars = 64 * 1024;

    private static readonly string[] WindowsRefusedExtensions = [".cmd", ".bat", ".ps1"];

    /// <summary>
    /// Walks the search path in order. On Windows only <c>.exe</c> and <c>.com</c> are accepted: starting a
    /// <c>.cmd</c> or a <c>.bat</c> means <c>cmd.exe</c> interprets the arguments, which is the shell the
    /// argument-array invariant exists to exclude.
    /// </summary>
    public ResolvedExecutable Resolve(ExecutableRequest request, int index)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = ProblemPath(index);
        var shellOnly = false;

        // Collected as the walk goes, never tried inside it: a shim is the last resort rather than the first
        // hit, so a `.cmd` in an earlier directory must lose to an `.exe` in a later one. Reading one during
        // the walk would make installing npm's shim silently change which program a name already answered with.
        var shims = new List<string>();

        foreach (var directory in Directories())
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var candidate in WindowsCandidates(request.Name))
                {
                    var file = Path.Combine(directory, candidate);
                    if (File.Exists(file))
                    {
                        return new ResolvedExecutable(request.Name, file, [], null, request.MinVersion, null);
                    }
                }

                foreach (var extension in RefusedExtensions())
                {
                    var refused = Path.Combine(directory, request.Name + extension);
                    if (!File.Exists(refused))
                    {
                        continue;
                    }

                    shellOnly = true;
                    if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
                    {
                        shims.Add(refused);
                    }
                }
            }
            else
            {
                var file = Path.Combine(directory, request.Name);
                if (File.Exists(file) && IsExecutable(file))
                {
                    return new ResolvedExecutable(request.Name, file, [], null, request.MinVersion, null);
                }
            }
        }

        if (shims.Count > 0)
        {
            // The whole search path yielded neither an .exe nor a .com, so the first `.cmd` it did yield is
            // read — and only now.
            var shim = ReadNpmShim(shims[0], request.Name, out var rule);
            if (shim is not null)
            {
                return new ResolvedExecutable(request.Name, shim.Interpreter, [shim.Entry], null, request.MinVersion, null);
            }

            return new ResolvedExecutable(request.Name, null, [], null, request.MinVersion, new ManifestProblem(
                ProblemCodes.ExecutableNotRunnable,
                path,
                $"'{request.Name}' resolves only to '{Path.GetFileName(shims[0])}', which {rule}. Starting it would mean cmd.exe interprets the arguments, which a plugin may never do."));
        }

        var problem = shellOnly
            ? new ManifestProblem(
                ProblemCodes.ExecutableNotRunnable,
                path,
                $"'{request.Name}' resolves only to a script a shell would have to interpret; a plugin may start programs, never shells.")
            : new ManifestProblem(
                ProblemCodes.ExecutableMissing,
                path,
                $"'{request.Name}' was not found on the runtime's search path.");
        return new ResolvedExecutable(request.Name, null, [], null, request.MinVersion, problem);
    }

    /// <summary>
    /// Runs the declared version command with the environment it is handed and nothing else, and reads the first
    /// version-looking token out of the answer. Called only for an executable the user granted.
    /// </summary>
    public async Task<ResolvedExecutable> CheckVersionAsync(
        ResolvedExecutable resolved,
        ExecutableRequest request,
        int index,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Redactor? redactor = null)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(environment);

        if (resolved.Path is null || request.VersionCommand is null || request.VersionCommand.Count == 0)
        {
            return resolved;
        }

        var path = ProblemPath(index);

        // Where the thing being checked actually lives. With an interpreter the resolved path is node's own
        // directory, which tells a vendor CLI nothing about where its package is.
        var home = resolved.Launch.Count > 0
            ? Path.GetDirectoryName(resolved.Launch[0])!
            : Path.GetDirectoryName(resolved.Path)!;
        var start = new ProcessStartInfo
        {
            FileName = resolved.Path,
            WorkingDirectory = home,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // The pair the runtime resolved, then the version command: what is checked has to be what will be
        // started, or the check is about a different program than the one a plugin will reach.
        foreach (var argument in resolved.Launch)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var argument in request.VersionCommand)
        {
            start.ArgumentList.Add(argument);
        }

        // Built from nothing rather than inherited: a version check runs with exactly the environment an
        // invocation would run with, so what it proves is what will happen later.
        start.Environment.Clear();
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return Failed(resolved, path, $"'{resolved.Name}' could not be started: {exception.Message}");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        string output;
        string error;
        try
        {
            process.StandardInput.Close();
            var reading = Task.WhenAll(
                ReadCappedAsync(process.StandardOutput, deadline.Token),
                ReadCappedAsync(process.StandardError, deadline.Token));
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var captured = await reading.ConfigureAwait(false);
            output = captured[0];
            error = captured[1];
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(resolved, path, $"'{resolved.Name}' did not answer its version command in time.");
        }

        if (process.ExitCode != 0)
        {
            // The exit code alone explains nothing; what the program said on its way out is what a person acts on.
            return Failed(
                resolved,
                path,
                string.Create(CultureInfo.InvariantCulture, $"'{resolved.Name}' answered its version command with exit code {process.ExitCode}{Said(error, output, redactor)}."));
        }

        var version = FirstVersion(output) ?? FirstVersion(error);
        if (version is null)
        {
            return Failed(resolved, path, $"'{resolved.Name}' answered its version command without a version number.");
        }

        if (request.MinVersion is not null
            && Version.TryParse(version, out var reported)
            && Version.TryParse(request.MinVersion, out var required)
            && reported < required)
        {
            return resolved with
            {
                Version = version,
                Problem = new ManifestProblem(
                    ProblemCodes.ExecutableIncompatible,
                    path,
                    $"'{resolved.Name}' reports {version}; the manifest asks for {request.MinVersion} or later."),
            };
        }

        return resolved with { Version = version };
    }

    private static ResolvedExecutable Failed(ResolvedExecutable resolved, string path, string message) =>
        resolved with { Problem = new ManifestProblem(ProblemCodes.ExecutableVersionCheckFailed, path, message) };

    // ---------------------------------------------------------------------------------------------------------
    // npm's cmd shim, and nothing else.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>What a shim resolved to: the program that is started, and the one argument it is handed first.</summary>
    private sealed record NpmShimTarget(string Interpreter, string Entry);

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
    /// npm's own shim, and nothing else. A <c>.cmd</c> is refused because starting it means <c>cmd.exe</c>
    /// parses the arguments; the way out is not to escape them but to start what the shim would have started.
    /// The file is read, held to npm's exact template, held inside the package directory it names, and
    /// cross-checked against that package's own <c>bin</c> entry — so "resolve a program out of a file" never
    /// becomes "run whatever the file says". Nothing here is remembered: a file that decides what runs is read
    /// again at every reload rather than trusted from last time. The answer is null and <paramref name="rule"/>
    /// names the rule that was broken, because a refusal an operator cannot act on is barely a refusal at all.
    /// </summary>
    private NpmShimTarget? ReadNpmShim(string shim, string name, out string rule)
    {
        rule = "is not the shim npm writes";
        var directory = Path.GetDirectoryName(shim);
        if (directory is null)
        {
            return null;
        }

        var text = ReadCapped(shim, MaxShimBytes);
        if (text is null || !ShimMarkers.All(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return null;
        }

        var lines = text.Split('\n');
        var last = lines.Select(line => line.TrimEnd('\r', ' ', '\t')).LastOrDefault(line => line.Length > 0) ?? string.Empty;
        var call = NpmShimCall().Match(last);
        if (!call.Success)
        {
            return null;
        }

        var package = call.Groups["package"].Value;
        if (package.StartsWith('@'))
        {
            // A scoped install is two directories under node_modules and this reader knows one. Saying which
            // rule that is beats leaving the author of a scoped CLI to guess.
            rule = "names a scoped package, and only a package one directory under node_modules is read";
            return null;
        }

        if (!PackageName().IsMatch(package))
        {
            // A segment is a package name or it is nothing: `..` would climb out of node_modules and a drive
            // letter would leave the tree altogether, both while still reading as one segment.
            rule = "names a package whose name is not one npm could have written";
            return null;
        }

        var relative = NormalizeEntry(call.Groups["entry"].Value);
        var modules = Path.Combine(directory, "node_modules");
        var root = Path.Combine(modules, package);

        // The entry as the shim means it, and the package root as the disk really holds it. A junction at
        // node_modules or at the package leaves the entry lexically inside while the file lives anywhere at
        // all, so the two sides are computed differently on purpose and then have to agree.
        var realRoot = FinalDirectory(Path.Combine(FinalDirectory(modules) ?? modules, package));
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

        if (!File.Exists(entry))
        {
            rule = "names an entry that is not there";
            return null;
        }

        var manifest = ReadCapped(Path.Combine(root, "package.json"), MaxPackageJsonBytes);
        if (manifest is null || !Declares(manifest, name, relative))
        {
            rule = "names an entry its package does not declare";
            return null;
        }

        var interpreter = FindInterpreter(directory);
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
    /// therefore captures its scope here and is refused by name above rather than by accident.
    /// </summary>
    [GeneratedRegex("""^endLocal & goto #_undefined_# 2>NUL \|\| title %COMSPEC% & "%_prog%" +"%dp0%\\node_modules\\(?<package>[^"\\/]+)\\(?<entry>[^"]+)" %\*$""")]
    private static partial Regex NpmShimCall();

    /// <summary>An npm package name as npm itself allows one, which is also everything a directory may be here.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,213}$")]
    private static partial Regex PackageName();

    /// <summary>
    /// The package's own <c>bin</c>, in the two shapes npm publishes: a map from command name to script, and a
    /// bare string for a package whose single command is its own name. Anything else is a shape this reader
    /// does not know, which is a refusal rather than a guess.
    /// </summary>
    private static bool Declares(string manifest, string name, string entry)
    {
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
                && string.Equals(packageName.GetString(), name, StringComparison.Ordinal)
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
    /// the first on the same search path the declared name was looked for on. Always an absolute path, so what
    /// is recorded is the file that runs rather than a name something else could answer for later.
    /// </summary>
    private string? FindInterpreter(string directory)
    {
        var beside = Path.Combine(directory, "node.exe");
        if (File.Exists(beside))
        {
            return Path.GetFullPath(beside);
        }

        foreach (var candidate in Directories())
        {
            var file = Path.Combine(candidate, "node.exe");
            if (File.Exists(file))
            {
                return Path.GetFullPath(file);
            }
        }

        return null;
    }

    /// <summary>A file the runtime is willing to read, or null — too large, missing or unreadable are one answer.</summary>
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
            // may depend on how much of it somebody chose to write after it was measured.
            var text = File.ReadAllText(file);
            return text.Length > maxBytes ? null : text;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>How much of a failing program's output travels in the problem: enough to name the cause.</summary>
    public const int SaidChars = 200;

    /// <summary>
    /// The tail of what the program printed — stderr first, stdout when stderr is silent — on one line and
    /// through the redactor, so a value the plugin was granted cannot end up in a listing.
    /// </summary>
    private static string Said(string error, string output, Redactor? redactor)
    {
        var said = string.Join(' ', (string.IsNullOrWhiteSpace(error) ? output : error).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (said.Length == 0)
        {
            return string.Empty;
        }

        said = (redactor ?? Redactor.None).Redact(said);
        return ": " + (said.Length <= SaidChars ? said : "…" + said[^SaidChars..]);
    }

    private static string ProblemPath(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"capabilities.exec.executables[{index}].name");

    private IEnumerable<string> Directories()
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
    private IEnumerable<string> RefusedExtensions() =>
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

    private static async Task<string> ReadCappedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var captured = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            var room = MaxVersionOutputChars - captured.Length;
            if (room > 0)
            {
                captured.Append(buffer, 0, Math.Min(read, room));
            }
        }

        return captured.ToString();
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
        }
    }

    private static string? FirstVersion(string text)
    {
        var match = VersionToken().Match(text);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionToken();
}
