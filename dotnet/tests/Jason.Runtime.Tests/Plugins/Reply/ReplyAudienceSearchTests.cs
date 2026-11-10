using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>audience.search</c>, <c>audience.search_status</c> and <c>audience.search_cancel</c> as the official package performs
/// them over Reply's Live Data. A search makes a contact list of its own, starts Reply's search on it and answers
/// <c>accepted</c> with both ids in its reference; a status read reads the search's status, then the list's people from
/// the cursor on, the cursor being how many it already returned, and deletes the list once the search has ended and the
/// last of them is read; a cancel deletes the search and the list. Reply's statuses are told in the contract's words.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyAudienceSearchTests
{
    /// <summary>The work item the search is, which <c>ReplyOperations</c> invokes every operation as.</summary>
    internal const string SearchItem = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRH";

    private const string ListName = "Jason search " + SearchItem + " #1";

    private const string Reference = "search-901-list-77";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Software => new()
    {
        ["industries"] = new JsonArray(new JsonObject { ["value"] = "Software Development", ["type"] = "include" }),
        ["jobTitles"] = new JsonArray(new JsonObject { ["value"] = "CTO", ["type"] = "include" }),
    };

    /// <summary>
    /// A search makes a private list named after the work item and the attempt, starts Reply's search on it with the
    /// definition on stdin, as written, and the envelope's options as Reply's flags — none for an option left out, which
    /// is Reply's own default — and answers <c>accepted</c> with a reference holding the search's id and the list's.
    /// </summary>
    [Theory]
    [InlineData(25, 2, true, "--max-per-company 2 --with-phones")]
    [InlineData(10, null, null, "")]
    public async Task A_search_starts_on_a_list_of_its_own_and_answers_accepted_with_both_ids(int count, int? perCompany, bool? phones, string options)
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, "audience.search", SearchInput(count, perCompany, phones), Ct);

        Assert.Equal(
            """{"items":[{"status":"accepted","reference":"search-4001-list-3001","found":null,"people":[]}]}""",
            ReplyOperations.Succeeded("audience.search", result).ToJsonString());
        Assert.Equal("search-4001-list-3001", (string?)Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!["search"]);
        var list = Assert.Single(account.Lists.OfType<JsonObject>());
        Assert.Equal((3001, ListName, false), ((int)list["id"]!, (string?)list["name"], (bool)list["isShared"]!));
        var search = Assert.Single(account.Searches.OfType<JsonObject>());
        Assert.Equal("""[{"id":3001,"name":"Jason search wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRH #1"}]""", search["lists"]!.ToJsonString());
        Assert.True(JsonNode.DeepEquals(Software, search["filters"]));
        var start = Assert.Single(account.Calls, call => call.Command == "live-data start");
        Assert.Equal(
            ("--json -q live-data start --filters - --count " + count + " --list-id 3001 " + options).Trim(),
            string.Join(' ', start.Args));
    }

    /// <summary>
    /// reply-cli takes no flag for people without an email, so a search asking for them is refused before anything is
    /// made, with the code reserved for an argument the provider's tool does not offer, rather than run without it.
    /// </summary>
    [Fact]
    public async Task A_search_that_asks_for_people_without_an_email_is_refused_before_anything_is_made()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed("audience.search", await ReplyOperations.InvokeAsync(api, "audience.search", SearchInput(count: 10, withoutEmail: true), Ct));

        Assert.Equal(("capability_not_offered", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("include_without_email", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Lists);
        Assert.Empty(account.Searches);
    }

    /// <summary>
    /// Reply's refusals of a start, each in the code the document gives it and none a provider block: an unfinished search
    /// of the same definition (409) a permanent refusal that says how to find it; no credits for one contact (422); a user
    /// without the contact permissions (403); a plan with no room for another contact (422); and a definition Reply refused
    /// for a rule of its own, in Reply's words. The
    /// list made for the search is deleted again: nothing is left standing at Reply.
    /// </summary>
    [Theory]
    [InlineData("in_progress", "search_in_progress", FailureClass.Permanent, "live-data list")]
    [InlineData("no_credits", "insufficient_credits", FailureClass.Permanent, "credits")]
    [InlineData("forbidden", "feature_unavailable", FailureClass.Permanent, "permission")]
    [InlineData("contact_limit", "feature_unavailable", FailureClass.Permanent, "holds no more contacts")]
    [InlineData("rejected", "definition_rejected", FailureClass.Validation, "Company-level filters cannot be combined with accountListIds")]
    public async Task A_start_reply_refuses_is_the_document_s_refusal_and_leaves_no_list_behind(string world, string code, FailureClass failure, string said)
    {
        using var account = new ReplyAccount().WithList(78, "Jason search wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRJ #1");
        _ = world switch
        {
            "in_progress" => account.WithSearch(901, "manuallyPaused", 78, Software),
            "no_credits" => account.Without("liveDataCredits"),
            "forbidden" => account.Without("liveData"),
            "contact_limit" => account.Refuses("live-data start", "live_data.contact_limit_reached", status: 422, replyCode: "liveDataSearch.contactLimitReached"),
            _ => account.Refuses("live-data start", "live_data.invalid_filters", status: 400, replyCode: "liveDataSearch.invalidParameter",
                detail: "Company-level filters cannot be combined with accountListIds; the account list defines the company set."),
        };
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed("audience.search", await ReplyOperations.InvokeAsync(api, "audience.search", SearchInput(count: 10), Ct));

        Assert.Equal((code, failure), (error.Code, error.Class));
        Assert.Null(error.Block);
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        Assert.Equal([78], account.Lists.Select(list => (int)list!["id"]!));
        Assert.Equal(world == "in_progress" ? 1 : 0, account.Searches.Count);
    }

    /// <summary>
    /// A start whose answer was lost may have started a search, so its list stays; the repeat reads Reply's searches first,
    /// finds the one on a list named after this work item, and answers it, starting nothing.
    /// </summary>
    [Fact]
    public async Task A_repeat_after_a_lost_start_answers_the_search_the_first_attempt_started()
    {
        using var account = new ReplyAccount().LosesTheOutcome("live-data start");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var before = DateTimeOffset.UtcNow;

        var lost = ReplyOperations.Failed("audience.search", await ReplyOperations.InvokeAsync(api, "audience.search", SearchInput(count: 10), Ct));
        var repeat = await ReplyOperations.InvokeAsync(api, "audience.search", SearchInput(count: 10), Ct, attempt: 2, unsettledSince: before);

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (lost.Code, lost.Class));
        Assert.Equal(
            """{"items":[{"status":"accepted","reference":"search-4001-list-3001","found":null,"people":[]}]}""",
            ReplyOperations.Succeeded("audience.search", repeat).ToJsonString());
        Assert.Single(account.Searches);
        Assert.Single(account.Lists);
    }

    /// <summary>
    /// A repeat that finds no search on a list of this work item's starts one on a list of its own attempt: another work
    /// item's search is not this one's, and a list an earlier attempt made under its own number never collides.
    /// </summary>
    [Fact]
    public async Task A_repeat_that_finds_no_search_of_its_own_starts_one_on_a_list_named_for_its_attempt()
    {
        using var account = new ReplyAccount()
            .WithList(78, "Jason search wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRJ #1")
            .WithSearch(901, "inProgress", 78, startedAt: DateTimeOffset.UtcNow.ToString("O"))
            .WithList(79, ListName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, "audience.search", SearchInput(count: 10), Ct, attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal("search-4001-list-3001", (string?)ReplyOperations.Succeeded("audience.search", result)["items"]![0]!["reference"]);
        Assert.Equal("Jason search wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRH #2", (string?)account.Lists.OfType<JsonObject>().Single(list => (int)list["id"]! == 3001)["name"]);
        Assert.Equal([901, 4001], account.Searches.Select(search => (int)search!["id"]!).Order());
    }

    /// <summary>
    /// A status read answers Reply's status in the contract's word, Reply's count of the people found, and a page of the
    /// list's people Live Data created from the cursor on, oldest first, each in Jason's shape with Reply's contact id and
    /// Reply's record beside it — a contact someone else put on the list is not one; the next cursor is how many the list
    /// has returned, and the next read starts there.
    /// </summary>
    [Fact]
    public async Task A_status_read_answers_a_page_of_the_people_found_from_the_cursor_on()
    {
        using var account = new ReplyAccount().WithContact(1001, "kept@example.com", "Kept").WithList(77, ListName, 1001).WithSearch(901, "inProgress", 77)
            .Finds(901,
                ReplyAccount.Person(5001, "Lena", "Novak", email: "lena.novak@northwind.example", title: "CTO", company: "Northwind Freight", domain: "northwind.example",
                    companySize: "51-200", industry: "Logistics", city: "Porto", country: "Portugal", phone: "+351 555 0100", linkedIn: "https://www.linkedin.com/in/lena-novak-example"),
                ReplyAccount.Person(5002, "Ivo", "Marek"),
                ReplyAccount.Person(5003, "Ana", "Silva"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(null, 2), Ct));
        var second = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput((string?)first["cursor"], 2), Ct));

        Assert.Equal(("in_progress", 3, "2", true, "inProgress"), ((string?)first["state"], (int)first["found"]!, (string?)first["cursor"], (bool)first["has_more"]!, (string?)first["vendor"]!["status"]));
        Assert.Equal(["5001", "5002"], first["people"]!.AsArray().Select(person => (string?)person!["external_id"]));
        var lena = first["people"]![0]!.AsObject();
        var shaped = new JsonObject(lena.Where(member => member.Key != "raw").Select(member => KeyValuePair.Create(member.Key, member.Value?.DeepClone())));
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse("""{"external_id":"5001","first_name":"Lena","last_name":"Novak","title":"CTO","company":{"name":"Northwind Freight","domain":"northwind.example","size":"51-200","industry":"Logistics"},"location":"Porto, Portugal","channels":[{"channel":"email","value":"lena.novak@northwind.example"},{"channel":"phone","value":"+351 555 0100"},{"channel":"linkedin","value":"https://www.linkedin.com/in/lena-novak-example"}]}"""),
                shaped),
            shaped.ToJsonString());
        Assert.Equal(5001, (int)JsonNode.Parse((string)lena["raw"]!)!["id"]!);
        Assert.Equal(("in_progress", "3", false), ((string?)second["state"], (string?)second["cursor"], (bool)second["has_more"]!));
        Assert.Equal(["5003"], second["people"]!.AsArray().Select(person => (string?)person!["external_id"]));
        Assert.Single(account.Lists);
    }

    /// <summary>
    /// A page that would pass what the runtime takes as one result is cut short with more to read: the cursor stops at the
    /// last person answered, and the next read starts with the first left out.
    /// </summary>
    [Fact]
    public async Task A_page_too_large_for_one_result_is_cut_and_the_next_read_starts_where_it_stopped()
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, "inProgress", 77)
            .Finds(901, [.. Enumerable.Range(0, 40).Select(at => Heavy(5001 + at))]);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(null, 50), Ct));
        var answered = first["people"]!.AsArray().Count;
        var next = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput((string?)first["cursor"], 50), Ct));

        Assert.InRange(answered, 1, 39);
        Assert.Equal((answered.ToString(System.Globalization.CultureInfo.InvariantCulture), true), ((string?)first["cursor"], (bool)first["has_more"]!));
        Assert.Equal((5001 + answered).ToString(System.Globalization.CultureInfo.InvariantCulture), (string?)next["people"]![0]!["external_id"]);
    }

    /// <summary>
    /// Reply's record goes beside each person as JSON text of at most 16 KiB: whole where it fits, without its custom fields
    /// where those are what would not, and null where even the rest would not.
    /// </summary>
    [Theory]
    [InlineData("small", true, true)]
    [InlineData("custom_fields", true, false)]
    [InlineData("name", false, false)]
    public async Task Reply_s_record_beside_a_person_is_trimmed_to_fit(string large, bool kept, bool withCustomFields)
    {
        var person = ReplyAccount.Person(5001, large == "name" ? new string('L', 17000) : "Lena", "Novak");
        person["customFields"] = new JsonArray(new JsonObject { ["key"] = "notes", ["value"] = new string('n', large == "custom_fields" ? 17000 : 10) });
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, "inProgress", 77).Finds(901, person);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(null, 10), Ct));

        var raw = (string?)read["people"]![0]!["raw"];
        Assert.Equal(kept, raw is not null);
        if (raw is not null)
        {
            Assert.Equal((5001, withCustomFields), ((int)JsonNode.Parse(raw)!["id"]!, JsonNode.Parse(raw)!.AsObject().ContainsKey("customFields")));
        }
    }

    /// <summary>
    /// A repeat reads the team's searches back, a page at a time, past newer searches of other work items, to the one its
    /// own earlier attempt started; and stops reading at a page that ends an hour before the earliest attempt that may have
    /// acted, so a search of its own older than that is not found and a new one starts.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_repeat_reads_the_team_s_searches_back_as_far_as_its_earliest_attempt(bool within)
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount();
        var older = within ? 0 : 100;
        foreach (var at in Enumerable.Range(0, 100 + older))
        {
            var startedAt = at < 100 ? since.AddMinutes(1 + at) : since.AddHours(-2).AddMinutes(-at);
            account.WithList(6001 + at, "Jason search wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRJ #1")
                .WithSearch(7001 + at, "inProgress", 6001 + at, startedAt: startedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }

        account.WithList(77, ListName).WithSearch(901, "finished", 77, Software, startedAt: (within ? since.AddSeconds(30) : since.AddHours(-10)).ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded("audience.search", await ReplyOperations.InvokeAsync(api, "audience.search", SearchInput(count: 10), Ct, attempt: 2, unsettledSince: since));

        Assert.Equal(within ? "search-901-list-77" : "search-7201-list-6201", (string?)result["items"]![0]!["reference"]);
        Assert.Equal(within ? 101 : 202, account.Searches.Count);
    }

    /// <summary>
    /// A contact Reply's answer gives without an identifier cannot be answered as a person, so it is passed over and still
    /// counted as read: the cursor moves past it, and the next read does not meet it again.
    /// </summary>
    [Fact]
    public async Task A_contact_reply_gives_without_an_id_is_passed_over_and_counted_as_read()
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, "inProgress", 77)
            .Returns("contact-list contacts", JsonNode.Parse("""
                {"items":[{"id":null,"email":"no.id@example.com","firstName":"No","lastName":"Id"},
                          {"id":5002,"email":"ivo.marek@example.com","firstName":"Ivo","lastName":"Marek","addingDate":"2026-11-10T09:01:00.00Z"}],
                 "has_more":false}
                """)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("3", 50), Ct));

        Assert.Equal(["5002"], read["people"]!.AsArray().Select(person => (string?)person!["external_id"]));
        Assert.Equal(("5", false), ((string?)read["cursor"], (bool)read["has_more"]!));
    }

    /// <summary>
    /// A read with nobody new answers an empty page and the cursor it was given, so the next read starts there again; a
    /// first read of a search that found nobody yet answers the cursor at the start of the list.
    /// </summary>
    [Theory]
    [InlineData(null, 0, "0")]
    [InlineData("1", 1, "1")]
    public async Task A_read_with_nobody_new_answers_an_empty_page_and_where_the_next_starts(string? cursor, int people, string next)
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, "inProgress", 77)
            .Finds(901, [.. Enumerable.Range(0, people).Select(at => ReplyAccount.Person(5001 + at, "Lena", "Novak"))]);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(cursor, 50), Ct));

        Assert.Equal(
            $$$"""{"state":"in_progress","found":{{{people}}},"people":[],"cursor":"{{{next}}}","has_more":false,"vendor":{"status":"inProgress"}}""",
            read.ToJsonString());
        Assert.Single(account.Lists);
    }

    /// <summary>
    /// Once the search has ended — the count reached, or the source run dry — the read that returns the last of its people
    /// deletes the list: Reply moves a batch onto the list before it sets the final status, so nothing lands after.
    /// </summary>
    [Theory]
    [InlineData("finished", "finished")]
    [InlineData("noMoreContacts", "exhausted")]
    public async Task The_terminal_read_answers_the_last_people_and_deletes_the_list(string status, string state)
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, status, 77)
            .Finds(901, ReplyAccount.Person(5001, "Lena", "Novak"), ReplyAccount.Person(5002, "Ivo", "Marek"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("1", 50), Ct));

        Assert.Equal((state, "2", false), ((string?)read["state"], (string?)read["cursor"], (bool)read["has_more"]!));
        Assert.Equal(["5002"], read["people"]!.AsArray().Select(person => (string?)person!["external_id"]));
        Assert.Empty(account.Lists);
        Assert.Equal(2, account.Contacts.Count);
    }

    /// <summary>An ended search whose page is cut short keeps its list until the read that reaches the last person.</summary>
    [Fact]
    public async Task An_ended_search_keeps_its_list_until_the_last_page_is_read()
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, "finished", 77)
            .Finds(901, ReplyAccount.Person(5001, "Lena", "Novak"), ReplyAccount.Person(5002, "Ivo", "Marek"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(null, 1), Ct));
        var listed = account.Lists.Count;
        var last = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("1", 1), Ct));

        Assert.Equal(("finished", true, 1), ((string?)first["state"], (bool)first["has_more"]!, listed));
        Assert.Equal(("finished", false), ((string?)last["state"], (bool)last["has_more"]!));
        Assert.Empty(account.Lists);
    }

    /// <summary>
    /// A list Reply would not delete never holds back the people read: the read answers them, and the next read of the
    /// ended search, which finds nobody new, deletes it.
    /// </summary>
    [Fact]
    public async Task A_list_reply_would_not_delete_does_not_hold_back_the_people_read()
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, "finished", 77)
            .Finds(901, ReplyAccount.Person(5001, "Lena", "Novak"))
            .Refuses("contact-list delete", "reply.unavailable", status: 503);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(null, 50), Ct));
        var again = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("1", 50), Ct));

        Assert.Equal(("finished", "1"), ((string?)read["state"], (string?)read["cursor"]));
        Assert.Equal(["5001"], read["people"]!.AsArray().Select(person => (string?)person!["external_id"]));
        Assert.Equal(("finished", "1", 0), ((string?)again["state"], (string?)again["cursor"], again["people"]!.AsArray().Count));
        Assert.Empty(account.Lists);
    }

    /// <summary>
    /// Each of Reply's statuses in the contract's word, Reply's own word beside it; a word this version does not know is the
    /// nearest of the six — a pause where Reply's word says paused, and otherwise a search still running, read again.
    /// </summary>
    [Theory]
    [InlineData("inProgress", "in_progress")]
    [InlineData("finished", "finished")]
    [InlineData("noMoreContacts", "exhausted")]
    [InlineData("manuallyPaused", "paused")]
    [InlineData("pausedDueToInsufficientCredits", "paused_for_credits")]
    [InlineData("pausedDueToContactLimit", "paused_for_limit")]
    [InlineData("pausedDueToMaintenance", "paused")]
    [InlineData("queued", "in_progress")]
    public async Task Each_of_reply_s_statuses_is_told_in_the_contract_s_word(string status, string state)
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, status, 77);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(null, 10), Ct));

        Assert.Equal((state, status), ((string?)read["state"], (string?)read["vendor"]!["status"]));
    }

    /// <summary>A search Reply no longer holds, and a reference this package never made, are a search not found.</summary>
    [Theory]
    [InlineData("search-901-list-77")]
    [InlineData("901")]
    [InlineData("search-0-list-77")]
    public async Task A_search_reply_does_not_hold_is_not_found(string reference)
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(null, 10, reference), Ct));

        Assert.Equal(("search_not_found", FailureClass.Permanent), (error.Code, error.Class));
    }

    /// <summary>A cursor this package never wrote is refused before anything is asked: the runtime keeps the package's own.</summary>
    [Theory]
    [InlineData("-1")]
    [InlineData("1000001")]
    [InlineData("abc")]
    public async Task A_cursor_this_package_never_wrote_is_refused_before_anything_is_asked(string cursor)
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(cursor, 10), Ct));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Empty(account.Calls);
    }

    /// <summary>A cancel stops a search running or paused: the search is deleted at Reply, and so is its list.</summary>
    [Theory]
    [InlineData("inProgress")]
    [InlineData("manuallyPaused")]
    [InlineData("pausedDueToInsufficientCredits")]
    public async Task A_cancel_stops_a_search_running_or_paused_and_deletes_its_list(string status)
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, status, 77).Finds(901, ReplyAccount.Person(5001, "Lena", "Novak"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded("audience.search_cancel", await ReplyOperations.InvokeAsync(api, "audience.search_cancel", CancelInput(), Ct));

        Assert.Equal("""{"items":[{"status":"cancelled","ended_as":null}]}""", result.ToJsonString());
        Assert.Empty(account.Searches);
        Assert.Empty(account.Lists);
        Assert.NotNull(account.ContactOf(5001));
    }

    /// <summary>
    /// A cancel of a search that already ended answers how it ended and changes nothing: the search and its list stay, so the
    /// status read goes on reading the people it found.
    /// </summary>
    [Theory]
    [InlineData("finished", "finished")]
    [InlineData("noMoreContacts", "exhausted")]
    public async Task A_cancel_of_an_ended_search_answers_how_it_ended(string status, string endedAs)
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, status, 77);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded("audience.search_cancel", await ReplyOperations.InvokeAsync(api, "audience.search_cancel", CancelInput(), Ct));

        Assert.Equal($$"""{"items":[{"status":"already_ended","ended_as":"{{endedAs}}"}]}""", result.ToJsonString());
        Assert.Single(account.Searches);
        Assert.Single(account.Lists);
    }

    /// <summary>
    /// A cancel whose answer was lost may have deleted the search, so the repeat reads it first: Reply holding nothing under
    /// it is the cancel done, <c>already_ended</c>, where on a first attempt it is a search not found.
    /// </summary>
    [Fact]
    public async Task A_cancel_s_repeat_after_a_lost_answer_finds_the_search_gone_and_answers_it_ended()
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, "inProgress", 77).LosesTheOutcome("live-data delete");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed("audience.search_cancel", await ReplyOperations.InvokeAsync(api, "audience.search_cancel", CancelInput(), Ct));
        var repeat = ReplyOperations.Succeeded("audience.search_cancel", await ReplyOperations.InvokeAsync(api, "audience.search_cancel", CancelInput(), Ct, attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var first = ReplyOperations.Failed("audience.search_cancel", await ReplyOperations.InvokeAsync(api, "audience.search_cancel", CancelInput(), Ct));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (lost.Code, lost.Class));
        Assert.Equal("""{"items":[{"status":"already_ended","ended_as":null}]}""", repeat.ToJsonString());
        Assert.Equal(("search_not_found", FailureClass.Permanent), (first.Code, first.Class));
        Assert.Empty(account.Lists);
    }

    internal static JsonObject SearchInput(int count, int? perCompany = null, bool? phones = null, bool? withoutEmail = null)
    {
        var args = new JsonObject { ["definition"] = Software, ["count"] = count };
        if (perCompany is { } most)
        {
            args["people_per_company"] = most;
        }

        if (phones is { } find)
        {
            args["find_phones"] = find;
        }

        if (withoutEmail is { } include)
        {
            args["include_without_email"] = include;
        }

        return new JsonObject
        {
            ["args"] = args,
            ["definition"] = Software,
            ["campaign"] = Campaign(),
            ["idempotency_key"] = SearchItem,
        };
    }

    internal static JsonObject StatusInput(string? cursor, int limit, string reference = Reference) => new()
    {
        ["args"] = new JsonObject { ["search"] = SearchItem, ["limit"] = limit },
        ["search"] = new JsonObject { ["work_item"] = SearchItem, ["reference"] = reference, ["cursor"] = cursor },
        ["campaign"] = Campaign(),
    };

    internal static JsonObject CancelInput(string reference = Reference) => new()
    {
        ["args"] = new JsonObject { ["search"] = SearchItem },
        ["search"] = new JsonObject { ["work_item"] = SearchItem, ["reference"] = reference },
        ["campaign"] = Campaign(),
    };

    /// <summary>A person whose record weighs some 36 KB as the runtime counts a result: forty of them pass one page's budget.</summary>
    private static JsonObject Heavy(int id)
    {
        var person = ReplyAccount.Person(id, "Lena", "Novak");
        person["customFields"] = new JsonArray(new JsonObject { ["key"] = "notes", ["value"] = new string('<', 6000) });
        return person;
    }

    internal static JsonObject Campaign() => new()
    {
        ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        ["name"] = "Q3 LatAm founders",
        ["status"] = "active",
        ["external_ids"] = new JsonObject(),
    };
}
