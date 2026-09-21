using System.Diagnostics;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Skills;

/// <summary>
/// Putting one skill's directory in place, all of it or none of it.
/// </summary>
/// <remarks>
/// <para>
/// The launcher reads a role's tree and then copies it file by file. A deployment that wrote into that tree
/// while it was being read would hand the child a mix of two trees and record a size matching neither, against
/// the whole discipline that a host reads exactly the bytes it was promised. So a skill is built beside its
/// destination and moved into place.
/// </para>
/// <para>
/// There is no atomic directory replace on either platform — POSIX <c>rename(2)</c> replaces only an empty
/// directory, and Windows answers <c>[WinError 183]</c> — so replacing one is two renames, and the order
/// matters. <b>The live tree goes aside first</b>, because that is the one a reader can refuse: on Windows
/// <c>Directory.Move</c> is denied while a file inside is open, and denied <em>cleanly</em>, leaving the source
/// where it was and creating nothing. A failure there has therefore changed nothing. The second rename targets
/// a path nothing can hold open, so it cannot be blocked.
/// </para>
/// <para>
/// A first deployment is one rename and has no window at all; an unchanged one does not rename.
/// </para>
/// </remarks>
public static class SkillsSwap
{
    /// <summary>How long the fallible rename is retried before the deployment gives up on that skill.</summary>
    public static TimeSpan ReplaceTimeout { get; } = TimeSpan.FromSeconds(10);

    private static TimeSpan Pause => TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Where a deployment assembles a tree before moving it into <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// For the runtime's own role root this is a sibling directory the runtime never enumerates, because every
    /// directory under the role root is reported as a deployed role — staging inside it would be reported as
    /// one, by the operation an operator asks whether this installation is ready. For a person's harness root
    /// nothing enumerates it that way, so staging goes inside it, which is what keeps both renames on one
    /// volume and therefore metadata operations rather than copies.
    /// </remarks>
    public static string StagingFor(JasonPaths paths, string root)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        return string.Equals(Path.GetFullPath(root), Path.GetFullPath(paths.RoleSkillsDirectory), StringComparison.OrdinalIgnoreCase)
            ? paths.SkillsStagingDirectory
            : Path.Combine(root, ".jason-staging");
    }

    /// <summary>
    /// Moves <paramref name="staged"/> onto <paramref name="live"/>, or leaves everything as it was and says
    /// why. The message names the cause rather than a path: "access is denied" on a directory nobody is
    /// writing to is not something an operator can act on.
    /// </summary>
    public static string? Replace(string staged, string live, string asideRoot, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staged);
        ArgumentException.ThrowIfNullOrWhiteSpace(live);
        ArgumentNullException.ThrowIfNull(clock);

        var name = Path.GetFileName(live);
        Directory.CreateDirectory(Path.GetDirectoryName(live)!);

        if (!Directory.Exists(live))
        {
            // One rename, and no window at all.
            Directory.Move(staged, live);
            return null;
        }

        var aside = Path.Combine(asideRoot, $"{name}.replaced-{Guid.NewGuid():N}");
        Directory.CreateDirectory(asideRoot);

        if (!TryMoveAside(live, aside, clock, out var refusal))
        {
            return $"'{name}' could not be replaced: {refusal} Nothing was changed for it. Stop the runtime, or try again.";
        }

        Directory.Move(staged, live);
        Remove(aside);
        return null;
    }

    private static bool TryMoveAside(string live, string aside, TimeProvider clock, out string refusal)
    {
        var started = clock.GetTimestamp();
        refusal = string.Empty;

        while (true)
        {
            try
            {
                Directory.Move(live, aside);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Windows denies this while a launch holds a file inside the tree open, which is exactly what
                // teaching a role does. It denies it cleanly, so nothing has moved and waiting is safe.
                refusal = "a launch is reading this role's skill right now, and it has held it for "
                    + $"{ReplaceTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} seconds.";

                if (clock.GetElapsedTime(started) >= ReplaceTimeout)
                {
                    return false;
                }

                Thread.Sleep(Pause);
            }
        }
    }

    private static void Remove(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A launch that was reading the tree still holds it. It is out of the way, which is what mattered;
            // the next deployment collects it.
            Debug.WriteLine($"The replaced tree at '{directory}' is still held and was left for the next deployment.");
        }
    }

    /// <summary>Everything a previous deployment set aside and could not remove at the time.</summary>
    public static void Collect(string asideRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asideRoot);

        if (!Directory.Exists(asideRoot))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(asideRoot))
        {
            if (Path.GetFileName(directory).Contains(".replaced-", StringComparison.Ordinal))
            {
                Remove(directory);
            }
        }
    }
}
