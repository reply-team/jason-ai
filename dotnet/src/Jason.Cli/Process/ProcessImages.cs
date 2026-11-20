namespace Jason.Cli.Process;

/// <summary>The processes that may run a program file, and the path each was started from, as the system reports them.</summary>
public static class ProcessImages
{
    /// <summary>How many characters of a process's name Linux keeps.</summary>
    private const int Kept = 15;

    /// <summary>
    /// The processes that may run <paramref name="executable"/>, its image deciding afterwards. Elsewhere than Linux, those of
    /// the file's name without its extension, as .NET names a process. Linux keeps fifteen characters of a process's name, and
    /// .NET reads the rest of a longer one from the process's first argument, which Chromium rewrites into one string with its
    /// switches (<c>/opt/Jason/jason-desktop-app --</c>); and a process's name there keeps the file's extension, as an AppImage
    /// named <c>Jason.AppImage</c> runs as <c>Jason.AppImage</c>. So on Linux a process may be one where its name is the file's
    /// name with or without its extension, or, for a longer one, begins with the characters the system keeps of it.
    /// </summary>
    public static System.Diagnostics.Process[] Named(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var stem = Path.GetFileNameWithoutExtension(executable);
        if (!OperatingSystem.IsLinux())
        {
            return System.Diagnostics.Process.GetProcessesByName(stem);
        }

        string[] names = [stem, Path.GetFileName(executable)];
        var chosen = new List<System.Diagnostics.Process>();
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            if (SafeName(process) is { } named && names.Any(name => name.Length > Kept ? named.StartsWith(name[..Kept], StringComparison.Ordinal) : named == name))
            {
                chosen.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return [.. chosen];
    }

    /// <summary>
    /// The path a process's image was started from. Where the file was replaced or removed under it, as apt replaces a
    /// package's files under a running application, Linux reports the image as <c>&lt;path&gt; (deleted)</c>; the process
    /// still runs that installation's program, so it is read at the path it names.
    /// </summary>
    public static string AtItsPath(string image)
    {
        ArgumentNullException.ThrowIfNull(image);
        const string Gone = " (deleted)";
        return OperatingSystem.IsLinux() && image.EndsWith(Gone, StringComparison.Ordinal) && !File.Exists(image) ? image[..^Gone.Length] : image;
    }

    /// <summary>A process's name, or null where it is gone already.</summary>
    public static string? SafeName(System.Diagnostics.Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            return process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
