namespace Jason.Cli.Status;

/// <summary>
/// Which file a bare command name resolves to on this machine, worked out the way a shell works it out.
/// </summary>
/// <remarks>
/// Reading PATH rather than starting anything. "Is <c>jason</c> reachable by name?" is a question about a
/// directory listing, and answering it by running the program would be a probe — which is the line between a
/// readiness check and a diagnostics tool.
/// </remarks>
internal static class Executables
{
    public static string? OnPath(string command, string? searchPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var path = searchPath ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in Candidates(command))
            {
                string file;
                try
                {
                    file = Path.Combine(directory, candidate);
                }
                catch (ArgumentException)
                {
                    // A PATH entry with characters no path may hold. A shell skips it and so does this.
                    break;
                }

                if (File.Exists(file))
                {
                    return file;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The names to look for. On Windows a bare command is tried against the extensions PATHEXT lists, which
    /// is why <c>jason</c> finds <c>jason.exe</c>; everywhere else the name is the name.
    /// </summary>
    private static IEnumerable<string> Candidates(string command)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield return command;
            yield break;
        }

        if (Path.HasExtension(command))
        {
            yield return command;
            yield break;
        }

        var extensions = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        foreach (var extension in extensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return command + extension;
        }
    }
}
