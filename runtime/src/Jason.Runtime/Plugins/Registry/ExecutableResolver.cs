using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// A declared executable as this machine answers for it. A problem here is always environmental: it holds its
/// own plugin back and never invalidates a package.
/// </summary>
public sealed record ResolvedExecutable(string Name, string? Path, string? Version, string? MinVersion, ManifestProblem? Problem);

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

        foreach (var directory in Directories())
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var candidate in WindowsCandidates(request.Name))
                {
                    var file = Path.Combine(directory, candidate);
                    if (File.Exists(file))
                    {
                        return new ResolvedExecutable(request.Name, file, null, request.MinVersion, null);
                    }
                }

                shellOnly |= RefusedExtensions().Any(extension => File.Exists(Path.Combine(directory, request.Name + extension)));
            }
            else
            {
                var file = Path.Combine(directory, request.Name);
                if (File.Exists(file) && IsExecutable(file))
                {
                    return new ResolvedExecutable(request.Name, file, null, request.MinVersion, null);
                }
            }
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
        return new ResolvedExecutable(request.Name, null, null, request.MinVersion, problem);
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
        var start = new ProcessStartInfo
        {
            FileName = resolved.Path,
            WorkingDirectory = Path.GetDirectoryName(resolved.Path)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

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
