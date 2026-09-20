using Jason.Cli.Update;
using Jason.Contracts.Discovery;
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
    /// What <c>update_file_refused</c> covers, as the code now raises it: not only a file an update had to
    /// move, but a rollback's renames, the ledger's own write and the database restore. A row that names one
    /// of four sends a person reading it to the wrong place — and it is the row they read at the worst moment.
    /// </summary>
    [Fact]
    public void The_page_says_which_files_the_refusal_about_a_file_covers()
    {
        var row = Row(UpdateCodes.FileRefused);

        Assert.Contains("rollback", row, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database", row, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ledger", row, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The one row of the codes table that begins with this code, for a test that is about its wording.</summary>
    private static string Row(string code)
    {
        var row = Read()
            .Split('\n')
            .FirstOrDefault(line => line.TrimStart().StartsWith($"| `{code}` |", StringComparison.Ordinal));

        Assert.NotNull(row);
        return row;
    }

    /// <summary>
    /// The steps and the refusal codes on the page are the ones this build has, read out of the enum and out of
    /// <see cref="UpdateCodes"/> rather than typed twice. A code renamed in the code turns the page red, which
    /// is the only way a table of error codes stays true for longer than the week it was written in.
    /// </summary>
    [Fact]
    public void Every_step_and_code_on_the_page_is_one_the_code_has()
    {
        var page = Read();

        // In the table of steps, and not merely somewhere on the page: half the step names read as ordinary
        // English, so a row dropped out of the table would still be "mentioned" by the prose around it.
        foreach (var step in Enum.GetValues<UpdateStep>())
        {
            Assert.Contains($"| `{step.ToString().ToLowerInvariant()}` |", page, StringComparison.Ordinal);
        }

        foreach (var code in UpdateCodes.All)
        {
            Assert.Contains($"| `{code}` |", page, StringComparison.Ordinal);
        }

        // And the other way round: a code on the page that nothing raises sends a reader looking through their
        // logs for a failure this build cannot produce.
        var known = new HashSet<string>(UpdateCodes.All, StringComparer.Ordinal)
        {
            UpdateFeedException.Invalid,
            UpdateFeedException.Unreachable,
            UpdateFeedException.Insecure,
            UpdateLedgerException.Invalid,
        };

        foreach (var printed in Printed(page, "update_"))
        {
            Assert.Contains(printed, known);
        }
    }

    /// <summary>
    /// What an update leaves alone, in the paragraph that promises it. An operator deciding whether to type
    /// <c>jason update apply</c> on a machine that is doing real work is asking exactly this, and a page that
    /// answers it in scattered half-sentences has not answered it.
    /// </summary>
    [Fact]
    public void The_page_says_what_an_update_does_not_touch()
    {
        var untouched = Assert.Single(
            Paragraphs(),
            paragraph => paragraph.Contains("It does not touch the rest of the data", StringComparison.Ordinal));

        foreach (var kept in (string[])["database", "settings.json", "plugins", "skills", "logs", "work directories"])
        {
            Assert.Contains(kept, untouched, StringComparison.Ordinal);
        }

        // And the registration an operator wrote themselves, which is the one a rename could have broken.
        Assert.Contains("systemd unit", untouched, StringComparison.Ordinal);
        Assert.Contains("scheduled task", untouched, StringComparison.Ordinal);
    }

    /// <summary>
    /// Restoring a database is a documented procedure rather than a verb, so the procedure has to be complete:
    /// a person following three of its four steps ends up with a restored database and a write-ahead log from
    /// after the migration, which SQLite replays into the corruption the restore was meant to avoid.
    /// </summary>
    [Fact]
    public void The_page_prints_the_manual_database_restore_in_full()
    {
        var page = Read();
        var restore = page[page.IndexOf("Restoring a database by hand", StringComparison.Ordinal)..];

        var stop = restore.IndexOf("jason runtime stop", StringComparison.Ordinal);
        var copy = restore.IndexOf("state/jason.db", StringComparison.Ordinal);
        var sidecars = restore.IndexOf("jason.db-wal", StringComparison.Ordinal);
        var start = restore.IndexOf("runtime start", StringComparison.Ordinal);

        Assert.True(stop >= 0, "the page does not say to stop the runtime first");
        Assert.True(copy > stop, "the page does not say to copy the backup over the database, after the stop");
        Assert.True(sidecars > copy, "the page does not say to delete the sidecars, after the copy");
        Assert.Contains("jason.db-shm", restore, StringComparison.Ordinal);
        Assert.True(start > sidecars, "the page does not say to start the matching binary last");

        // The backups are where the migrator puts them, not where somebody remembered they were.
        Assert.Contains("state/backups", restore, StringComparison.Ordinal);
    }

    /// <summary>
    /// The applier ships before there is anything for it to apply, and the very first release cannot carry it
    /// at all — the first tag is cut from a <c>main</c> that predates this work. Both sentences are for the
    /// same reader: the one who installed Jason today and typed <c>jason update apply</c>.
    /// </summary>
    [Fact]
    public void The_page_says_the_applier_has_nothing_to_apply_until_the_first_release()
    {
        var page = Read();

        var nothing = Assert.Single(
            Paragraphs(),
            paragraph => paragraph.Contains("nothing to apply until the first release", StringComparison.Ordinal));

        Assert.Contains("0.1.0` installation has no `jason update apply", nothing, StringComparison.Ordinal);
        Assert.Contains("install one-liner", nothing, StringComparison.Ordinal);

        // And why the one-liner itself answers nothing until then, which is the same fact from the other end.
        Assert.Contains("404", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one window an update cannot be typed out of, and both ways through it. Between <c>kept</c> and
    /// <c>swapped</c> the install path is empty, so the <c>jason</c> on a person's PATH is the file that is not
    /// there — and a page that describes the window without printing a way out has told somebody their
    /// installation is broken and stopped.
    /// </summary>
    [Fact]
    public void The_page_prints_both_ways_out_of_the_window_where_the_install_path_is_empty()
    {
        var window = Assert.Single(
            Paragraphs(),
            paragraph => paragraph.Contains("Between `kept` and `swapped` the install path is empty", StringComparison.Ordinal));

        Assert.Contains("one rename wide", window, StringComparison.Ordinal);

        // The two directories, named from the paths the code composes rather than from memory.
        var page = Read();
        var update = new UpdatePaths(new JasonPaths(Path.Combine("~", ".jason")));
        Assert.Contains($"update/{Path.GetFileName(update.Applier)}", page, StringComparison.Ordinal);
        Assert.Contains($"update/{Path.GetFileName(update.Previous)}", page, StringComparison.Ordinal);

        // Way out one: run the copy that is doing the swap. Way out two: put the kept file back by hand.
        Assert.Contains($"{Path.GetFileName(update.Applier)}/jason update apply", page, StringComparison.Ordinal);
        Assert.Contains("by hand", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// An update ends with a runtime running, including on an installation that had none when it began — which
    /// is not a corner case but the ordinary way to update something nobody has started yet, and the only shape
    /// in which the new build's first start is the one that migrates.
    /// </summary>
    [Fact]
    public void The_page_says_an_update_leaves_the_runtime_running()
    {
        var running = Assert.Single(
            Paragraphs(),
            paragraph => paragraph.Contains("leaves a runtime running", StringComparison.Ordinal));

        Assert.Contains("no runtime answering", running, StringComparison.Ordinal);
        Assert.Contains("`drained` and `stopped` are satisfied", running, StringComparison.Ordinal);
        Assert.Contains("first", running, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the Unix execute bit comes back, and that both places named really restore it. A zip does not
    /// carry the bit and a tar carries somebody else's, so an unpacked file that nobody chmods is a release
    /// that installs and will not run.
    /// </summary>
    [Fact]
    public void The_page_names_where_the_unix_execute_bit_is_restored()
    {
        var page = Read();

        Assert.Contains("execute bit", page, StringComparison.Ordinal);
        Assert.Contains("UpdateStager", page, StringComparison.Ordinal);
        Assert.Contains("install.sh", page, StringComparison.Ordinal);

        // Both named places do it, so the page is naming code rather than an intention.
        Assert.Contains("SetUnixFileMode", Source("runtime/src/Jason.Cli/Update/UpdateStager.cs"), StringComparison.Ordinal);
        Assert.Contains("chmod", Source("install/install.sh"), StringComparison.Ordinal);
    }

    /// <summary>The page's paragraphs, the way the two-directory guard above reads them.</summary>
    private static string[] Paragraphs() => Read().Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Every identifier on the page that begins with <paramref name="prefix"/>, read out of the inline code
    /// spans rather than out of a list somebody keeps beside the page.
    /// </summary>
    private static IEnumerable<string> Printed(string page, string prefix)
    {
        for (var at = page.IndexOf(prefix, StringComparison.Ordinal); at >= 0; at = page.IndexOf(prefix, at + 1, StringComparison.Ordinal))
        {
            var end = at;
            while (end < page.Length && (char.IsAsciiLetterLower(page[end]) || page[end] == '_'))
            {
                end++;
            }

            yield return page[at..end];
        }
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
