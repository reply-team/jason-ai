using Jason.Cli.Skills;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// The two renames a replacement is, and what each of them does when it cannot go through.
/// </summary>
public class SkillsSwapTests
{
    /// <summary>A first deployment is one rename, and has no window at all.</summary>
    [Fact]
    public void A_skill_that_is_not_there_yet_is_moved_into_place_in_one_rename()
    {
        using var tree = new TempPaths();
        var staged = Compose(tree, "staging/researcher", "NEW");
        var live = Path.Combine(tree.Paths.Root, "roles", "researcher");

        Assert.Null(SkillsSwap.Replace(staged, live, Path.Combine(tree.Paths.Root, "staging"), TimeProvider.System));

        Assert.Contains("NEW", File.ReadAllText(Path.Combine(live, "SKILL.md")), StringComparison.Ordinal);
        Assert.False(Directory.Exists(staged));
    }

    /// <summary>And a replacement puts the new tree in and takes the old one away.</summary>
    [Fact]
    public void A_skill_that_is_there_is_replaced_and_the_tree_it_replaced_is_taken_away()
    {
        using var tree = new TempPaths();
        var staging = Path.Combine(tree.Paths.Root, "staging");
        var live = Compose(tree, "roles/researcher", "OLD");
        var staged = Compose(tree, "staging/researcher", "NEW");

        Assert.Null(SkillsSwap.Replace(staged, live, staging, TimeProvider.System));

        Assert.Contains("NEW", File.ReadAllText(Path.Combine(live, "SKILL.md")), StringComparison.Ordinal);
        Assert.DoesNotContain(Directory.GetDirectories(staging), directory => directory.Contains(".replaced-", StringComparison.Ordinal));
    }

    /// <summary>
    /// The second rename blocked. By then the live tree has already gone aside, so leaving it there is the
    /// worst outcome this file can produce: the role is neither old nor new, every launch of it runs untaught,
    /// and the next deployment's collection removes the only copy of what was there. The tree that was there
    /// is put back instead, and the verb says so.
    /// </summary>
    /// <remarks>
    /// Windows only, because holding a file open is what denies a directory rename and only Windows denies
    /// it. Elsewhere the rename goes through and there is no failure to recover from.
    /// </remarks>
    [Fact]
    public void A_blocked_second_rename_puts_back_the_tree_that_was_there()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var tree = new TempPaths();
        var staging = Path.Combine(tree.Paths.Root, "staging");
        var live = Compose(tree, "roles/researcher", "OLD");
        var staged = Compose(tree, "staging/researcher", "NEW");

        // Something holding the staged tree open: an indexer, a scanner, anything that walks a directory
        // somebody has just finished writing.
        using var held = File.Open(Path.Combine(staged, "SKILL.md"), FileMode.Open, FileAccess.Read, FileShare.Read);

        var refusal = SkillsSwap.Replace(staged, live, staging, Brief());

        Assert.NotNull(refusal);
        Assert.Contains("has been put back", refusal, StringComparison.Ordinal);
        Assert.True(Directory.Exists(live), "The role directory is neither old nor new: every launch of it now runs untaught.");
        Assert.Contains("OLD", File.ReadAllText(Path.Combine(live, "SKILL.md")), StringComparison.Ordinal);
    }

    /// <summary>
    /// A clock that reports the bound as already spent, so this test is about what happens when the retry
    /// gives up rather than about waiting ten seconds for it to.
    /// </summary>
    /// <remarks>
    /// <c>GetElapsedTime</c> is not virtual; it is computed from the timestamp and the frequency. So the
    /// timestamps this returns are a bound apart, which is the same thing said in the units the base class
    /// actually reads.
    /// </remarks>
    private static TimeProvider Brief() => new Expired();

    private sealed class Expired : TimeProvider
    {
        private long _reads;

        /// <summary>
        /// Every read a whole bound past the one before it, so any two reads are an expired bound apart.
        /// </summary>
        /// <remarks>
        /// Counting reads and answering "zero, then far away" was tried first and hung: a replacement calls
        /// this twice, so the second call's own first read was already the far one and every read after it
        /// was the same value — an elapsed time of zero, retried forever.
        /// </remarks>
        public override long GetTimestamp() =>
            Interlocked.Increment(ref _reads) * ((long)(SkillsSwap.ReplaceTimeout.TotalSeconds * TimestampFrequency) + 1);
    }

    private static string Compose(TempPaths tree, string relative, string marker)
    {
        var directory = Directory.CreateDirectory(Path.Combine(tree.Paths.Root, relative.Replace('/', Path.DirectorySeparatorChar))).FullName;
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: researcher\ndescription: one line\n---\n\n{marker}\n");
        return directory;
    }
}
