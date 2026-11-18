using System.Runtime.InteropServices;

namespace Jason.Cli.Installation;

/// <summary>
/// Whether the application runs from a place that goes away, where <c>jason install</c> sets nothing up: a copy macOS
/// runs translocated, from a temporary place of its own because it was opened where it was downloaded, or a volume that
/// is read-only, as a mounted disk image is. A terminal command, a logon start or a retirement pointed there would point
/// at nothing once the image is ejected or the application quits.
/// </summary>
/// <remarks>
/// <para>
/// <b>The look writes nothing.</b> The translocation mark is read from the path, and the volume is asked through
/// <c>access(2)</c>, which answers <c>EROFS</c> for a write on a read-only file system ahead of any permission. A probe
/// file inside the application would break its seal, so there is no "try to create a file" fallback. Only a sure answer
/// refuses: a call that fails, or answers anything but <c>EROFS</c>, refuses nothing. A second disk a person keeps is
/// writable and is never refused by its path's spelling.
/// </para>
/// <para>
/// <b>macOS only.</b> Windows is not gated: nothing publishes a build of Jason Desktop for Windows that can sit on
/// read-only media, and a native call on the Windows install path would be one more way to refuse a real person wrongly.
/// If a portable build ever exists, <see cref="SetupPlaces.ReadOnlyVolume"/> is where its look goes.
/// </para>
/// </remarks>
internal static class VanishingPlace
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    /// <summary><c>W_OK</c> in macOS's <c>unistd.h</c>.</summary>
    private const int WriteOk = 2;

    /// <summary><c>EROFS</c> in macOS's <c>errno.h</c>: a read-only file system.</summary>
    private const int ReadOnlyFileSystem = 30;

    /// <summary>The folder name macOS runs a translocated application under.</summary>
    private const string TranslocationMark = "AppTranslocation";

    /// <summary>
    /// The refusal's sentence where the carried jason runs from a place that goes away, or null where it does not or the
    /// system is not macOS.
    /// </summary>
    public static string? Refusal(CarriedExecutable carried, SetupPlaces places)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(places);
        if (places.Platform is not SetupSystem.MacOs)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(carried.Executable)!;
        var translocated = carried.Executable.Split('/', '\\').Contains(TranslocationMark, StringComparer.Ordinal);
        if (!translocated && !places.ReadOnlyVolume(folder))
        {
            return null;
        }

        return $"This jason, {carried.Executable}, runs from a place that goes away: "
            + (translocated
                ? "a copy macOS runs from a temporary place of its own, because Jason was opened where it was downloaded."
                : "a read-only volume, such as the disk image Jason came in.")
            + " Nothing was placed, retired or stopped: the terminal's jason, the skills and any command-line install are as they were. "
            + "Move Jason to Applications, or to the Applications folder in your home (~/Applications), and open it from there: it sets the machine up then.";
    }

    /// <summary>
    /// Whether <paramref name="folder"/> is on a read-only volume, as <c>access(2)</c> answers a write: on macOS only, and
    /// true only for <c>EROFS</c>. It writes nothing. Everywhere else, and wherever the call fails, false.
    /// </summary>
    public static bool OnReadOnlyVolume(string folder)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            return Access(folder, WriteOk) != 0 && Marshal.GetLastPInvokeError() == ReadOnlyFileSystem;
        }
        catch (Exception unanswered) when (unanswered is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport(LibSystem, EntryPoint = "access", SetLastError = true)]
    private static extern int Access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
}
