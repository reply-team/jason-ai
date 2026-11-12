using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's Live Data and contact-list commands, answered as the reply-cli release
/// <see cref="Jason.ReplyCliStub.ReplyCliRelease"/> names documents them: a list made from a name on stdin, read, emptied
/// of nobody and deleted; a list's people a page at a time, oldest first; a search started on a list with the filters on
/// stdin, read, listed newest first and deleted; a preview of filters; and the values Reply suggests for a filter.
/// </summary>
public class ReplyStubLiveDataTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string CtoOnly = """{"jobTitles":[{"value":"CTO","type":"include"}]}""";

    [Fact]
    public async Task A_list_is_made_from_its_name_on_stdin_read_by_its_id_and_deleted()
    {
        using var account = new ReplyAccount();

        var (made, created, _) = await account.RunAsync(Line("contact-list", "create", "--name", "-"), """{"name":"Audience 12"}""", Ct);
        var id = (int)JsonNode.Parse(created)!["list_id"]!;
        var (read, got, _) = await account.RunAsync(Line("contact-list", "get", id.ToString(System.Globalization.CultureInfo.InvariantCulture)), Ct);
        var (deleted, gone, _) = await account.RunAsync(Line("contact-list", "delete", id.ToString(System.Globalization.CultureInfo.InvariantCulture)), Ct);
        var (again, _, said) = await account.RunAsync(Line("contact-list", "delete", id.ToString(System.Globalization.CultureInfo.InvariantCulture)), Ct);

        Assert.Equal((0, 0, 0, 1), (made, read, deleted, again));
        Assert.Equal($$"""{"status":"created","list_id":{{id}},"name":"Audience 12","is_shared":false}""", created.Trim());
        Assert.Equal($$"""{"id":{{id}},"name":"Audience 12","isShared":false}""", got.Trim());
        Assert.Equal($$"""{"status":"deleted","list_id":{{id}}}""", gone.Trim());
        Assert.Equal(("contact-list.not_found", 404), ((string?)Error(said)["code"], (int)Error(said)["status"]!));
        Assert.Empty(account.Lists);
    }

    [Fact]
    public async Task A_second_list_of_a_name_is_refused_as_reply_refuses_it()
    {
        using var account = new ReplyAccount().WithList(77, "Audience 12");

        var (exit, _, said) = await account.RunAsync(Line("contact-list", "create", "--name", "-"), """{"name":"Audience 12"}""", Ct);

        Assert.Equal(1, exit);
        Assert.Equal(("contact-list.name_taken", 409, "contactsList.duplicateName"), ((string?)Error(said)["code"], (int)Error(said)["status"]!, (string?)Error(said)["reply_code"]));
        Assert.Single(account.Lists);
    }

    /// <summary>
    /// A list's people, oldest first by the date each was added and then by the highest id, a page at a time; with
    /// <c>--live-data-only</c>, only the people Live Data created; and a list Reply does not hold reads as empty.
    /// </summary>
    [Fact]
    public async Task A_list_s_people_are_read_oldest_first_a_page_at_a_time()
    {
        using var account = new ReplyAccount()
            .WithContact(1001, "kept@example.com", "Kept")
            .WithList(77, "Audience 12", 1001)
            .WithSearch(901, "inProgress", 77)
            .Finds(901, ReplyAccount.Person(1002, "Lena", "Novak", email: "lena@example.com"), ReplyAccount.Person(1003, "Ivo", "Marek"));

        var (first, page, _) = await account.RunAsync(Line("contact-list", "contacts", "77", "--live-data-only", "--limit", "1", "--offset", "0"), Ct);
        var (_, next, _) = await account.RunAsync(Line("contact-list", "contacts", "77", "--live-data-only", "--limit", "1", "--offset", "1"), Ct);
        var (_, all, _) = await account.RunAsync(Line("contact-list", "contacts", "77"), Ct);
        var (_, none, _) = await account.RunAsync(Line("contact-list", "contacts", "78"), Ct);

        Assert.Equal(0, first);
        Assert.Equal((1002, true), ((int)JsonNode.Parse(page)!["items"]![0]!["id"]!, (bool)JsonNode.Parse(page)!["has_more"]!));
        Assert.Equal((1003, false), ((int)JsonNode.Parse(next)!["items"]![0]!["id"]!, (bool)JsonNode.Parse(next)!["has_more"]!));
        Assert.Equal([1001, 1002, 1003], JsonNode.Parse(all)!["items"]!.AsArray().Select(item => (int)item!["id"]!));
        Assert.Equal("""{"items":[],"has_more":false}""", none.Trim());
        Assert.Equal("Novak", (string?)JsonNode.Parse(page)!["items"]![0]!["lastName"]);
    }

    [Fact]
    public async Task A_search_starts_on_its_list_with_the_filters_on_stdin_and_is_read_and_deleted()
    {
        using var account = new ReplyAccount().WithList(77, "Audience 12");

        var (started, answer, _) = await account.RunAsync(
            Line("live-data", "start", "--filters", "-", "--count", "25", "--list-id", "77", "--max-per-company", "2", "--with-phones"), CtoOnly, Ct);
        var id = (int)JsonNode.Parse(answer)!["search_id"]!;
        var (read, search, _) = await account.RunAsync(Line("live-data", "get", Id(id)), Ct);
        var (listed, page, _) = await account.RunAsync(Line("live-data", "list", "--limit", "100"), Ct);
        var (deleted, gone, _) = await account.RunAsync(Line("live-data", "delete", Id(id)), Ct);
        var (again, _, said) = await account.RunAsync(Line("live-data", "get", Id(id)), Ct);

        Assert.Equal((0, 0, 0, 0, 1), (started, read, listed, deleted, again));
        Assert.Equal($$"""{"status":"started","search_id":{{id}}}""", answer.Trim());
        var held = JsonNode.Parse(search)!;
        Assert.Equal(("inProgress", 0), ((string?)held["status"], (int)held["contactsFound"]!));
        Assert.Equal("""[{"id":77,"name":"Audience 12"}]""", held["lists"]!.ToJsonString());
        Assert.Equal(CtoOnly, held["filters"]!.ToJsonString());
        Assert.DoesNotContain(held.AsObject(), member => member.Key.StartsWith('_'));
        var item = JsonNode.Parse(page)!["items"]![0]!.AsObject();
        Assert.Equal(id, (int)item["id"]!);
        Assert.False(item.ContainsKey("filters"));
        Assert.Equal($$"""{"status":"deleted","search_id":{{id}}}""", gone.Trim());
        Assert.Equal(("live_data.not_found", 404, id), ((string?)Error(said)["code"], (int)Error(said)["status"]!, (int)Error(said)["ids"]!["search_id"]!));
        Assert.Empty(account.Searches);
    }

    /// <summary>The team's searches, newest first, a page at a time.</summary>
    [Fact]
    public async Task Searches_are_listed_newest_first()
    {
        using var account = new ReplyAccount()
            .WithList(77, "Older").WithList(78, "Newer")
            .WithSearch(901, "finished", 77, startedAt: "2026-11-09T08:00:00Z")
            .WithSearch(902, "inProgress", 78, startedAt: "2026-11-10T08:00:00Z");

        var (_, first, _) = await account.RunAsync(Line("live-data", "list", "--limit", "1"), Ct);
        var (_, second, _) = await account.RunAsync(Line("live-data", "list", "--limit", "1", "--offset", "1"), Ct);

        Assert.Equal((902, true), ((int)JsonNode.Parse(first)!["items"]![0]!["id"]!, (bool)JsonNode.Parse(first)!["has_more"]!));
        Assert.Equal((901, false), ((int)JsonNode.Parse(second)!["items"]![0]!["id"]!, (bool)JsonNode.Parse(second)!["has_more"]!));
    }

    /// <summary>
    /// What Reply refuses a start for, each as reply-cli names it, and nothing started: an unfinished search of the same
    /// filters, running or paused (409); a team that cannot afford one contact (422); a user without the contact
    /// permissions (403, <c>access.feature_unavailable</c>); filters that name none (400); and a list the account lacks.
    /// </summary>
    [Theory]
    [InlineData("in_progress", "live_data.search_in_progress", 409, "liveDataSearch.searchInProgress")]
    [InlineData("paused", "live_data.search_in_progress", 409, "liveDataSearch.searchInProgress")]
    [InlineData("no_credits", "live_data.insufficient_credits", 422, "liveDataSearch.insufficientCredits")]
    [InlineData("forbidden", "access.feature_unavailable", 403, "liveDataSearch.forbidden")]
    [InlineData("no_filter", "live_data.invalid_filters", 400, "liveDataSearch.invalidParameter")]
    [InlineData("no_list", "contact-list.not_found", 404, null)]
    public async Task A_start_reply_refuses_starts_nothing(string world, string code, int status, string? replyCode)
    {
        using var account = new ReplyAccount().WithList(77, "Audience 12").WithList(78, "Running");
        _ = world switch
        {
            "in_progress" => account.WithSearch(901, "inProgress", 78, JsonNode.Parse(CtoOnly)!.AsObject()),
            "paused" => account.WithSearch(901, "pausedDueToInsufficientCredits", 78, JsonNode.Parse(CtoOnly)!.AsObject()),
            "no_credits" => account.Without("liveDataCredits"),
            "forbidden" => account.Without("liveData"),
            _ => account,
        };
        var filters = world == "no_filter" ? "{}" : CtoOnly;
        var list = world == "no_list" ? "79" : "77";
        var before = account.Searches.Count;

        var (exit, stdout, said) = await account.RunAsync(Line("live-data", "start", "--filters", "-", "--count", "25", "--list-id", list), filters, Ct);

        Assert.Equal((1, string.Empty), (exit, stdout.Trim()));
        var error = Error(said);
        Assert.Equal((code, status, replyCode), ((string?)error["code"], (int)error["status"]!, (string?)error["reply_code"]));
        Assert.Equal(before, account.Searches.Count);
    }

    /// <summary>A finished or deleted search of the same filters blocks nothing.</summary>
    [Fact]
    public async Task A_finished_search_of_the_same_filters_does_not_block_a_start()
    {
        using var account = new ReplyAccount().WithList(77, "Audience 12").WithList(78, "Done")
            .WithSearch(901, "noMoreContacts", 78, JsonNode.Parse(CtoOnly)!.AsObject());

        var (exit, _, _) = await account.RunAsync(Line("live-data", "start", "--filters", "-", "--count", "25", "--list-id", "77"), CtoOnly, Ct);

        Assert.Equal(0, exit);
        Assert.Equal(2, account.Searches.Count);
    }

    [Fact]
    public async Task A_preview_answers_reply_s_counts_and_sample_or_that_its_job_is_still_running()
    {
        using var done = new ReplyAccount().WithPreview(1200, 340,
            ReplyAccount.Sampled("Lena", "Novak", "CTO", "Porto, Portugal", "https://www.linkedin.com/in/lena-novak-example", "Northwind Freight", "northwind.example"));
        using var pending = new ReplyAccount().WithPendingPreview();

        var (finished, answer, _) = await done.RunAsync(Line("live-data", "preview", "--filters", "-", "--max-per-company", "2", "--wait-seconds", "60"), CtoOnly, Ct);
        var (waited, still, _) = await pending.RunAsync(Line("live-data", "preview", "--filters", "-", "--wait-seconds", "0"), CtoOnly, Ct);
        var (refused, _, said) = await done.RunAsync(Line("live-data", "preview", "--filters", "-"), "{}", Ct);

        Assert.Equal((0, 0, 1), (finished, waited, refused));
        var result = JsonNode.Parse(answer)!;
        Assert.Equal(("done", 1200, 340), ((string?)result["status"], (long)result["contacts_count"]!, (long)result["companies_count"]!));
        Assert.Equal("Lena", (string?)result["sample_contacts"]![0]!["first_name"]);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", (string?)result["job_id"]);
        Assert.Equal("pending", (string?)JsonNode.Parse(still)!["status"]);
        Assert.Equal("live_data.invalid_filters", (string?)Error(said)["code"]);
    }

    /// <summary>The values Reply suggests for a filter, those that contain the text ignoring case, a page at a time.</summary>
    [Fact]
    public async Task A_filter_s_values_are_those_that_contain_the_text_a_page_at_a_time()
    {
        using var account = new ReplyAccount().WithFilterValues("industries", "Software Development", "Logistics", "Computer Software");

        var (exit, answer, _) = await account.RunAsync(Line("live-data", "filter-values", "industries", "--search", "SOFT", "--limit", "1"), Ct);
        var (_, all, _) = await account.RunAsync(Line("live-data", "filter-values", "industries"), Ct);
        var (unknown, _, _) = await account.RunAsync(Line("live-data", "filter-values", "colours"), Ct);

        Assert.Equal((0, 2), (exit, unknown));
        Assert.Equal("""{"items":["Software Development"],"has_more":true}""", answer.Trim());
        Assert.Equal(3, JsonNode.Parse(all)!["items"]!.AsArray().Count);
    }

    /// <summary>The commands take their own options and nothing else, as commander does.</summary>
    [Theory]
    [InlineData("live-data", "start", "--filters", "-", "--count", "1", "--with-emails")]
    [InlineData("live-data", "get", "901", "--live-data-only")]
    [InlineData("contact-list", "contacts", "77", "--search", "x")]
    public async Task An_option_a_command_does_not_take_is_a_usage_error(params string[] command)
    {
        using var account = new ReplyAccount().WithList(77, "Audience 12");

        var (exit, _, _) = await account.RunAsync(Line(command), CtoOnly, Ct);

        Assert.Equal(2, exit);
    }

    private static string Id(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string[] Line(params string[] command) => ["--json", "-q", .. command];

    private static JsonObject Error(string stderr) =>
        JsonNode.Parse(stderr.Trim().Split('\n')[^1])!["error"]!.AsObject();
}
