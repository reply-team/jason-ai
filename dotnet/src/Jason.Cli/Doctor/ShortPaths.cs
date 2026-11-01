using System.Runtime.InteropServices;

namespace Jason.Cli.Doctor;

/// <summary>
/// The two spellings Windows keeps for a folder whose name is long: the long one a program usually shows, and the short
/// one — <c>C:\Users\ADALOV~1</c> for <c>C:\Users\Ada Lovelace</c> — that a path reads like where it was never spelled out
/// long: the raw value of a temporary-directory variable, a command line as it was launched, another program's output.
/// Elsewhere there is one spelling.
/// </summary>
/// <remarks>
/// Asked of the operating system rather than worked out here, because which number follows the <c>~</c> depends on what
/// else is in the folder, and only for a directory that exists, which is the only kind the system keeps a form of. The
/// other way, from short to long, needs no call of its own: the full path of a path with a <c>~</c> in it is already the
/// long one.
/// </remarks>
internal static class ShortPaths
{
    /// <summary>The short form of an existing directory, or null where there is none other than the path itself, or no answer.</summary>
    public static string? Of(string directory)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(directory))
        {
            return null;
        }

        // The call answers with the length a buffer needs when the one handed in is too small.
        var needed = GetShortPathNameW(directory, null, 0);
        if (needed == 0)
        {
            return null;
        }

        var buffer = new char[needed];
        var written = GetShortPathNameW(directory, buffer, needed);
        if (written == 0 || written >= needed)
        {
            return null;
        }

        var answer = new string(buffer, 0, (int)written);
        return string.Equals(answer, directory, StringComparison.OrdinalIgnoreCase) ? null : answer;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, [Out] char[]? shortPath, uint length);
}
