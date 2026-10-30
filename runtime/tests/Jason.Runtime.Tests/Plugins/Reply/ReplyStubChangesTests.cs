using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's <c>inbox changes</c>, answered as reply-cli 0.17.0 documents it over Reply's change feed: the threads that
/// moved, oldest first by their last activity, a page at a time from a cursor or from a moment, within the last 30 days.
/// An empty page still hands back a cursor ahead of the one it was given, a cursor or a moment older than the window and a
/// cursor Reply never issued are each refused under a code of their own, a deleted thread is not in the feed at all, and an account whose Reply has no feed yet
/// answers the bare 404 of a route Reply's API does not have.
/// </summary>
public class ReplyStubChangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task The_feed_pages_oldest_first_and_resumes_from_the_cursor_it_handed_back()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(503, Ago(1)))
            .WithThread(ReplyConversationListTests.Thread(501, Ago(3), sequence: 7))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(2), contactId: 1002, inbound: false));

        var (exit, stdout, _) = await account.RunAsync(Line("inbox", "changes", "--top", "2"), Ct);

        Assert.Equal(0, exit);
        var page = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal([501, 502], Threads(page));
        Assert.True((bool)page["has_more"]!);
        var first = page["items"]![0]!.AsObject();
        Assert.Equal(
            (1001, "email", 7, false, true, false),
            ((int)first["contactId"]!, (string?)first["channel"], (int)first["sequenceId"]!, (bool)first["hasMeetingIntent"]!,
                (bool)first["flags"]!["hasReply"]!, (bool)first["lastMessage"]!["isOutbound"]!));
        Assert.True((bool)page["items"]![1]!["lastMessage"]!["isOutbound"]!);
        Assert.DoesNotContain("subject", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("Marta", stdout, StringComparison.Ordinal);

        var (next, rest, _) = await account.RunAsync(Line("inbox", "changes", "--cursor", (string)page["cursor"]!), Ct);

        Assert.Equal(0, next);
        var after = JsonNode.Parse(rest)!.AsObject();
        Assert.Equal([503], Threads(after));
        Assert.False((bool)after["has_more"]!);
    }

    [Fact]
    public async Task An_empty_page_still_hands_back_a_cursor_ahead_of_the_one_it_was_given()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(3)));
        var (_, stdout, _) = await account.RunAsync(Line("inbox", "changes"), Ct);
        var given = (string)JsonNode.Parse(stdout)!["cursor"]!;

        var (exit, empty, _) = await account.RunAsync(Line("inbox", "changes", "--cursor", given), Ct);

        Assert.Equal(0, exit);
        var page = JsonNode.Parse(empty)!.AsObject();
        Assert.Empty(page["items"]!.AsArray());
        Assert.False((bool)page["has_more"]!);
        Assert.NotEqual(given, (string)page["cursor"]!);
    }

    [Fact]
    public async Task A_moment_reads_the_threads_that_moved_at_or_after_it_and_needs_an_offset()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, Ago(3)))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(2)));

        var (exit, stdout, _) = await account.RunAsync(Line("inbox", "changes", "--since", Ago(2)), Ct);
        Assert.Equal(0, exit);
        Assert.Equal([502], Threads(JsonNode.Parse(stdout)!.AsObject()));

        var zoneless = Ago(2).TrimEnd('Z');
        var (usage, _, said) = await account.RunAsync(Line("inbox", "changes", "--since", zoneless), Ct);
        Assert.Equal((2, "usage.date"), (usage, (string?)Error(said)["code"]));
    }

    [Theory]
    [InlineData("--since")]
    [InlineData("--cursor")]
    public async Task A_cursor_or_a_moment_older_than_the_window_is_refused_as_expired(string by)
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(3)));
        var aged = Now.AddDays(-31);
        var value = by == "--since"
            ? aged.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : Cursor(aged, 501);

        var (exit, stdout, stderr) = await account.RunAsync(Line("inbox", "changes", by, value), Ct);

        Assert.Equal((1, string.Empty), (exit, stdout.Trim()));
        var error = Error(stderr);
        Assert.Equal(
            ("inbox.cursor_expired", 400, "inboxThread.changeWindowExpired"),
            ((string?)error["code"], (int)error["status"]!, (string?)error["reply_code"]));
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("ahead")]
    public async Task A_cursor_reply_never_issued_or_one_ahead_of_its_clock_is_refused(string given)
    {
        using var account = new ReplyAccount();
        var cursor = given == "ahead" ? Cursor(Now.AddHours(1), 501) : given;

        var (exit, _, stderr) = await account.RunAsync(Line("inbox", "changes", "--cursor", cursor), Ct);

        Assert.Equal(1, exit);
        var error = Error(stderr);
        Assert.Equal(("inbox.cursor_refused", 400, null), ((string?)error["code"], (int)error["status"]!, (string?)error["reply_code"]));
    }

    [Fact]
    public async Task A_deleted_thread_is_not_in_the_feed()
    {
        var deleted = ReplyConversationListTests.Thread(502, Ago(2));
        deleted["_deleted"] = true;
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(3))).WithThread(deleted);

        var (_, stdout, _) = await account.RunAsync(Line("inbox", "changes"), Ct);

        Assert.Equal([501], Threads(JsonNode.Parse(stdout)!.AsObject()));
    }

    [Fact]
    public async Task A_feed_told_to_hand_back_no_cursor_hands_back_an_empty_one()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(3))).WithEmptyFeedCursors();

        var (exit, stdout, _) = await account.RunAsync(Line("inbox", "changes"), Ct);

        Assert.Equal(0, exit);
        var page = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal([501], Threads(page));
        Assert.Equal(string.Empty, (string?)page["cursor"]);
    }

    [Fact]
    public async Task A_feed_the_account_s_reply_does_not_offer_yet_is_a_bare_404()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(3))).NotOffering("inbox_changes");

        var (exit, _, stderr) = await account.RunAsync(Line("inbox", "changes"), Ct);

        Assert.Equal(1, exit);
        var error = Error(stderr);
        Assert.Equal(("reply.refused", 404, null), ((string?)error["code"], (int)error["status"]!, (string?)error["reply_code"]));
        Assert.Contains("GET /v3/inbox/threads/changes", (string?)error["detail"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--top", "0", "usage.range")]
    [InlineData("--top", "1001", "usage.range")]
    [InlineData("--both", "", "usage.inbox")]
    public async Task A_line_the_cli_refuses_is_a_usage_error(string option, string value, string code)
    {
        using var account = new ReplyAccount();
        string[] line = option == "--both"
            ? Line("inbox", "changes", "--cursor", Cursor(Now, 1), "--since", Ago(1))
            : Line("inbox", "changes", option, value);

        var (exit, _, stderr) = await account.RunAsync(line, Ct);

        Assert.Equal((2, code), (exit, (string?)Error(stderr)["code"]));
    }

    /// <summary>A moment some minutes ago, as Reply writes a thread's last activity.</summary>
    private static string Ago(int minutes) =>
        Now.AddMinutes(-minutes).ToString("yyyy-MM-dd'T'HH:mm:ss.ff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The stand-in's own cursor for a position: the moment and the thread a page ended at.</summary>
    internal static string Cursor(DateTimeOffset at, int thread) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes(
            at.UtcTicks.ToString(CultureInfo.InvariantCulture) + "." + thread.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static int[] Threads(JsonObject page) => [.. page["items"]!.AsArray().Select(item => (int)item!["threadId"]!)];

    private static string[] Line(params string[] command) => ["--json", "-q", .. command];

    private static JsonObject Error(string stderr) =>
        JsonNode.Parse(stderr.Trim().Split('\n')[^1])!["error"]!.AsObject();
}
