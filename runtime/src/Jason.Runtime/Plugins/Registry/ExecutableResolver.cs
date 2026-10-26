using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Contracts.Programs;
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

    /// <summary>
    /// The declared name as <see cref="ProgramLookup"/> answers for it on the runtime's own search path, worded as a
    /// plugin's problem where it answers nothing that may be started. The walk, the Windows order, the refused
    /// extensions and the reading of npm's shim are that lookup's, and the same for every program Jason starts by
    /// name; what is this resolver's is the problem a plugin is held back by and the version check below.
    /// </summary>
    public ResolvedExecutable Resolve(ExecutableRequest request, int index)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = ProblemPath(index);

        var onPath = new ProgramSearchPath(searchPath.Path, searchPath.PathExt);
        var found = ProgramLookup.Find(request.Name, onPath, onPath);
        if (found is { Outcome: ProgramLookupOutcome.Program or ProgramLookupOutcome.NpmShim, Path: { } program })
        {
            return new ResolvedExecutable(request.Name, program, found.Launch, null, request.MinVersion, null);
        }

        var problem = found.Outcome switch
        {
            ProgramLookupOutcome.RefusedShim => new ManifestProblem(
                ProblemCodes.ExecutableNotRunnable,
                path,
                $"'{request.Name}' resolves only to '{Path.GetFileName(found.File)}', which {found.Refusal}. Starting it would mean cmd.exe interprets the arguments, which a plugin may never do."),
            ProgramLookupOutcome.ShellOnly => new ManifestProblem(
                ProblemCodes.ExecutableNotRunnable,
                path,
                $"'{request.Name}' resolves only to a script a shell would have to interpret; a plugin may start programs, never shells."),
            _ => new ManifestProblem(
                ProblemCodes.ExecutableMissing,
                path,
                $"'{request.Name}' was not found on the runtime's search path."),
        };
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
        // A version check is a system program's answer, read in the console's code page as it is written.
        var start = ChildProcess.StartInfo(resolved.Path, ChildKind.SystemToolWithInput);
        start.WorkingDirectory = home;

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
            ChildProcess.Start(process);
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
        return ": " + (said.Length <= SaidChars ? said : "..." + said[^SaidChars..]);
    }

    private static string ProblemPath(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"capabilities.exec.executables[{index}].name");

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
