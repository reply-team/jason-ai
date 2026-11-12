using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Routing;

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
    /// Every ending of a start the package reads, in the code the document gives it and none a provider block. Reply's
    /// refusals: an unfinished search of the same definition (409), a permanent refusal that says how to find it; no
    /// credits for one contact (422); a user without the contact permissions (403); a plan with no room for another
    /// contact (422); a definition Reply refused for a rule of its own, in Reply's words. The passing ones the runtime
    /// repeats: Reply not reached, too many calls, its search service failed. Each started nothing, so the list made for
    /// the search is deleted again and nothing is left standing at Reply. An ending that may have started the search keeps
    /// the list, which is how the repeat's recovery read finds it: the CLI's word for a lost answer, <c>outcome.unknown</c>,
    /// at exit 1 as at exit 3, and a start that timed out; and a list create whose answer was lost starts nothing on a list
    /// nobody named.
    /// </summary>
    [Theory]
    [InlineData("in_progress", "search_in_progress", FailureClass.Permanent, "live-data list", false, 1)]
    [InlineData("no_credits", "insufficient_credits", FailureClass.Permanent, "credits", false, 0)]
    [InlineData("forbidden", "feature_unavailable", FailureClass.Permanent, "permission", false, 0)]
    [InlineData("contact_limit", "feature_unavailable", FailureClass.Permanent, "holds no more contacts", false, 0)]
    [InlineData("rejected", "definition_rejected", FailureClass.Validation, "Company-level filters cannot be combined with accountListIds", false, 0)]
    [InlineData("unavailable", "provider_unavailable", FailureClass.Transient, null, false, 0)]
    [InlineData("rate_limited", "rate_limited", FailureClass.Transient, null, false, 0)]
    [InlineData("service_failed", "provider_unavailable", FailureClass.Transient, null, false, 0)]
    [InlineData("outcome_unknown", "provider_answer_lost", FailureClass.Ambiguous, null, true, 0)]
    [InlineData("timed_out", "provider_answer_lost", FailureClass.Ambiguous, null, true, 0)]
    [InlineData("create_lost", "provider_answer_lost", FailureClass.Ambiguous, null, true, 0)]
    public async Task A_start_s_ending_is_the_document_s_and_keeps_its_list_only_where_a_search_may_have_started(
        string world, string code, FailureClass failure, string? said, bool kept, int searches)
    {
        using var account = new ReplyAccount().WithList(78, "Jason search wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRJ #1");
        _ = world switch
        {
            "in_progress" => account.WithSearch(901, "manuallyPaused", 78, Software),
            "no_credits" => account.Without("liveDataCredits"),
            "forbidden" => account.Without("liveData"),
            "contact_limit" => account.Refuses("live-data start", "live_data.contact_limit_reached", status: 422, replyCode: "liveDataSearch.contactLimitReached"),
            "rejected" => account.Refuses("live-data start", "live_data.invalid_filters", status: 400, replyCode: "liveDataSearch.invalidParameter",
                detail: "Company-level filters cannot be combined with accountListIds; the account list defines the company set."),
            "unavailable" => account.Refuses("live-data start", "reply.unavailable", status: 503),
            "rate_limited" => account.Refuses("live-data start", "rate_limited", status: 429),
            "service_failed" => account.Refuses("live-data start", "live_data.service_failed", status: 500),
            "outcome_unknown" => account.Refuses("live-data start", "outcome.unknown", status: 502),
            "timed_out" => account.Hangs("live-data start", 20_000),
            _ => account.LosesTheOutcome("contact-list create"),
        };
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            "audience.search",
            await ReplyOperations.InvokeAsync(api, "audience.search", SearchInput(count: 10), Ct, timeout: world == "timed_out" ? TimeSpan.FromSeconds(16) : null));

        Assert.Equal((code, failure), (error.Code, error.Class));
        Assert.Null(error.Block);
        if (said is not null)
        {
            Assert.Contains(said, error.Message, StringComparison.Ordinal);
        }

        Assert.Equal(kept ? [78, 3001] : [78], account.Lists.Select(list => (int)list!["id"]!).Order());
        Assert.Equal(searches, account.Searches.Count);
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
    /// acted, so a search of its own older than that is not found and a new one starts. Reply writes <c>startedAt</c> in
    /// UTC with no offset, and a repeat on a machine whose clock is east of UTC by more than that hour still reads it as
    /// UTC, so a page of newer searches never looks older than it is. Asia/Tokyo is UTC+9 all year, with no daylight
    /// saving to move it.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task A_repeat_reads_the_team_s_searches_back_as_far_as_its_earliest_attempt(bool within, bool offsetLessEastOfUtc)
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-30);
        using var account = new ReplyAccount();
        var older = within ? 0 : 100;
        foreach (var at in Enumerable.Range(0, 100 + older))
        {
            var startedAt = at < 100 ? since.AddMinutes(1 + at) : since.AddHours(-2).AddMinutes(-at);
            account.WithList(6001 + at, "Jason search wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRJ #1")
                .WithSearch(7001 + at, "inProgress", 6001 + at, startedAt: Written(startedAt));
        }

        account.WithList(77, ListName).WithSearch(901, "finished", 77, Software, startedAt: Written(within ? since.AddSeconds(30) : since.AddHours(-10)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        using var zone = new ProcessVariable("TZ", offsetLessEastOfUtc ? "Asia/Tokyo" : "UTC");
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded("audience.search", await ReplyOperations.InvokeAsync(api, "audience.search", SearchInput(count: 10), Ct, attempt: 2, unsettledSince: since));

        Assert.Equal(within ? "search-901-list-77" : "search-7201-list-6201", (string?)result["items"]![0]!["reference"]);
        Assert.Equal(within ? 101 : 202, account.Searches.Count);

        string Written(DateTimeOffset moment) => offsetLessEastOfUtc
            ? moment.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ff", System.Globalization.CultureInfo.InvariantCulture)
            : moment.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
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
    /// Once the search has ended — the count reached, or the source run dry — the read that returns its last people never
    /// deletes the list: it answers them with more to read, so the runtime keeps them before anything is deleted. The read
    /// after them starts at the end, finds nobody, answers the search ended with nothing more, and deletes the list; its
    /// repeat finds the same nothing, since Reply reads a list it no longer holds as empty.
    /// </summary>
    [Theory]
    [InlineData("finished", "finished")]
    [InlineData("noMoreContacts", "exhausted")]
    public async Task The_last_people_of_an_ended_search_come_with_more_and_the_empty_read_after_them_deletes_the_list(string status, string state)
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, status, 77)
            .Finds(901, ReplyAccount.Person(5001, "Lena", "Novak"), ReplyAccount.Person(5002, "Ivo", "Marek"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var last = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("1", 50), Ct));
        var kept = account.Lists.Count;
        var after = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("2", 50), Ct));
        var deleted = account.Lists.Count;
        var repeat = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("2", 50), Ct));

        Assert.Equal((state, "2", true, 1), ((string?)last["state"], (string?)last["cursor"], (bool)last["has_more"]!, kept));
        Assert.Equal(["5002"], last["people"]!.AsArray().Select(person => (string?)person!["external_id"]));
        Assert.Equal((state, "2", false, 0, 0), ((string?)after["state"], (string?)after["cursor"], (bool)after["has_more"]!, after["people"]!.AsArray().Count, deleted));
        Assert.Equal((state, "2", false, 0), ((string?)repeat["state"], (string?)repeat["cursor"], (bool)repeat["has_more"]!, repeat["people"]!.AsArray().Count));
        Assert.Equal(2, account.Contacts.Count);
    }

    /// <summary>
    /// The same through the dispatcher, as a search is really followed: a search a person approved runs, and the poll's
    /// read of it once Reply has finished it lands its two people with the list still at Reply, so a read whose answer were
    /// lost could be made again. The poll then makes the read after them at once, which finds nobody, deletes the list and
    /// ends the search, said once.
    /// </summary>
    [Fact]
    public async Task The_poll_lands_an_ended_search_s_last_people_and_then_makes_the_read_that_deletes_its_list()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        await using var api = await DispatchHarness.ScannedOnceAsync(await ReplyPlugins.StartAsync(Ct, Idle, clock), Ct);
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 LatAm founders" }, Ct)).Id;
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign }, Ct);
        var search = (await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "provider_op", operation = "audience.search", context = new JsonObject { ["input"] = new JsonObject { ["definition"] = Software, ["count"] = 2 } } },
            Ct)).Id;
        await ScanAsync(api);
        var approval = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);
        await api.PostOkAsync<ApprovalDto>(Operations.ApprovalApprove, new { approval_id = approval.Id, actor = new { type = "human", id = "ada" } }, Ct);
        await ScanAsync(api);
        var started = Assert.Single(account.Searches.OfType<JsonObject>());
        account.Finds((int)started["id"]!, ReplyAccount.Person(5001, "Lena", "Novak"), ReplyAccount.Person(5002, "Ivo", "Marek")).Moves((int)started["id"]!, "finished");

        clock.Advance(TimeSpan.FromMinutes(5));
        await ScanAsync(api);
        var landed = (await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, new { campaign_id = campaign }, Ct)).Items.Count;
        var listed = account.Lists.Count;
        var endedFirst = await EndedAsync(api, search);
        await ScanAsync(api);

        Assert.Equal<(int, int, int)>((2, 1, 0), (landed, listed, endedFirst));
        Assert.Empty(account.Lists);
        Assert.Equal(1, await EndedAsync(api, search));
        var reads = (await api.PostOkAsync<Page<WorkItemDto>>(Operations.WorkItemList, new { campaign_id = campaign }, Ct)).Items
            .Where(item => item.Operation == "audience.search_status").ToList();
        Assert.Equal(2, reads.Count);
        Assert.All(reads, read => Assert.Equal(WorkItemStatus.Succeeded, read.Status));

        static void Idle(JasonPaths paths)
        {
            var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
            settings["Dispatcher"] = JsonNode.Parse("""{"TickSeconds":3600}""");
            File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
            TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(ReplyPlugins.PluginId));
        }
    }

    private static async Task ScanAsync(RuntimeApiFixture api)
    {
        await api.Resolve<ScanRunner>().ScanOnceAsync(Ct);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(60)));
    }

    private static async Task<int> EndedAsync(RuntimeApiFixture api, string search) =>
        (await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { kind = "audience_search_ended" }, Ct)).Items.Count(line => line.Key == search);

    /// <summary>An ended search whose page is cut short keeps its list through every read that carries people.</summary>
    [Fact]
    public async Task An_ended_search_keeps_its_list_until_the_last_page_is_read()
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, "finished", 77)
            .Finds(901, ReplyAccount.Person(5001, "Lena", "Novak"), ReplyAccount.Person(5002, "Ivo", "Marek"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(null, 1), Ct));
        var last = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("1", 1), Ct));

        Assert.Equal(("finished", true), ((string?)first["state"], (bool)first["has_more"]!));
        Assert.Equal(("finished", true), ((string?)last["state"], (bool)last["has_more"]!));
        Assert.Single(account.Lists);
    }

    /// <summary>
    /// A list Reply would not delete never holds back the answer: the empty read after the last people still answers the
    /// search ended, with nothing more to read, and its repeat, finding the same nobody, deletes the list.
    /// </summary>
    [Fact]
    public async Task A_list_reply_would_not_delete_does_not_hold_back_the_people_read()
    {
        using var account = new ReplyAccount().WithList(77, ListName).WithSearch(901, "finished", 77)
            .Finds(901, ReplyAccount.Person(5001, "Lena", "Novak"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput(null, 50), Ct));
        account.Refuses("contact-list delete", "reply.unavailable", status: 503);
        var empty = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("1", 50), Ct));
        var kept = account.Lists.Count;
        var again = ReplyOperations.Succeeded("audience.search_status", await ReplyOperations.InvokeAsync(api, "audience.search_status", StatusInput("1", 50), Ct));

        Assert.Equal(("finished", "1", true), ((string?)read["state"], (string?)read["cursor"], (bool)read["has_more"]!));
        Assert.Equal(["5001"], read["people"]!.AsArray().Select(person => (string?)person!["external_id"]));
        Assert.Equal(("finished", "1", false, 1), ((string?)empty["state"], (string?)empty["cursor"], (bool)empty["has_more"]!, kept));
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
