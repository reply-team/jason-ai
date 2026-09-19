using Jason.Contracts.Update;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The page that tells an operator what a release publishes, what the runtime sends to find out about one, and
/// which settings decide it. Every number and name on it is printed from the code it describes, because a page
/// that drifts from the runtime is worse than no page: it is believed.
/// </summary>
public class ReleaseAndUpdateDocTests
{
    [Fact]
    public void The_page_prints_the_settings_this_build_really_has()
    {
        var page = Read();
        var defaults = new UpdateOptions();

        Assert.Contains("Update:CheckEnabled", page, StringComparison.Ordinal);
        Assert.Contains("Update:FeedUrl", page, StringComparison.Ordinal);
        Assert.Contains("Update:InitialDelayMinutes", page, StringComparison.Ordinal);
        Assert.Contains("Update:IntervalHours", page, StringComparison.Ordinal);

        Assert.Contains($"| `{defaults.InitialDelayMinutes}` |", page, StringComparison.Ordinal);
        Assert.Contains($"| `{defaults.IntervalHours}` |", page, StringComparison.Ordinal);
        Assert.Contains(defaults.FeedUrl, page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bounds as the validator enforces them. An operator reading a range that the runtime then refuses has
    /// been told a different rule from the one in force.
    /// </summary>
    [Fact]
    public void The_page_prints_the_bounds_the_validator_refuses_by()
    {
        var page = Read();

        Assert.Contains($"{UpdateOptions.MinimumInitialDelayMinutes}..{UpdateOptions.MaximumInitialDelayMinutes}", page, StringComparison.Ordinal);
        Assert.Contains($"{UpdateOptions.MinimumIntervalHours}..{UpdateOptions.MaximumIntervalHours}", page, StringComparison.Ordinal);
    }

    /// <summary>The asset names are the contract with everything downstream: a script, a feed, a person with curl.</summary>
    [Fact]
    public void The_page_names_the_assets_the_code_names()
    {
        var page = Read();

        foreach (var rid in ReleaseAssets.Rids)
        {
            Assert.Contains(ReleaseAssets.For(rid), page, StringComparison.Ordinal);
        }

        Assert.Contains(ReleaseAssets.Manifest, page, StringComparison.Ordinal);
        Assert.Contains(ReleaseAssets.Checksums, page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The manifest is read field by field, and the page describes it field by field. A field the page forgets
    /// is a field a release could omit without anybody noticing until an updater refused the feed.
    /// </summary>
    [Fact]
    public void The_page_describes_every_field_a_manifest_must_carry()
    {
        var page = Read();

        foreach (var field in (string[])["schema", "version", "published_at", "release_notes_url", "min_upgrade_from", "artifacts", "asset", "sha256", "size"])
        {
            Assert.Contains($"`{field}`", page, StringComparison.Ordinal);
        }

        Assert.Contains(UpdateManifest.CurrentSchema.ToString(System.Globalization.CultureInfo.InvariantCulture), page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two sentences this page exists to make checkable: what a check sends, and that nothing is signed.
    /// Both are promises to a person who is deciding whether to let a runtime talk to the internet at all.
    /// </summary>
    /// <summary>
    /// Both callers of the feed wait exactly as long as this build says they do, and the page prints that
    /// number from the constant rather than from somebody's memory of it.
    /// </summary>
    /// <remarks>
    /// There were two waits, declared separately, and they had already drifted from the one the design chose:
    /// the runtime's unattended check and <c>jason update check</c> each said thirty seconds where ten was
    /// decided. A person reading the page cannot see which of the two they are waiting for, so there is one.
    /// </remarks>
    [Fact]
    public void The_page_prints_the_one_wait_both_callers_of_the_feed_use()
    {
        var page = Read();
        var seconds = (int)UpdateFeed.DefaultTimeout.TotalSeconds;

        Assert.Contains($"{seconds} seconds", page, StringComparison.Ordinal);

        // And it is one wait: neither caller declares its own.
        foreach (var caller in new[]
                 {
                     Source("runtime/src/Jason.Runtime/Hosting/Modules/UpdateModule.cs"),
                     Source("runtime/src/Jason.Cli/Commands/UpdateCommands.cs"),
                 })
        {
            Assert.Contains("UpdateFeed.DefaultTimeout", caller, StringComparison.Ordinal);
            Assert.DoesNotContain("TimeSpan.FromSeconds(30)", caller, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_page_says_what_is_sent_and_what_is_not_signed()
    {
        var page = Read();

        Assert.Contains("User-Agent", page, StringComparison.Ordinal);
        Assert.Contains("jason/<version>", page, StringComparison.Ordinal);
        Assert.Contains("signed", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// And that it says plainly what this version cannot do. A page describing an applier that does not exist
    /// would have a reader waiting for a command to finish that was never going to start.
    /// </summary>
    [Fact]
    public void The_page_says_that_nothing_is_applied_in_this_version()
    {
        var page = Read();

        Assert.Contains("applies nothing in this version", page, StringComparison.Ordinal);
        Assert.Contains("404", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sentence that explains why the data directory and the install directory are two things sits in the
    /// paragraph that names them both, and not after the one about a cache neither of them contains.
    /// </summary>
    /// <remarks>
    /// It ended up there when the extraction cache was added between them, and the result reads as if the cache
    /// were one of the two — the kind of defect that only a reader notices and no test had a way to.
    /// </remarks>
    [Fact]
    public void The_page_explains_the_two_directories_in_the_paragraph_that_names_them()
    {
        var page = Read();
        var paragraphs = page.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        var both = Assert.Single(
            paragraphs,
            p => p.Contains("**data directory**", StringComparison.Ordinal)
                && p.Contains("**install directory**", StringComparison.Ordinal));
        Assert.Contains("separate on purpose", both, StringComparison.Ordinal);

        var cache = Assert.Single(paragraphs, p => p.Contains("DOTNET_BUNDLE_EXTRACT_BASE_DIR", StringComparison.Ordinal));
        Assert.DoesNotContain("separate on purpose", cache, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the page says why a dry run of the release workflow cannot happen before a merge, because the first
    /// question a reader asks of a pipeline nobody has run is "then how do you know it works?".
    /// </summary>
    [Fact]
    public void The_page_says_why_a_dry_run_can_only_happen_after_a_merge()
    {
        var page = Read();

        Assert.Contains("workflow_dispatch", page, StringComparison.Ordinal);
        Assert.Contains("default branch", page, StringComparison.Ordinal);
    }

    private static string Read() =>
        File.ReadAllText(Path.Combine(DocumentsDirectory(), "release-and-update.md"));

    /// <summary>One of this repository's own files, read as text: a rule kept in two places is read in two places.</summary>
    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(DocumentsDirectory())!, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string DocumentsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? ".", "docs");
    }
}
