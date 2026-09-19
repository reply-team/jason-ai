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

    private static string Read() =>
        File.ReadAllText(Path.Combine(DocumentsDirectory(), "release-and-update.md"));

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
