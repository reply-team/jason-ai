using System.Text.RegularExpressions;
using Jason.Contracts.Execution;
using Jason.Runtime.Configuration;
using Jason.Runtime.Decisions;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The manager loop's page, held to the few claims a reader will act on: what the word "check-in" means, which
/// kinds wake a manager, what the dispatcher does not read, and what a cadence costs. A page that drifted from
/// any of those would be teaching an operator to expect a loop they do not have.
/// </summary>
public partial class CampaignManagerDocTests
{
    /// <summary>
    /// A review asked for: the page says what it makes, what it does to the two clocks, what its line is, and each code it is
    /// refused with, in the order the service refuses them, each code read from the runtime's own error.
    /// </summary>
    [Fact]
    public void The_page_says_what_a_review_asked_for_makes_and_where_it_is_refused_in_order()
    {
        var page = Flattened(Read());

        Assert.Contains("**A review asked for.**", page, StringComparison.Ordinal);
        Assert.Contains("`jason campaign review <id>`", page, StringComparison.Ordinal);
        Assert.Contains("its brief says `review_intent: \"requested\"` and names no trigger and no cause", page, StringComparison.Ordinal);
        Assert.Contains("a review asked for moves the quick anchor, as every check-in does", page, StringComparison.Ordinal);
        Assert.Contains("a deep review asked for leaves the grid where it was, so the daily deep review still comes at its moment", page, StringComparison.Ordinal);
        Assert.Contains($"The request writes one `{JournalKinds.ReviewRequested}` line", page, StringComparison.Ordinal);
        Assert.Contains("So `review_intent` is one of three values, `scheduled`, `triggered` and `requested`.", page, StringComparison.Ordinal);

        string[] order =
        [
            Jason.Runtime.Domain.DomainErrors.ReviewInsideARun("att_x").Code,
            Jason.Runtime.Domain.DomainErrors.ReviewActorRequired().Code,
            Jason.Runtime.Domain.DomainErrors.ReviewNotHuman(Jason.Contracts.Api.ActorType.Role).Code,
            Jason.Runtime.Domain.DomainErrors.CampaignNotFound("cmp_x").Code,
            Jason.Runtime.Domain.DomainErrors.CampaignNotActive("cmp_x", Jason.Contracts.Api.CampaignStatus.Paused).Code,
            Jason.Runtime.Domain.DomainErrors.ReviewAlreadyOpen("cmp_x", "wi_x").Code,
            Jason.Runtime.Domain.DomainErrors.ReviewLaunchesSpent("cmp_x", 1, "2026-10-06T15:00:00Z", "UTC").Code,
        ];
        var section = page[page.IndexOf("**A review asked for.**", StringComparison.Ordinal)..];
        var at = order.Select(code => section.IndexOf($"`{code}`", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, at);
        Assert.Equal(at.Order(), at);
        Assert.Contains(Jason.Runtime.Notifications.NotificationComposer.LiftHold, page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manager_contract_defines_the_check_in_and_prices_the_cadence()
    {
        var page = Flattened(Read());

        // One word for the work the runtime creates, defined where somebody meets it first.
        Assert.Contains("A check-in is the work item the runtime creates to have a campaign reviewed", page, StringComparison.Ordinal);

        // The kinds this version reacts to, named on the page and not only in the code.
        Assert.Contains(JournalKinds.WorkItemFailed, page, StringComparison.Ordinal);
        Assert.Contains(JournalKinds.ApprovalRejected, page, StringComparison.Ordinal);
        Assert.Contains(JournalKinds.ExternalEffectReported, page, StringComparison.Ordinal);
        Assert.Contains(JournalKinds.DecisionAnswered, page, StringComparison.Ordinal);

        // The boundary the whole design rests on.
        Assert.Contains("The dispatcher reads no meaning", page, StringComparison.Ordinal);

        // What a number somebody will type actually costs them, said in launches rather than in seconds.
        Assert.Contains("One launch per active campaign per interval", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reserve's default is set by the runtime at the first start, so the page says when, for which campaigns, by whom
    /// and how a person has none; a page that still said a campaign has none until one is set would teach a reader that a
    /// null before the start means none.
    /// </summary>
    [Fact]
    public void The_reserve_page_says_the_default_the_first_start_sets_and_how_to_have_none()
    {
        var page = Flattened(Read());

        Assert.DoesNotContain("A campaign has none until one is set", page, StringComparison.Ordinal);
        Assert.Contains("When it first goes live, `campaign.start` from `draft`, holding none, and its people come from a search that can top them up, the runtime gives it `Manager:AudienceThreshold`, twenty by default", page, StringComparison.Ordinal);
        Assert.Contains("A campaign whose people the person brought as a list holds neither and is given nothing", page, StringComparison.Ordinal);
        Assert.Contains("by `system:runtime`, whose reason says it is the default at the first start", page, StringComparison.Ordinal);
        Assert.Contains("A resume never does, and a campaign that was already live when its installation was upgraded is never given one.", page, StringComparison.Ordinal);
        Assert.Contains("So a person who wants none clears it after the start, `--clear audience_threshold`, and it stays cleared through every pause and resume.", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The settings table is the part an operator edits, so the numbers on the page have to be the numbers the
    /// runtime holds them to. A default that drifts here is a page that lies about what happens by itself.
    /// </summary>
    [Fact]
    public void The_settings_the_page_publishes_are_the_ones_the_runtime_enforces()
    {
        var page = Flattened(Read());
        var defaults = new ManagerOptions();

        var cadence = $"{ManagerOptions.MinimumReviewSeconds}..{ManagerOptions.MaximumReviewSeconds}";
        Assert.Contains($"`Manager:ReviewSeconds` | `{defaults.ReviewSeconds}` | {cadence}", page, StringComparison.Ordinal);
        Assert.Contains($"`Manager:DeepReviewSeconds` | `{defaults.DeepReviewSeconds}` | {cadence}", page, StringComparison.Ordinal);
        Assert.Contains($"`Manager:TimeoutSeconds` | `{defaults.TimeoutSeconds}` | 30..86400", page, StringComparison.Ordinal);
        Assert.Contains($"`Manager:MaxAttempts` | `{defaults.MaxAttempts}` | 1..10", page, StringComparison.Ordinal);
        Assert.Contains($"`Manager:Priority` | `{defaults.Priority}` | -1000..1000", page, StringComparison.Ordinal);
        Assert.Contains($"`Manager:MaxEntriesPerScan` | `{defaults.MaxEntriesPerScan}` | 50..10000", page, StringComparison.Ordinal);
        Assert.Contains(
            $"`Manager:AudienceThreshold` | `{defaults.AudienceThreshold}` | {ReserveCounter.MinimumThreshold}..{ReserveCounter.MaximumThreshold}",
            page,
            StringComparison.Ordinal);

        // And every kind the defaults hold is one the page publishes: a row of the table of what wakes a manager, and
        // the default of the setting, in the order the runtime holds them.
        foreach (var kind in ManagerOptions.Default)
        {
            Assert.Contains($"| `{kind}` |", page, StringComparison.Ordinal);
        }

        Assert.Contains(
            $"`Manager:Triggers` | {string.Join(", ", ManagerOptions.Default.Select(kind => $"`{kind}`"))} |",
            page,
            StringComparison.Ordinal);
        Assert.Contains("There is no `Manager:Enabled`", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two clocks' pace as the page says it, and what they cost a day, printed from the defaults: a quick look per
    /// interval since the last review, a deep review per grid interval in place of the quick look that would have fallen on
    /// it, so at the defaults a day is three quick looks and one deep review.
    /// </summary>
    [Fact]
    public void The_page_states_the_two_clocks_and_what_a_day_of_them_costs()
    {
        var page = Flattened(Read());
        var defaults = new ManagerOptions();

        Assert.Equal(6, defaults.ReviewSeconds / 3_600);
        Assert.Equal(0, defaults.DeepReviewSeconds % defaults.ReviewSeconds);
        Assert.Contains("six hours by default", page, StringComparison.Ordinal);
        Assert.Contains("a day by default", page, StringComparison.Ordinal);
        Assert.Contains("after a resume it starts again", page, StringComparison.Ordinal);
        Assert.Contains("a runtime that was down for three days runs one deep review, not three", page, StringComparison.Ordinal);
        Assert.Contains("Where both clocks are due, the deep review runs instead of the quick look", page, StringComparison.Ordinal);
        Assert.Contains("a deep review, even when the chronicle read a cause. It carries that cause in its brief as a triggered review would", page, StringComparison.Ordinal);
        Assert.Contains("a day at the defaults is three quick looks and one deep review", page, StringComparison.Ordinal);

        const int Day = 86_400;
        var quick = (defaults.DeepReviewSeconds / defaults.ReviewSeconds) - 1;
        Assert.Contains(
            $"| the defaults, {defaults.ReviewSeconds} and {defaults.DeepReviewSeconds} | {quick} | 1 | {quick + 1} | {10 * (quick + 1)} |",
            page,
            StringComparison.Ordinal);
        Assert.Equal(40, 10 * (quick + 1));
        Assert.Contains("about forty launches a day", page, StringComparison.Ordinal);

        var floor = ManagerOptions.MinimumReviewSeconds;
        Assert.Contains($"| the quick look at the floor, {floor} | {(Day / floor) - 1} | 1 | {Day / floor} | {10 * Day / floor} |", page, StringComparison.Ordinal);
        Assert.Contains($"| the deep review at the floor, {floor} | 0 | {Day / floor} | {Day / floor} | {10 * Day / floor} |", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the shape a review answers in, which is the one thing a role's own skill will be written around.
    /// </summary>
    [Fact]
    public void The_page_prints_the_shape_a_review_answers_in()
    {
        var page = Flattened(Read());

        // Read out of the shape itself rather than listed here, so a field added to what a review may answer
        // is a field the page has to print. A list written twice is a list that drifts.
        var fields = ManagerCheckIn.ResultFormat.AsObject()["properties"]!.AsObject().Select(property => property.Key).ToList();

        Assert.NotEmpty(fields);
        foreach (var field in fields)
        {
            Assert.Contains(field, page, StringComparison.Ordinal);
        }

        Assert.Contains("acted | escalated | nothing", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a question and its answer may be, printed from the same constants the service refuses by. A bound
    /// that moved in code and not on the page would be a refusal nobody was warned about.
    /// </summary>
    [Fact]
    public void The_bounds_the_page_publishes_are_the_ones_a_question_is_held_to()
    {
        var page = Flattened(Read());

        Assert.Contains($"| {DecisionLimits.MaxQuestionLength} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxAnswerLength} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxOptions} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxOptionLabelLength} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxOptionDetailLength} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxReferences} |", page, StringComparison.Ordinal);

        // By its row and not by its number: 2000 is already on this page twice, so a bare bound would have
        // passed while the reason was undocumented — which is how a bound comes to live outside the type that
        // exists to hold them all.
        Assert.Contains(
            $"| the reason on either call, in characters | {DecisionLimits.MaxReasonLength} |",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every code escalation can answer with, one sentence each, the way the approvals section of the
    /// work-execution contract carries its own. A code a caller meets and cannot look up is a dead end.
    /// </summary>
    [Fact]
    public void The_page_names_every_code_a_question_can_be_refused_with()
    {
        var page = Flattened(Read());

        foreach (var code in new[]
        {
            "decision_not_found",
            "decision_not_pending",
            "decision_not_human",
            "decided_inside_a_run",
            "decision_option_unknown",
            "decision_reference_unresolved",
        })
        {
            Assert.Contains($"`{code}`", page, StringComparison.Ordinal);
        }

        // And the fence, which is not escalation's own code but is the one a role meets most often.
        Assert.Contains("`stale_attempt`", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three claims about the loop that a reader would otherwise have to infer from code: why a person's
    /// line is exempt and what that trust is worth, what a narrowed trigger list costs, and how the summon
    /// names a decision without reading one.
    /// </summary>
    [Fact]
    public void The_page_states_the_exemption_its_limit_and_what_a_narrowed_list_costs()
    {
        var page = Flattened(Read());

        Assert.Contains("A line whose actor is a **person** is never passed over", page, StringComparison.Ordinal);
        Assert.Contains("tell a person from a process holding that person's own command line", page, StringComparison.Ordinal);
        Assert.Contains($"A narrowed list has to keep `{JournalKinds.DecisionAnswered}`", page, StringComparison.Ordinal);
        Assert.Contains($"is refused, at a start and at a live edit, which keeps the list in force: \"{ManagerOptionsValidator.EmptyTriggers}\"", page, StringComparison.Ordinal);

        // How the cause names a decision: an identifier lookup, never a line's meaning.
        Assert.Contains("remembers the chronicle line its answer wrote", page, StringComparison.Ordinal);

        // And that raising is a fenced call, so nobody wonders why a lease moved.
        Assert.Contains("counts as a heartbeat", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// What this version cannot do, said on the page rather than discovered in a launch — and no longer
    /// claiming the two things that have since arrived. A "not here yet" list that outlives the work it
    /// described is worse than none: a reader believes it.
    /// </summary>
    [Fact]
    public void The_page_says_what_the_loop_does_not_do_yet()
    {
        var page = Flattened(Read());

        Assert.Contains("Notifications", page, StringComparison.Ordinal);
        Assert.Contains("Anomaly rules and reconciliation", page, StringComparison.Ordinal);

        Assert.DoesNotContain("nothing sets it in this version", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ships with escalation", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The table of entries §5 prints is the function the summon calls, row for row: each row's situation is read off
    /// the page, put to <see cref="ManagerEntryChoice.Choose"/>, and its answer held to the entry the row prints. A row
    /// added to the function and not the page, or the page and not the function, is red here.
    /// </summary>
    [Fact]
    public void The_entry_table_the_page_prints_is_the_function_the_summon_calls()
    {
        var rows = EntryRows(Read());

        Assert.Equal(6, rows.Count);
        var named = rows
            .Select(row => Backticked(row.Situation))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (situation, printed) in rows)
        {
            var expected = printed.StartsWith("none", StringComparison.Ordinal) ? null : Assert.IsType<string>(Backticked(printed));
            if (expected is not null)
            {
                Assert.True(RoleEntries.Carries(ManagerCheckIn.Role, expected), expected);
            }

            foreach (var inputs in Situations(situation, named))
            {
                var answered = ManagerEntryChoice.Choose(inputs);
                Assert.True(
                    expected == answered,
                    $"The page prints '{printed}' for '{situation}', and the function answers '{answered ?? "none"}' for {inputs}.");
            }
        }

        // And every name the function can give is a row's.
        var printedNames = rows.Select(row => Backticked(row.Entry)).OfType<string>().Order(StringComparer.Ordinal);
        Assert.Equal(
            [ManagerEntryChoice.DeepReview, ManagerEntryChoice.InboundReply, ManagerEntryChoice.QuickLook, ManagerEntryChoice.Resume, ManagerEntryChoice.Start],
            printedNames);
    }

    /// <summary>The rank §2 states, in the order the summon ranks by.</summary>
    [Fact]
    public void The_page_states_the_rank_a_burst_is_caused_by()
    {
        var page = Flattened(Read());

        Assert.True(ManagerTriggers.Rank(JournalKinds.CampaignStarted) < ManagerTriggers.Rank(JournalKinds.InboundReplyObserved));
        Assert.True(ManagerTriggers.Rank(JournalKinds.InboundReplyObserved) < ManagerTriggers.Rank(JournalKinds.WorkItemFailed));
        Assert.Equal(ManagerTriggers.Rank(JournalKinds.WorkItemFailed), ManagerTriggers.Rank(JournalKinds.DecisionAnswered));
        Assert.Contains(
            $"`{JournalKinds.CampaignStarted}` ranks first, then `{JournalKinds.InboundReplyObserved}`, then every other kind equally, where the first line wins",
            page,
            StringComparison.Ordinal);
        Assert.Contains("`qualifying_count` still counts every line that qualified", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a row's situation puts to the function. A row naming a kind is that kind, as the campaign's first such line
    /// where it says "first" and a later one where it says "later"; a row of "any other kind" is every default trigger no
    /// other row names; a row of no cause is the cadence come round; the row of the deep review due is every cause the
    /// defaults hold and none, as a first start and a later one, with the quick look due and not. Every other row is read
    /// with the deep review not due, as the page says.
    /// </summary>
    private static IEnumerable<ManagerEntryInputs> Situations(string situation, IReadOnlySet<string> named)
    {
        if (situation.StartsWith("the deep review is due", StringComparison.Ordinal))
        {
            return
                from read in ManagerOptions.Default.Select(kind => (string?)kind).Append(null)
                from firstStart in new[] { true, false }
                from quick in new[] { true, false }
                select new ManagerEntryInputs(read, firstStart, quick, DeepReviewDue: true);
        }

        if (situation.StartsWith("no cause", StringComparison.Ordinal))
        {
            Assert.Contains("is due", situation, StringComparison.Ordinal);
            return [new ManagerEntryInputs(CauseKind: null, FirstStart: false, ReviewDue: true)];
        }

        if (situation.Contains("any other kind", StringComparison.Ordinal))
        {
            var others = ManagerOptions.Default.Where(kind => !named.Contains(kind)).ToList();
            Assert.NotEmpty(others);
            return others.Select(kind => new ManagerEntryInputs(kind, FirstStart: false, ReviewDue: false));
        }

        var cause = Assert.IsType<string>(Backticked(situation));
        Assert.Contains(cause, ManagerOptions.Default);
        var first = situation.Contains("first", StringComparison.Ordinal);
        var later = situation.Contains("later", StringComparison.Ordinal);
        Assert.False(first && later, situation);
        if (cause == JournalKinds.CampaignStarted)
        {
            Assert.True(first || later, $"A row of {cause} says whether its line is the first or a later one: '{situation}'.");
        }

        return [new ManagerEntryInputs(cause, FirstStart: first, ReviewDue: false)];
    }

    /// <summary>The rows of the table under "| the summon has | entry |", as its two cells.</summary>
    private static List<(string Situation, string Entry)> EntryRows(string page)
    {
        var lines = page.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var header = lines.FindIndex(line => line == "| the summon has | entry |");
        Assert.True(header >= 0, "The page prints no table of entries.");
        Assert.Equal("|---|---|", lines[header + 1]);

        return lines
            .Skip(header + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Trim('|').Split('|'))
            .Select(cells => (cells[0].Trim(), cells[1].Trim()))
            .ToList();
    }

    /// <summary>The first text between backticks in a cell, or null where there is none.</summary>
    private static string? Backticked(string cell)
    {
        var match = Code().Match(cell);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    private static string Read() =>
        File.ReadAllText(Path.Combine(DocumentsDirectory(), "campaign-manager.md"));

    /// <summary>
    /// Read with its line breaks flattened. A published page wraps where the column runs out, so a guard that
    /// searched the raw text would be asserting where a paragraph happens to break rather than what it says.
    /// </summary>
    private static string Flattened(string text) => Whitespace().Replace(text, " ");

    private static string DocumentsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "docs");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
