namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_analyst_is_told_that_this_build_records_no_metrics()
    {
        var skill = Flattened("analyst");

        // The role whose job description implies a dashboard. An analyst that assumed opens and clicks were
        // somewhere would either hunt for them or, worse, report plausible ones.
        Assert.Contains("There is no metrics store here — the provider reports, and the envelope says what the number is.", skill, StringComparison.Ordinal);

        // And the one distinction that keeps its answer honest: a report is somebody's word, not a measurement.
        Assert.Contains("an admission, not a measurement", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("analyst");
    }

    /// <summary>
    /// The analyst counts replies by category over a window from the verdict lines, and says what that count is. The
    /// listing takes a start and no end, the line names the reply by the fields the runtime writes, and the latest line of
    /// a reply stands, so a count that kept every line, or ran past the window's end, would be a wrong figure said plainly.
    /// </summary>
    [Fact]
    public void The_analyst_counts_replies_by_category_over_a_window_from_the_verdict_lines()
    {
        var skill = Flattened("analyst");

        Assert.Contains("**Replies by category are the responder's reading, and you count them from the chronicle.**", skill, StringComparison.Ordinal);
        Assert.Contains("a `reply_classified` line whose `new` names the reply (`plugin`, `thread`, `message_id`) and its `category`", skill, StringComparison.Ordinal);
        Assert.Contains("drop any whose `ts` falls after its end, because the list takes a start and no end", skill, StringComparison.Ordinal);
        Assert.Contains("keep the latest line of each reply, because a reply can be recorded again and the latest stands", skill, StringComparison.Ordinal);
        Assert.Contains("the replies the responder gave a verdict, by the category it gave, and never the provider's own count of replies", skill, StringComparison.Ordinal);
        Assert.Contains("The base categories are the responder's `categories.json`, which is not copied here", skill, StringComparison.Ordinal);
        Assert.Contains(
            "jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind reply_classified --since 2026-10-09T00:00:00Z",
            SkillPack.PrintedCommands(SkillPack.Find("analyst").File));

        // The facts the instruction rests on are the runtime's: the listing has a start and no end, and the line is named by
        // those fields in the contract, under the kind the runtime writes.
        var request = typeof(Jason.Contracts.Api.JournalListRequest);
        Assert.NotNull(request.GetProperty("Since"));
        Assert.Null(request.GetProperty("Until"));
        Assert.Equal("reply_classified", Jason.Runtime.Journal.JournalKinds.ReplyClassified);
        var api = File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "docs", "runtime-api.md")).ReplaceLineEndings(" ");
        Assert.Contains("The line names the reply as `plugin`, `thread`, `message_id` and `message_at`", System.Text.RegularExpressions.Regex.Replace(api, @"\s+", " "), StringComparison.Ordinal);
        Assert.Contains("the latest for a reply stands", System.Text.RegularExpressions.Regex.Replace(api, @"\s+", " "), StringComparison.Ordinal);
    }
}
