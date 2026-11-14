using System.Text.RegularExpressions;
using Jason.Cli.Autostart;
using Jason.Cli.Update;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Jason.Runtime.Plugins.Registry;

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

        foreach (var name in ReleaseAssets.Published)
        {
            Assert.Contains(name, page, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The manifest is read field by field, and the page describes it field by field. A field the page forgets
    /// is a field a release could omit without anybody noticing until an updater refused the feed.
    /// </summary>
    [Fact]
    public void The_page_describes_every_field_a_manifest_must_carry()
    {
        var page = Read();

        foreach (var field in (string[])["schema", "version", "published_at", "release_notes_url", "min_upgrade_from", "artifacts", "asset", "sha256", "size", "plugins", "digest", "recorded"])
        {
            Assert.Contains($"`{field}`", page, StringComparison.Ordinal);
        }

        Assert.Contains(UpdateManifest.CurrentSchema.ToString(System.Globalization.CultureInfo.InvariantCulture), page, StringComparison.Ordinal);
    }

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
                     Source("dotnet/src/Jason.Runtime/Hosting/Modules/UpdateModule.cs"),
                     Source("dotnet/src/Jason.Cli/Commands/UpdateCommands.cs"),
                 })
        {
            Assert.Contains("UpdateFeed.DefaultTimeout", caller, StringComparison.Ordinal);
            Assert.DoesNotContain("TimeSpan.FromSeconds(30)", caller, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The two sentences this page exists to make checkable: what a check sends, and which executables a release signs.
    /// Both are promises to a person who is deciding whether to let a runtime talk to the internet at all, and whether to
    /// run what it downloads. The environment that keeps the signature to release tags and release branches also
    /// decides where the pipeline is rehearsed, and the page says so where it tells how.
    /// </summary>
    [Fact]
    public void The_page_says_what_is_sent_and_which_executables_are_signed()
    {
        var page = Regex.Replace(Read(), @"\s+", " ");

        Assert.Contains("User-Agent", page, StringComparison.Ordinal);
        Assert.Contains("jason/<version>", page, StringComparison.Ordinal);
        Assert.Contains("**The macOS executable is signed with Reply's Developer ID and notarized by Apple, and the Windows executable with a certificate issued to Reply through Azure Artifact Signing, timestamped; the Linux executable is not signed.**", page, StringComparison.Ordinal);
        Assert.Contains("On Linux it is the only provenance there is; on macOS and Windows the signature says who built the executable as well.", page, StringComparison.Ordinal);
        Assert.Contains("The Windows signature is made by the release's Windows job alone: it runs in the GitHub environment `signing`, which only release tags and release branches may run in, and signs in to Azure with that job's own OIDC token, which Azure accepts from that environment alone; there is no client secret.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Signing the Windows executable is its own story", page, StringComparison.Ordinal);

        // What the macOS signature covers beyond the executable: the one native library it unpacks, which is why the
        // executable can keep library validation and needs only the JIT's two entitlements.
        Assert.Contains("On macOS the one native library the executable unpacks, `libe_sqlite3.dylib`, is signed with the same Developer ID before it is bundled, so the executable keeps the hardened runtime's library validation and ignores `DYLD_` environment variables; its only entitlements, `com.apple.security.cs.allow-jit` and `com.apple.security.cs.allow-unsigned-executable-memory`, are the ones the .NET runtime's JIT compiler needs.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("disable-library-validation", page, StringComparison.Ordinal);

        // What that environment's rule makes of a rehearsal: the macOS and Windows jobs of a run from any other branch
        // are refused, and the job that assembles the release needs them, so a change to the pipeline is rehearsed from
        // a release branch.
        Assert.Contains("a branch's own version can be run from it (`gh workflow run release.yml --ref <branch>`). Its jobs that sign, publish or hold a token run in the environment `signing`, which admits only release tags and release branches, so a change to the pipeline is rehearsed from a release branch before it merges: a run from any other branch, a dry run included, is refused at the macOS and Windows jobs, and nothing is assembled.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("which is how a change to the pipeline is rehearsed before it merges", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing in a release is signed", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// One repository builds and publishes a release: the tag pushed on the merge of the release pull request builds it
    /// and publishes it at once, and the same tag anywhere else publishes nothing; the button's draft is for reading and
    /// is deleted, never published. The page says so where it says what a release is, and no longer speaks of a second
    /// repository, a publication token or a pull request that publishes.
    /// </summary>
    [Fact]
    public void The_page_says_one_repository_builds_and_publishes_a_release()
    {
        var page = Regex.Replace(Read(), @"\s+", " ");

        Assert.Contains("Everything about a release happens in one repository, `reply-team/jason-ai`: the source, its tags and its releases.", page, StringComparison.Ordinal);
        Assert.Contains("A release starts as the release pull request into `main`, whose run builds and tests the tree on Linux, macOS and Windows; merging it is the last review a release has.", page, StringComparison.Ordinal);
        Assert.Contains("publishes the release at once, with the assets below and its notes, as the latest release unless a newer one already is or the version is a release candidate;", page, StringComparison.Ordinal);
        Assert.Contains("The same tag pushed to any other repository that holds the workflow — a fork, a copy — builds what it can and publishes nothing: a copy without the signing secrets stops at the macOS and Windows jobs, and one that holds them assembles the release and says in one line that it publishes nothing.", page, StringComparison.Ordinal);
        Assert.Contains("The button's draft is made in `reply-team/jason-ai` alone, names the commit the run built, is never the latest release, and asks nothing of the tap.", page, StringComparison.Ordinal);
        Assert.Contains("The draft is for reading and is deleted afterwards: a release is published only by its tag, never by publishing the button's draft.", page, StringComparison.Ordinal);

        foreach (var gone in (string[])["Two repositories carry a release", "publication token", "the pull request's body", "open set", "PUBLISH_" + "TOKEN"])
        {
            Assert.DoesNotContain(gone, page, StringComparison.Ordinal);
        }
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
    [Critical(Family.D1, "the update contract page matches the code")]
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
            AutostartCodes.Unsupported,
            AutostartCodes.Refused,
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

        foreach (var kept in (string[])["database", "settings.json", "plugins", "logs", "work directories"])
        {
            Assert.Contains(kept, untouched, StringComparison.Ordinal);
        }

        // And the registration an operator wrote themselves, which is the one a rename could have broken.
        Assert.Contains("systemd unit", untouched, StringComparison.Ordinal);
        Assert.Contains("scheduled task", untouched, StringComparison.Ordinal);

        // The skills are no longer among them: the apply ends by redeploying the ones this installation recorded.
        Assert.DoesNotContain("not the skills", Flat(untouched), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the apply does to the skills, said where a person reads what it does to everything else: the step after
    /// <c>complete</c>, what it carries and from where, that its failure leaves the update standing and what finishes it,
    /// and the one thing an update applied by an older build still needs.
    /// </summary>
    [Fact]
    public void The_page_says_the_apply_ends_by_redeploying_the_recorded_skills()
    {
        var skills = Flat(Assert.Single(
            Paragraphs(),
            paragraph => Flat(paragraph).Contains("Its last step, after `complete`, is the skills", StringComparison.Ordinal)));

        Assert.Contains("exactly what `jason skills update` does, at the new release's tag", skills, StringComparison.Ordinal);
        Assert.Contains("from the source its record names", skills, StringComparison.Ordinal);
        Assert.Contains("nothing is written into an agent's harness that has no record", skills, StringComparison.Ordinal);
        Assert.Contains("the update still stands", skills, StringComparison.Ordinal);
        Assert.Contains("`run: jason skills update`", skills, StringComparison.Ordinal);
        Assert.Contains(
            "An update applied by a build before 0.12.0 has no such step, so it needs one `jason skills update` after it; from 0.12.0 on, `jason update apply` does it.",
            skills,
            StringComparison.Ordinal);
        Assert.DoesNotContain("printed for you, not run for you", Read(), StringComparison.Ordinal);

        // What it covers, said as what it is: the roots detection finds, and one line per pack group rather than per pack.
        Assert.Contains("each agent harness this machine detects", skills, StringComparison.Ordinal);
        Assert.Contains("a deployment made with `--root` into a directory detection does not find is not", skills, StringComparison.Ordinal);
        Assert.Contains("one `skills:` line for each pack group", skills, StringComparison.Ordinal);

        // And what does not run it at all: a package manager's upgrade never types `jason update apply`.
        Assert.Contains("A package manager's upgrade does not run `jason update apply`", skills, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rollback puts the executable back and leaves the skills where the update's last step put them, so the page that
    /// says how to go back says what then answers <c>ready</c> again.
    /// </summary>
    [Fact]
    public void The_page_says_a_rollback_leaves_the_skills_at_the_newer_tag()
    {
        var going = Flat(Assert.Single(
            Paragraphs(),
            paragraph => Flat(paragraph).Contains("A rollback leaves the skills", StringComparison.Ordinal)));

        Assert.Contains("at the newer release's tag", going, StringComparison.Ordinal);
        Assert.Contains("`jason skills update`", going, StringComparison.Ordinal);
        Assert.Contains("`jason skills install --roles-only`", going, StringComparison.Ordinal);
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
    /// What somebody who typed <c>jason update apply</c> on a fresh installation needs to know, stated as the
    /// rule rather than as today's state: where the feed names no release there is nothing to apply.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sentence that used to be here said the first release could not carry the applier <em>at all</em>,
    /// because the first tag was expected from a <c>main</c> that predated it. That stopped being true four
    /// merges ago, and this guard is what kept the sentence alive: a false claim with a green test behind it
    /// is the sharpest form of this defect class, because the test is the reason nobody reread the sentence.
    /// </para>
    /// <para>
    /// The negative assertion is deliberate and is the one retraction here. A scan for an absent phrase is
    /// only the right shape because the positive assertion above it is what carries the claim; on its own it
    /// would pass on an empty page.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_page_says_what_an_applier_does_where_the_feed_names_no_release()
    {
        var page = Read();

        var nothing = Assert.Single(
            Paragraphs(),
            paragraph => paragraph.Contains("nothing to apply until one is published", StringComparison.Ordinal));

        Assert.Contains("update_feed_unreachable", nothing, StringComparison.Ordinal);
        Assert.DoesNotContain("predates this work", page, StringComparison.Ordinal);

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

        // The executable's rename aside, its notices' after it, and the rename in: no longer one rename wide.
        Assert.Contains("a few renames wide", window, StringComparison.Ordinal);
        Assert.DoesNotContain("one rename wide", window, StringComparison.Ordinal);

        // The two directories, named from the paths the code composes rather than from memory.
        var page = Read();
        var update = new UpdatePaths(new JasonPaths(Path.Combine("~", ".jason"), 1));
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
        Assert.Contains("SetUnixFileMode", Source("dotnet/src/Jason.Cli/Update/UpdateStager.cs"), StringComparison.Ordinal);
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

    /// <summary>
    /// What each platform registers, named on the page, with the file each of them leaves behind. An operator
    /// who wants to know what was done to their machine should not have to read this repository to find out.
    /// </summary>
    [Fact]
    public void The_page_explains_what_autostart_registers_on_each_platform()
    {
        var page = Read();

        Assert.Contains("schtasks", page, StringComparison.Ordinal);
        // The task's name, the way the page has to spell it: one per account, named for the account's SID.
        Assert.Contains($"`{AutostartArtifacts.TaskName("<SID>")}`", page, StringComparison.Ordinal);
        Assert.Contains(AutostartArtifacts.Label, page, StringComparison.Ordinal);
        Assert.Contains(AutostartArtifacts.UnitName, page, StringComparison.Ordinal);
        Assert.Contains("Library/LaunchAgents", page, StringComparison.Ordinal);
        Assert.Contains(".config/systemd/user", page, StringComparison.Ordinal);
        Assert.Contains(AutostartArtifacts.TaskDocument, page, StringComparison.Ordinal);
    }

    /// <summary>
    /// And that nothing watches it afterwards. "It starts at logon" and "it is kept running" are different
    /// promises, and a person who assumed the second would not look at their runtime again for a week.
    /// </summary>
    [Fact]
    public void The_page_says_what_is_not_supervised()
    {
        var page = Read();

        Assert.Contains("registration, not supervision", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KeepAlive", page, StringComparison.Ordinal);
        Assert.Contains("stays stopped", page, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The registration carries the data directory it was made under, and `status` prints it: D33.</summary>
    [Fact]
    public void The_page_says_the_registration_carries_the_data_directory()
    {
        var page = Read();

        Assert.Contains("--data-dir", page, StringComparison.Ordinal);
        Assert.Contains(JasonPaths.DataDirectoryVariable, page, StringComparison.Ordinal);
        Assert.Contains("autostart status", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two verbs that do nothing to a runtime that is running now. Somebody who reads `enable` as "start
    /// it" and `disable` as "stop it" will type one of them and watch nothing happen.
    /// </summary>
    [Fact]
    public void The_page_says_enable_starts_nothing_now()
    {
        var page = Read();

        Assert.Contains("`enable` starts nothing now", page, StringComparison.Ordinal);
        Assert.Contains("jason runtime start", page, StringComparison.Ordinal);
        Assert.Contains("jason runtime stop", page, StringComparison.Ordinal);
    }

    /// <summary>One registration per account: a second `enable` replaces it rather than adding another.</summary>
    [Fact]
    public void The_page_says_a_registration_is_one_per_user()
    {
        var page = Read();

        Assert.Contains("replaces", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("per account", page, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An autostarted runtime has the account's logon environment, not a shell's — which is where a plugin that
    /// works from a terminal and not after a logon comes from. <see cref="ISearchPath"/> makes the same remark
    /// about the same fact from the other side.
    /// </summary>
    [Fact]
    public void The_page_says_an_autostarted_runtime_has_the_logon_environment()
    {
        var page = Read();

        Assert.Contains("logon environment", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unavailable", page, StringComparison.Ordinal);
        Assert.Contains("PATH", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Which accounts a logon task works for, now that it is an interactive logon: every account, from the prompt
    /// it already has. The batch-logon right the old S4U task needed, and the standard accounts it could not
    /// serve, are gone from the page with it.
    /// </summary>
    [Fact]
    public void The_page_says_every_account_registers_from_an_ordinary_prompt()
    {
        var page = Read();

        Assert.Contains("from an ordinary prompt", page, StringComparison.Ordinal);
        Assert.Contains("standard account", page, StringComparison.Ordinal);
        Assert.Contains("`InteractiveToken`", page, StringComparison.Ordinal);
        Assert.DoesNotContain("SeBatchLogonRight", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Log on as a batch job", page, StringComparison.Ordinal);
        Assert.DoesNotContain("unsupported in this version", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page quotes Microsoft, and says so wherever it does: a sentence from the Task Scheduler's reference or
    /// the console documentation is Microsoft's claim, not one this project measured, and a reader should be able
    /// to tell which is which without leaving the paragraph — and to open the page it came from.
    /// </summary>
    [Theory]
    [InlineData("\"no password is stored by the system and there is no access to either the network or encrypted files\"", "https://learn.microsoft.com/en-us/windows/win32/taskschd/principal-logontype")]
    [InlineData("\"User must already be logged on. The task will be run only in an existing interactive session.\"", "https://learn.microsoft.com/en-us/windows/win32/taskschd/principal-logontype")]
    [InlineData("\"Interactive applications are terminated at logoff\"", "https://learn.microsoft.com/en-us/windows/console/handlerroutine")]
    [InlineData("\"informs the operating system to skip allocating a console\"", "https://learn.microsoft.com/en-us/windows/console/console-allocation-policy")]
    public void The_page_quotes_microsoft_as_microsoft(string quotation, string source)
    {
        var paragraph = Assert.Single(
            Read().ReplaceLineEndings("\n").Split("\n\n"),
            candidate => candidate.ReplaceLineEndings(" ").Contains(quotation, StringComparison.Ordinal));

        Assert.Contains("Microsoft", paragraph, StringComparison.Ordinal);
        Assert.Contains($"]({source})", paragraph, StringComparison.Ordinal);
    }

    /// <summary>
    /// And nothing that is not Microsoft's is put in quotation marks beside Microsoft's name. The page once quoted
    /// its own reading of the console setting as if it were the setting's documentation.
    /// </summary>
    [Fact]
    public void The_page_puts_no_paraphrase_in_microsoft_s_mouth() =>
        Assert.DoesNotContain(
            "\"give this program a console only if it inherits one\"",
            Read().ReplaceLineEndings(" "),
            StringComparison.Ordinal);

    /// <summary>
    /// The one cost of an interactive logon, said where the registration is described rather than discovered at
    /// the next sign-out, with where the work that was in flight goes.
    /// </summary>
    [Fact]
    public void The_page_says_what_signing_out_costs()
    {
        var page = Read();

        Assert.Contains("ends when you sign out", page, StringComparison.Ordinal);
        Assert.Contains("Stopping and restarting", page, StringComparison.Ordinal);

        // Measured: a runtime ended by a sign-out wrote neither the line it writes when it begins to stop nor the one
        // it writes when it has stopped.
        Assert.Contains("ends when you sign out, without a chance to say so", page.ReplaceLineEndings(" "), StringComparison.Ordinal);
    }

    /// <summary>
    /// Where "no window" stops being true: the setting is read by 24H2 and later and ignored before, and what is
    /// said about the older builds was measured with a test program lacking the setting, on 24H2, not on an older
    /// Windows. The page says both.
    /// </summary>
    [Fact]
    public void The_page_says_where_the_window_guarantee_ends()
    {
        var page = Read();

        Assert.Contains("`consoleAllocationPolicy`", page, StringComparison.Ordinal);
        Assert.Contains("24H2", page, StringComparison.Ordinal);
        Assert.Contains("26100", page, StringComparison.Ordinal);
        Assert.Contains("Windows Server 2025", page, StringComparison.Ordinal);
        Assert.Contains("closes by itself", page, StringComparison.Ordinal);

        // What has and has not been run on an older Windows: never at logon; the packaged executable, on every pull
        // request, on the build the CI job names, where a runtime `jason runtime start` launched says freed_hidden.
        var words = page.ReplaceLineEndings(" ");
        Assert.Contains("has not been started at logon on an older Windows", words, StringComparison.Ordinal);
        Assert.DoesNotContain("It has not been run on an older Windows", words, StringComparison.Ordinal);
        Assert.Contains("on Windows Server 2022 (build 20348)", words, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-2022", Source(".github/workflows/ci.yml"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The log line the page tells a person to read is the one the runtime's file carries, as the file carries it.
    /// The log is JSON Lines: the template and the value are separate properties, so the rendered sentence the page
    /// used to quote appears in no log anywhere, and a person searching for it found nothing. The page's text is
    /// computed from the code here, so neither can be renamed without the other.
    /// </summary>
    [Theory]
    [InlineData(ConsoleAtStart.None)]
    [InlineData(ConsoleAtStart.Freed)]
    [InlineData(ConsoleAtStart.FreedHidden)]
    [InlineData(ConsoleAtStart.NotFreed)]
    public void The_log_line_the_page_quotes_is_the_one_the_runtime_writes(ConsoleAtStart console)
    {
        var page = Read();

        Assert.Contains($"\"@mt\":\"{ProcessDetacher.LogTemplate}\"", page, StringComparison.Ordinal);
        Assert.Contains($"`\"Console\":\"{ProcessDetacher.Word(console)}\"`", page, StringComparison.Ordinal);
        Assert.DoesNotContain($"`Console at start: {ProcessDetacher.Word(console)}`", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// And what nothing here proves. Registering for real is not something a test does — it would leave a logon
    /// task on whoever ran the suite — so the page says which platforms can be checked by hand at all, which
    /// one was only ever composed, and what such a check has found <b>so far</b>.
    /// </summary>
    /// <remarks>
    /// That last part is why this test is shaped the way it is. The page said the check had been made before
    /// anybody had made one: a sentence in the past tense, written in advance, about the one claim on the page
    /// that no test can stand behind. It says what it has, and it is written again when there is more.
    /// </remarks>
    [Fact]
    public void The_page_says_what_only_a_hand_check_can_prove_and_what_it_has_proved_so_far()
    {
        var page = Read();

        Assert.Contains("by hand", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("composed and asserted only", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("macOS", page, StringComparison.Ordinal);

        // The line that carries the outcome, whatever the outcome is.
        Assert.Contains("Checked by hand so far:", page, StringComparison.Ordinal);

        // And it has an outcome. The check ran on Linux; the paragraph says what held there and stops. "nothing"
        // was true for exactly as long as it was true.
        Assert.DoesNotContain("Checked by hand so far:** nothing", page, StringComparison.Ordinal);
        Assert.Contains("systemd --user", page, StringComparison.Ordinal);

        // Windows was checked by hand in the shape this version registers, on a standard account, through a real
        // sign-out and sign-in. The record says what held, with the log line in the form the file carries it, and
        // what the runtime said when `jason runtime start` started it instead.
        Assert.DoesNotContain("not yet checked by hand in the shape this version registers", page, StringComparison.Ordinal);
        var record = HandCheckRecord(page);
        Assert.Contains("Windows, on 2026-09-29, on a standard account", record, StringComparison.Ordinal);
        Assert.Contains("signed out and back in", record, StringComparison.Ordinal);
        Assert.Contains("ended when the account signed out", record, StringComparison.Ordinal);
        Assert.Contains($"`\"@mt\":\"{ProcessDetacher.LogTemplate}\"` with `\"Console\":\"{ProcessDetacher.Word(ConsoleAtStart.None)}\"`", record, StringComparison.Ordinal);
        Assert.Contains($"`\"Console\":\"{ProcessDetacher.Word(ConsoleAtStart.FreedHidden)}\"`", record, StringComparison.Ordinal);

        // The administrator's half of "an administrator's account or a standard account" was measured too, while the
        // standard account's task stood registered, so the two accounts' tasks were shown side by side.
        Assert.Contains("On an administrator's account, from an ordinary prompt", record, StringComparison.Ordinal);
        Assert.Contains("while the standard account's task stood registered beside it", record, StringComparison.Ordinal);
    }

    /// <summary>The hand-check block, read as one line: from the line that names what was checked to what was not.</summary>
    private static string HandCheckRecord(string page)
    {
        var from = page.IndexOf("Checked by hand so far:", StringComparison.Ordinal);
        var to = page.IndexOf("**Not checked:**", from, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "The page has no hand-check block.");

        return string.Join(' ', page[from..to].ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimStart('>', ' ').TrimEnd()));
    }

    /// <summary>
    /// "Where things live" lists what the data directory holds, and autostart puts a file in it. A page that
    /// enumerates a directory and misses an entry is worse than one that does not enumerate it at all.
    /// </summary>
    [Fact]
    public void The_page_says_where_the_autostart_document_lives_among_the_others()
    {
        var page = Read();
        var directories = page[page.IndexOf("## 10. Where things live", StringComparison.Ordinal)..];

        Assert.Contains("autostart", directories, StringComparison.Ordinal);
        Assert.Contains("LaunchAgents", directories, StringComparison.Ordinal);
        Assert.Contains("systemd", directories, StringComparison.Ordinal);
    }

    /// <summary>
    /// The autostart refusals, read out of the code like the update ones above: the page's two rows are the two
    /// codes this build can raise, and no others.
    /// </summary>
    [Fact]
    public void The_autostart_codes_on_the_page_are_the_ones_the_code_has()
    {
        var page = Read();

        foreach (var code in AutostartCodes.All)
        {
            Assert.Contains($"| `{code}` |", page, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The one case an elevated prompt is still for: a task an earlier version registered from one. It is worded
    /// on the refusal having happened, because it was never measured — the task's own security may well let the
    /// same account remove it from an ordinary prompt — so the page says what to do if it happens, and never that
    /// it will.
    /// </summary>
    [Fact]
    public void The_page_says_the_one_case_an_elevated_prompt_is_for()
    {
        // Read as one line, so where the page happens to wrap decides nothing.
        var page = Read().ReplaceLineEndings(" ");

        Assert.Contains(
            "**If one of them answers `ERROR: Access is denied.` about the task named `Jason`**, an earlier version may have registered it from an elevated prompt",
            page,
            StringComparison.Ordinal);
        Assert.Contains("`jason runtime autostart disable` from an elevated prompt once", page, StringComparison.Ordinal);

        // Named: the task 0.1.0 left is `\Jason`, the one name every account shared, and only this account's goes.
        Assert.Contains($"called `\\{AutostartArtifacts.LegacyTaskName}` for the whole machine", page, StringComparison.Ordinal);
        Assert.Contains("take it away only where it is this account's 0.1.0 task, the one that runs the runtime", page, StringComparison.Ordinal);
        Assert.Contains("another account's, or a task of that name that runs anything else, is never touched", page, StringComparison.Ordinal);
        Assert.DoesNotContain("this account's is taken away, and another account's is never touched", page, StringComparison.Ordinal);

        // The elevated remedy is an administrator's act on their own account; a standard account asks an administrator.
        Assert.Contains("On an administrator's own account", page, StringComparison.Ordinal);
        Assert.Contains("on a standard account an elevated prompt runs as somebody else", page, StringComparison.Ordinal);
        Assert.Contains("ask an administrator to delete the task by the name the message gives", page, StringComparison.Ordinal);
        Assert.Contains("never run `jason` under another account", page, StringComparison.Ordinal);

        // And uninstall gets its own remedy. "The same remedy applies" handed it the one that ends by enabling again.
        Assert.Contains("run the same elevated `disable`, then `jason uninstall` again", page, StringComparison.Ordinal);
        Assert.DoesNotContain("the same remedy applies", page, StringComparison.Ordinal);

        // The unconditional claims about the unmeasured case, gone.
        Assert.DoesNotContain("are refused (`ERROR: Access is denied.`", page, StringComparison.Ordinal);
        Assert.DoesNotContain("and so is the first step of `jason uninstall`", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading the registration needs nothing, and reads this account's task only: <c>status</c> never asks after
    /// the name 0.1.0 registered, so the page says which verb does.
    /// </summary>
    [Fact]
    public void The_page_says_that_reading_the_registration_needs_nothing()
    {
        var page = Read().ReplaceLineEndings(" ");

        Assert.Contains("`status` reads this account's task from an ordinary prompt", page, StringComparison.Ordinal);
        Assert.Contains("it does not ask after `\\Jason`, so after an upgrade from 0.1.0 run `enable` or `disable` once", page, StringComparison.Ordinal);
        Assert.DoesNotContain("reads either kind", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The row that says what Windows registers names the task as the code names it. The name appears elsewhere on the
    /// page too, so only the row itself can hold the row: a row reverted to the old single name stayed green before.
    /// </summary>
    [Fact]
    public void The_windows_row_names_the_task_as_the_code_names_it()
    {
        var row = Assert.Single(Read().Split('\n'), line => line.TrimStart().StartsWith("| Windows |", StringComparison.Ordinal));

        Assert.Contains($"a logon task called `{AutostartArtifacts.TaskName("<SID>")}`, one per account", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every refusal names its task and its case, and the page says so for all three, not only for the task 0.1.0
    /// left: this account's own task out of this prompt's reach, and a machine that will not register one.
    /// </summary>
    [Fact]
    public void The_page_names_every_case_a_refusal_can_be()
    {
        var page = Read().ReplaceLineEndings(" ");

        Assert.Contains("Every refusal names the task it is about and its case", page, StringComparison.Ordinal);
        Assert.Contains("the task named for this account, `Jason-<SID>`, out of this prompt's reach", page, StringComparison.Ordinal);

        // Named, not owned: a task this prompt could not read cannot be shown to be this account's, and its denial never
        // calls it that. The page's list of cases does not either.
        Assert.DoesNotContain("this account's own task, `Jason-<SID>`, out of this prompt's reach", page, StringComparison.Ordinal);
        Assert.Contains("a machine that will not register a logon task for this account", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// What taking an installation off the machine removes and never touches, on the page and in the install guide,
    /// as the verb's own help says it: this account's registration, the task 0.1.0 left included, and never another
    /// account's.
    /// </summary>
    [Fact]
    public void The_pages_say_whose_logon_registration_uninstall_removes_as_its_help_does()
    {
        var page = Read().ReplaceLineEndings(" ");
        var install = Source("docs/INSTALL.md").ReplaceLineEndings(" ");

        Assert.Contains("| this account's logon registration, on Windows including the task `\\Jason` an earlier version registered for it |", page, StringComparison.Ordinal);
        Assert.Contains("a PATH entry the installer did not write; another account's logon registration; the notifications already in the notification center", page, StringComparison.Ordinal);
        Assert.Contains("this account's logon registration first — on Windows including the task `\\Jason` an earlier version registered for it", install, StringComparison.Ordinal);
        Assert.Contains("no PATH entry the installer did not write, no other account's logon registration, and none of the notifications already in the notification center.", install, StringComparison.Ordinal);
    }

    /// <summary>
    /// One task per account, said where a person reading about a shared machine would look: the task's name is the
    /// account's, and another account's is out of reach. A page that said one registration per account while the
    /// code registered one per machine is how this was found.
    /// </summary>
    [Fact]
    public void The_page_says_each_account_has_its_own_task()
    {
        var page = Read().ReplaceLineEndings(" ");

        Assert.Contains("every account on it has its own task there", page, StringComparison.Ordinal);
        Assert.Contains("another account's ordinary prompt can neither read nor change it", page, StringComparison.Ordinal);
        Assert.Contains("each registers their own", Source("docs/INSTALL.md").ReplaceLineEndings(" "), StringComparison.Ordinal);
        Assert.Contains("one task per account", Source("docs/handbook.md").ReplaceLineEndings(" "), StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing sends anybody to elevate for autostart any more: not the page, not the install guide, not the
    /// README, and not the skill an agent reads before it acts. A copy that still did would have somebody on a
    /// standard account handed a command they cannot run, for something they can do from the prompt they have.
    /// </summary>
    [Fact]
    public void Nothing_sends_anybody_to_elevate_for_autostart()
    {
        string[] places =
        [
            "README.md",
            "docs/handbook.md",
            "docs/INSTALL.md",
            "docs/release-and-update.md",
            "skills/runtime/operating-the-installation/SKILL.md",
        ];

        foreach (var place in places)
        {
            var text = Source(place).ReplaceLineEndings(" ");
            Assert.DoesNotContain("have to be run from an elevated prompt", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("must be run from an elevated prompt", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unsupported in this version", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SeBatchLogonRight", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// What the guard over child processes holds is exactly this: no <c>ProcessStartInfo</c> made outside the door,
    /// no write to <c>CreateNoWindow</c> or <c>UseShellExecute</c>, no <c>Process.Start</c> from a file name and no
    /// read of a process's <c>StartInfo</c>. The flag it protects keeps a console child from getting a console
    /// window of its own; it says nothing about a graphical program's windows or a grandchild's. The words say no
    /// more than that, wherever they are said.
    /// </summary>
    [Fact]
    public void The_words_about_a_child_s_window_claim_what_the_guard_holds()
    {
        // As words: a list item's continuation lines are indented, and where it wraps decides nothing.
        var instructions = string.Join(' ', Source("CLAUDE.md").Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("refuses any other way to start one", instructions, StringComparison.Ordinal);
        Assert.Contains("with no console window of its own and no shell", instructions, StringComparison.Ordinal);
        Assert.Contains("Reflection, expression trees and `new T()` are beyond what it reads", instructions, StringComparison.Ordinal);
        foreach (var shape in (string[])["a `ProcessStartInfo` made anywhere else", "`CreateNoWindow`", "`UseShellExecute`", "`Process.Start` from a file name", "a process's `StartInfo`"])
        {
            Assert.Contains(shape, instructions, StringComparison.Ordinal);
        }

        Assert.Contains("started with no console window of its own", Read().ReplaceLineEndings(" "), StringComparison.Ordinal);
        Assert.Contains(
            "No child this product starts gets a console window of its own",
            Source("dotnet/tests/Jason.App.Tests/Processes/NoChildGetsAWindowTests.cs").ReplaceLineEndings(" "),
            StringComparison.Ordinal);

        // And the register names the door, with what holds it: a guard over compiled IL, whose limits CLAUDE.md
        // states above.
        var register = Source("docs/architecture.md");
        Assert.Contains(
            "| INV-PROC-001 | Every child process the product starts is made and started at one door, `ChildProcess.StartInfo` and `ChildProcess.Start`, on Windows under the start's error mode, with no shell and no console window of its own, held by a guard over the compiled IL of the production assemblies. |",
            register,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The sentence that said nothing about autostart is ever registered lived in three places, and all three
    /// had to stop saying it on the day the verbs arrived. A guarantee kept in three copies is a guarantee that
    /// goes stale in two of them. The commands moved from CLAUDE.md to DEVELOPMENT.md, and the copy with them.
    /// </summary>
    [Fact]
    public void Nothing_claims_that_autostart_is_never_registered()
    {
        string[] places =
        [
            "docs/handbook.md",
            "DEVELOPMENT.md",
            "dotnet/src/Jason.Cli/Commands/RuntimeStartCommand.cs",
        ];

        foreach (var place in places)
        {
            var text = Source(place);
            Assert.DoesNotContain("Nothing about autostart is registered", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("no autostart registered", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("until you ask", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The one thing a fresh installation sends on its own is said where the person installs it: when, what, and the
    /// switch — each read from the code that decides it, so a changed default fails here rather than on a page.
    /// </summary>
    [Fact]
    public void The_install_page_says_beside_the_install_step_what_a_started_runtime_asks_and_how_to_turn_it_off()
    {
        var defaults = new UpdateOptions();
        var section = Flat(Section(Source("docs/INSTALL.md"), "## 2. Install from a release"));
        var minutes = defaults.InitialDelayMinutes switch { 5 => "five minutes", var other => $"{other} minutes" };
        var interval = defaults.IntervalHours switch { 24 => "once a day", var other => $"every {other} hours" };

        Assert.True(defaults.CheckEnabled);
        Assert.Contains($"asks the release feed whether a newer version exists {minutes} after it starts, and {interval} after that", section, StringComparison.Ordinal);
        Assert.Contains("one `GET`, carrying nothing about you or your work", section, StringComparison.Ordinal);
        Assert.Contains($"`{UpdateOptions.Section}:{nameof(UpdateOptions.CheckEnabled)}` set to `false`", section, StringComparison.Ordinal);
        Assert.Contains("it identifies only the runtime's version, including its build", section, StringComparison.Ordinal);
        Assert.DoesNotContain("release-and-update.md", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// After an upgrade from 0.1.0, `enable` or `disable` once takes the old task away and stops nothing: the runtime
    /// that task started outlives a sign-out and holds the data directory, and the runtime this version starts at
    /// sign-in then finds the directory taken and exits before it can say so. The page says to stop it, in the same
    /// paragraph that says to run `enable` or `disable` once.
    /// </summary>
    [Fact]
    public void After_an_upgrade_from_0_1_0_the_page_says_to_stop_the_runtime_its_task_started()
    {
        var paragraph = Flat(Assert.Single(Paragraphs(), p => Flat(p).Contains("after an upgrade from 0.1.0 run `enable` or `disable` once", StringComparison.Ordinal)));

        Assert.Contains("If that answers `ERROR: Access is denied.`, do what the paragraph above says first.", paragraph, StringComparison.Ordinal);
        Assert.Contains("And stop the runtime 0.1.0's task started, once: `jason runtime stop`, or restart the machine. Then start one: `jason runtime start`, or sign in again with autostart enabled.", paragraph, StringComparison.Ordinal);
        var enable = paragraph.IndexOf("run `enable` or `disable` once", StringComparison.Ordinal);
        var denied = paragraph.IndexOf("Access is denied.`, do what", StringComparison.Ordinal);
        var stop = paragraph.IndexOf("`jason runtime stop`, or restart", StringComparison.Ordinal);
        var start = paragraph.IndexOf("`jason runtime start`, or sign in again", StringComparison.Ordinal);
        Assert.True(enable >= 0 && denied > enable && stop > denied && start > stop, $"the acts are not named in order: {paragraph}");
        Assert.Contains("While it holds the data directory, the task this version registers starts a runtime at every sign-in that finds the directory taken and exits without a word.", paragraph, StringComparison.Ordinal);
    }

    /// <summary>
    /// Installing through a package manager is the third way in, and the page says who then updates the installation and
    /// who takes it off: the manager, with its own commands, because it keeps its own record of what it installed.
    /// </summary>
    [Fact]
    public void The_page_says_a_package_manager_updates_and_takes_off_what_it_installed()
    {
        var installing = Flat(Section(Read(), "## 6. Installing"));

        Assert.Contains("### Through a package manager", Section(Read(), "## 6. Installing"), StringComparison.Ordinal);
        foreach (var command in new[] { "`brew upgrade jason`", "`brew uninstall jason`", "`winget upgrade Reply.Jason`", "`winget uninstall Reply.Jason`" })
        {
            Assert.Contains(command, installing, StringComparison.Ordinal);
        }

        Assert.Contains("keeps its own record", installing, StringComparison.Ordinal);

        // The table names each upgrade bare, so the sentence that leads into it says how it is run.
        Assert.Contains("updates the installation, with the runtime stopped first and started after (§7)", installing, StringComparison.Ordinal);
    }

    /// <summary>
    /// Why an update under a manager is three lines and never one: the manager replaces the image the running runtime
    /// starts its plugin hosts from, and knows nothing of that runtime.
    /// </summary>
    [Fact]
    public void The_page_says_why_the_runtime_is_stopped_around_a_managers_upgrade()
    {
        var updating = Flat(Section(Read(), "## 7. Updating"));

        Assert.Contains("refuses an installation a package manager owns", updating, StringComparison.Ordinal);
        Assert.Contains("plugin host", updating, StringComparison.Ordinal);
        var stop = updating.IndexOf("jason runtime stop", StringComparison.Ordinal);
        var upgrade = updating.IndexOf("brew upgrade jason", StringComparison.Ordinal);
        var start = updating.IndexOf("jason runtime start", upgrade + 1, StringComparison.Ordinal);
        Assert.True(stop >= 0 && upgrade > stop && start > upgrade, "the page does not give the stop, the manager's upgrade and the start in that order.");
        Assert.Contains("jason runtime restart", updating, StringComparison.Ordinal);

        // What winget does under a running runtime, and with it stopped, as a real install of the release showed it.
        Assert.Contains("winget cannot remove the executable a running runtime holds: its upgrade fails and leaves the old version in place", updating, StringComparison.Ordinal);
        Assert.Contains("With the runtime stopped, the same upgrade replaces it and keeps the uninstall entry, its install location and the package directory as they were", updating, StringComparison.Ordinal);
    }

    /// <summary>
    /// The answer to <c>jason update check</c> names one command, and the page says why a person is printed three lines
    /// around it under a manager: the reason a real install of the release showed.
    /// </summary>
    [Fact]
    public void The_page_says_why_update_check_prints_three_lines_under_a_manager()
    {
        var asking = Flat(Section(Read(), "## 5. Asking by hand"));

        Assert.Contains("the field stays one command", asking, StringComparison.Ordinal);
        Assert.Contains("winget cannot replace the executable a running runtime holds", asking, StringComparison.Ordinal);
    }

    /// <summary>What <c>jason uninstall</c> leaves to the manager, and the command that takes it off.</summary>
    [Fact]
    public void The_page_says_what_an_uninstall_leaves_to_a_package_manager()
    {
        var uninstalling = Flat(Section(Read(), "## 9. Taking an installation off the machine"));

        Assert.Contains("left to the package manager", uninstalling, StringComparison.Ordinal);
        Assert.Contains("`brew uninstall jason`", uninstalling, StringComparison.Ordinal);
        Assert.Contains("`winget uninstall Reply.Jason`", uninstalling, StringComparison.Ordinal);
    }

    /// <summary>The architecture's register of accepted decisions records whose an installation is.</summary>
    [Fact]
    public void The_decision_register_records_whose_an_installation_is()
    {
        var architecture = Source("docs/architecture.md");
        var start = architecture.IndexOf("\n## 18. Accepted decision register", StringComparison.Ordinal);
        var end = architecture.IndexOf("\n## 19. ", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "docs/architecture.md no longer has §18 followed by §19.");

        Assert.Contains(
            "| DEC-UPDATE-003 | An installation is Jason's own unless a package manager's own record says otherwise; a managed installation is updated and taken off by its manager, and Jason's verbs act only on what is Jason's (autostart, skills by receipt, data); and a status check says when the running runtime is not the build installed. |",
            architecture[start..end],
            StringComparison.Ordinal);
    }

    /// <summary>A page's text from a heading to the next heading of the same level, or to its end.</summary>
    private static string Section(string page, string heading)
    {
        var start = page.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the page has no heading '{heading}'.");

        var next = page.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0 ? page[start..] : page[start..next];
    }

    /// <summary>A text with every run of white space made one space, so a sentence is found however it is wrapped.</summary>
    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

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
