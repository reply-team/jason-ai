using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Notifications;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Audiences;

/// <summary>
/// The audience family end to end: a real runtime, a real plugin host, and two stand-in providers. One keeps the searches
/// it runs at a stand-in account on the loopback interface, so a search started in one call is read and stopped in the
/// next; the other finishes every search inside its call. Everything is asked through the API, as a person or an agent
/// would ask it: what an operation takes for a campaign, an audience's size, a filter's values, whether the account may
/// search, and then a search a person approves, read and stopped by the reference the provider gave it.
/// </summary>
public sealed class AudienceFamilyTests
{
    private const string Keeping = "stand-in-search";

    private const string Finishing = "stand-in-finish";

    /// <summary>A provider that sizes audiences and searches none, with answers of its own.</summary>
    private const string Sizing = "stand-in-sizing";

    /// <summary>A real dispatcher that never ticks on its own: every scan here is one the test asked for.</summary>
    private const string Idle = """{"Dispatcher":{"TickSeconds":3600}}""";

    /// <summary>What both stand-ins say an audience carries: industries, each one of the provider's own words.</summary>
    private const string Definition = """
        audience_definition:
          schema:
            type: object
            additionalProperties: false
            required: [industries]
            properties:
              industries:
                description: Industries the company is in, as the provider names them.
                type: array
                minItems: 1
                maxItems: 25
                items:
                  type: string
                  minLength: 1
                  maxLength: 200
          keys:
            industries:
              side: company
              typeahead: industries
        """;

    /// <summary>One person as both stand-ins answer them, made up, with the provider's record beside the neutral part.</summary>
    private const string People = """
        function person(n) {
          return {
            external_id: "p-" + n, first_name: "Person", last_name: String(n), title: "Head of Sales",
            company: { name: "Northwind", domain: "northwind.example", size: "51-200", industry: "Software" },
            location: "Lisbon", channels: [{ channel: "email", value: "person" + n + "@northwind.example" }],
            raw: JSON.stringify({ id: n }),
          };
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Software => new() { ["industries"] = new JsonArray("Software") };

    /// <summary>
    /// The reads first: what <c>audience.search</c> takes for the campaign, the plugin's half beside the contract's, and
    /// nothing of a plugin's for an operation that carries no definition, and a refusal for one the routed plugin does
    /// not perform; an audience's size and sample; a filter's values; whether the account may search. Each read goes where
    /// the campaign's searches go, whatever its own operation resolves to — here an override sends all three to another
    /// provider — so nobody previews one provider's audience and approves a search at another. A definition off the
    /// plugin's schema, and an audience by a number the campaign holds none under, are refused before the provider is
    /// asked. Each exchange is recorded for the clients that render it, and none of them makes an audience.
    /// </summary>
    [Fact]
    public async Task A_campaign_s_reads_answer_where_its_searches_go_and_describe_what_the_routed_plugin_takes()
    {
        using var account = new StandInAccount();
        await using var api = await StartAsync(account);
        var campaign = await CampaignAsync(api, "Spring search");
        foreach (var read in new[] { "audience.preview", "audience.filter_values", "audience.availability" })
        {
            await TestRoutes.SetCampaignAsync(api, campaign, read, Sizing, null, Ct);
        }

        var plugin = (await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct)).Plugins.Single(listed => listed.Id == Keeping);
        Assert.Equal("Industries the company is in, as the provider names them.", (string?)plugin.AudienceDefinition!["schema"]!["properties"]!["industries"]!["description"]);
        Assert.Equal("""{"industries":{"side":"company","typeahead":"industries"}}""", plugin.AudienceDefinition["keys"]!.ToJsonString());

        var search = await api.RecordAsync<OperationDescriptionDto>(Operations.OperationDescribe, "audience-search", new { campaign_id = campaign, operation = "audience.search" }, Ct);
        Assert.Equal((Keeping, "1.0.0"), (search.PluginId, search.PluginVersion));
        Assert.Equal("integer", (string?)search.Arguments["properties"]!["count"]!["type"]);
        Assert.True(JsonNode.DeepEquals(plugin.AudienceDefinition, search.Definition));
        var described = await api.PostOkAsync<OperationDescriptionDto>(Operations.OperationDescribe, new { campaign_id = campaign, operation = "audience.preview" }, Ct);
        Assert.Equal(Keeping, described.PluginId);
        Assert.True(JsonNode.DeepEquals(plugin.AudienceDefinition, described.Definition));
        var get = await api.RecordAsync<OperationDescriptionDto>(Operations.OperationDescribe, "campaign-get", new { campaign_id = campaign, operation = "campaign.get" }, Ct);
        Assert.Null(get.Definition);
        Assert.Equal(
            (HttpStatusCode.Conflict, "plugin_operation_unsupported"),
            Refusal(await api.PostAsync(Operations.OperationDescribe, new { campaign_id = campaign, operation = "message.send" }, Ct)));

        var preview = await api.RecordAsync<AudiencePreviewResponse>(Operations.AudiencePreview, "by-definition", new { campaign_id = campaign, definition = Software }, Ct);
        Assert.Equal((Keeping, 1840L, 412L), (preview.PluginId, preview.Count, preview.Companies));
        Assert.Equal(["1", "2"], preview.Sample.Select(person => (string?)person!["last_name"]));
        var refused = await api.RecordErrorAsync(
            Operations.AudiencePreview, "definition-invalid", new { campaign_id = campaign, definition = new { industries = "Software" } }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal(AttemptErrors.DefinitionInvalid, refused.Code);
        Assert.Equal("/definition/industries", Assert.Single(refused.Details!).Field);
        Assert.Equal(
            AttemptErrors.AudienceUnknown,
            (await api.RecordErrorAsync(Operations.AudiencePreview, "audience-unknown", new { campaign_id = campaign, audience = 3 }, HttpStatusCode.NotFound, Ct)).Code);

        var values = await api.RecordAsync<AudienceFilterValuesResponse>(
            Operations.AudienceFilterValues, "industries", new { campaign_id = campaign, filter = "industries", query = "Software", limit = 5 }, Ct);
        Assert.Equal(["Software", "Software Development"], values.Values.Select(value => (string?)value));

        var availability = await api.RecordAsync<AudienceAvailabilityResponse>(Operations.AudienceAvailability, "available", new { campaign_id = campaign }, Ct);
        Assert.True(availability.Available);
        Assert.Null(availability.Reason);
        Assert.Equal("""{"status":null}""", availability.Vendor?.ToJsonString());

        // Reads are asked of the provider and recorded nowhere: not one search was started by any of them, and the campaign
        // holds no audience.
        Assert.Empty(account.Started);
        Assert.Equal(0, await AudiencesAsync(api));
    }

    /// <summary>
    /// A search a person approves: parked before the provider hears of it, started once approved under the definition the
    /// person read, and answered with the provider's reference, which the status read and the stop then act by. Each
    /// status read starts where the last one stopped, by the cursor the runtime keeps, and its people land as members of
    /// the campaign, listed with the audience and the search they came from; the read that reaches the last person of a
    /// finished search ends it. A stop of a running search cancels it, and a second stop finds it ended, which is a
    /// success too. The two searches run under two definitions, the campaign's audiences 1 and 2, and a
    /// preview that names the second by its number is answered: it is composed with the definition that audience was made
    /// from, without which the plugin's schema refuses it.
    /// </summary>
    [Fact]
    public async Task A_search_runs_once_approved_and_is_read_and_stopped_by_its_reference()
    {
        using var account = new StandInAccount();
        await using var api = await StartAsync(account);
        var campaign = await CampaignAsync(api, "Spring search");

        var first = await ApprovedSearchAsync(api, account, campaign);
        Assert.Equal(["accepted", "srch-1"], [(string?)first.Result!["items"]![0]!["status"], (string?)first.Result["items"]![0]!["reference"]]);

        var running = await RunAsync(api, campaign, "audience.search_status", new JsonObject { ["search"] = first.Id, ["limit"] = 2 });
        Assert.Equal(WorkItemStatus.Succeeded, running.Status);
        Assert.Equal("in_progress", (string?)running.Result!["state"]);
        Assert.Equal(["p-1", "p-2"], running.Result["people"]!.AsArray().Select(person => (string?)person!["external_id"]));
        Assert.True((bool)running.Result["has_more"]!);

        account.Finish("srch-1");
        var finished = await RunAsync(api, campaign, "audience.search_status", new JsonObject { ["search"] = first.Id, ["limit"] = 100 });
        Assert.Equal(("finished", "p-3", false), ((string?)finished.Result!["state"], (string?)finished.Result["people"]!.AsArray().Single()!["external_id"], (bool)finished.Result["has_more"]!));

        var members = await api.RecordAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, "from-a-search", new { campaign_id = campaign }, Ct);
        Assert.Equal(["1", "2", "3"], members.Items.Select(member => member.Contact.LastName).Order(StringComparer.Ordinal));
        var audience = new AudienceRef((string)first.Context!["audience"]!["id"]!, 1);
        Assert.All(members.Items, member => Assert.Equal((first.Id, audience), (member.Source!.Search, member.Source.Audience)));
        Assert.Equal(1, await EndedAsync(api, first.Id));

        var second = await ApprovedSearchAsync(api, account, campaign, new JsonObject { ["industries"] = new JsonArray("Logistics") });
        await api.RecordAsync<AudiencePreviewResponse>(Operations.AudiencePreview, "by-number", new { campaign_id = campaign, audience = 2 }, Ct);
        var stopped = await RunAsync(api, campaign, "audience.search_cancel", new JsonObject { ["search"] = second.Id });
        Assert.Equal("""{"items":[{"status":"cancelled","ended_as":null}]}""", stopped.Result!.ToJsonString());
        var again = await RunAsync(api, campaign, "audience.search_cancel", new JsonObject { ["search"] = second.Id });
        Assert.Equal("""{"items":[{"status":"already_ended","ended_as":"cancelled"}]}""", again.Result!.ToJsonString());
    }

    /// <summary>
    /// A provider that finishes a search inside its call answers every person it found and keeps no reference: they land
    /// from the answer itself and the search ends there, so there is nothing to read afterwards, and a status read of that
    /// search is refused at the claim, before any child runs. And a
    /// campaign's read is asked only of the plugin its route names, which here performs none.
    /// </summary>
    [Fact]
    public async Task A_search_finished_inside_its_call_answers_its_people_and_has_no_status_to_read()
    {
        using var account = new StandInAccount();
        await using var api = await StartAsync(account);
        var campaign = await CampaignAsync(api, "Autumn search");
        await TestRoutes.SetCampaignAsync(api, campaign, null, Finishing, null, Ct);

        var search = await ApprovedSearchAsync(api, account, campaign);
        Assert.Equal("finished", (string?)search.Result!["items"]![0]!["status"]);
        Assert.Null((string?)search.Result["items"]![0]!["reference"]);
        Assert.Equal(["p-1", "p-2"], search.Result["items"]![0]!["people"]!.AsArray().Select(person => (string?)person!["external_id"]));

        var members = await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, new { campaign_id = campaign }, Ct);
        Assert.Equal(2, members.Items.Count);
        Assert.All(members.Items, member => Assert.Equal(search.Id, member.Source!.Search));
        Assert.Equal(1, await EndedAsync(api, search.Id));

        var status = await RunAsync(api, campaign, "audience.search_status", new JsonObject { ["search"] = search.Id, ["limit"] = 10 });
        Assert.Equal(WorkItemStatus.Failed, status.Status);
        Assert.Equal(AttemptErrors.SearchReferenceAbsent, status.LastError!.Code);
        Assert.Empty(account.Started);

        Assert.Equal(
            (HttpStatusCode.Conflict, "plugin_operation_unsupported"),
            Refusal(await api.PostAsync(Operations.AudienceAvailability, new { campaign_id = campaign }, Ct)));
    }

    /// <summary>
    /// Archiving is how a person stops a campaign, so it stops the campaign's searches too: the archive cancels the
    /// campaign's open work, a read of the search among it but never a stop, and the next scan makes one stop for each
    /// search in flight, which is claimed in the archived campaign as a stop is in a paused one. The provider's search is
    /// cancelled and its row ends <c>cancelled</c>, said once; no second stop is made, and nothing else of the campaign
    /// runs again. A search the person deleted at the provider meanwhile is gone there: its stop fails
    /// <c>search_not_found</c> and ends the row <c>cancelled</c> all the same, said once, by that stop. Either way the
    /// search then holds its account no longer: moving the only route off it, refused while the search was followed, goes
    /// through.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archiving_a_campaign_stops_each_of_its_searches_in_flight(bool deletedAtTheProvider)
    {
        using var account = new StandInAccount();
        await using var api = await StartAsync(account);
        var campaign = await CampaignAsync(api, "Spring search");
        var search = await ApprovedSearchAsync(api, account, campaign);
        var read = await CreateAsync(api, campaign, "audience.search_status", new JsonObject { ["search"] = search.Id, ["limit"] = 2 });

        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = campaign, actor = new { type = "human", id = "ada" } }, Ct);
        Assert.Equal(WorkItemStatus.Cancelled, (await ReadAsync(api, read)).Status);
        if (deletedAtTheProvider)
        {
            account.Delete("srch-1");
        }

        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(Finishing), Ct);
        var refused = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);
        Assert.Equal(RouteProblemCodes.OrphansSearch, Assert.Single(refused.Details!).Code);

        await ScanAsync(api);
        await ScanAsync(api);

        var stop = Assert.Single((await api.PostOkAsync<Page<WorkItemDto>>(Operations.WorkItemList, new { campaign_id = campaign }, Ct)).Items, item => item.Operation == "audience.search_cancel");
        Assert.Equal(
            (deletedAtTheProvider ? WorkItemStatus.Failed : WorkItemStatus.Succeeded, deletedAtTheProvider ? "search_not_found" : null, "dispatcher"),
            (stop.Status, stop.LastError?.Code, stop.CreatedBy.Id));
        Assert.Equal(["srch-1"], account.Started);
        Assert.Equal([stop.Id], await EndedByAsync(api, search.Id));
        using (var scope = api.Resolve<IServiceScopeFactory>().CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<JasonDbContext>().AudienceSearches.AsNoTracking().SingleAsync(Ct);
            Assert.Equal("cancelled", row.EndedAs);
        }

        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        Assert.Equal(Finishing, api.Resolve<RouteRegistry>().Snapshot.Global.Default!.PluginId);
    }

    private static (HttpStatusCode Status, string? Code) Refusal((HttpStatusCode Status, string Body) answer) =>
        (answer.Status, (string?)JsonNode.Parse(answer.Body)!["error"]!["code"]);

    /// <summary>A campaign's read asked the wrong way, as written, and the faults the runtime names for it.</summary>
    public static TheoryData<string, string, string[]> RefusedReads => new()
    {
        { Operations.AudiencePreview, """{"audience":1,"definition":{"industries":["Software"]}}""", ["definition:conflict"] },
        { Operations.AudiencePreview, "{}", ["definition:required"] },
        { Operations.AudiencePreview, """{"audience":0}""", ["audience:invalid"] },
        { Operations.AudiencePreview, """{"audience":1,"people_per_company":0}""", ["people_per_company:invalid"] },
        { Operations.AudiencePreview, """{"audience":1,"people_per_company":101}""", ["people_per_company:invalid"] },
        { Operations.AudienceFilterValues, "{}", ["filter:required"] },
        { Operations.AudienceFilterValues, """{"filter":"Industries"}""", ["filter:invalid"] },
        { Operations.AudienceFilterValues, $$"""{"filter":"industries","query":"{{new string('a', 201)}}"}""", ["query:too_long"] },
        { Operations.AudienceFilterValues, """{"filter":"industries","limit":0}""", ["limit:invalid"] },
        { Operations.AudienceFilterValues, """{"filter":"industries","limit":101}""", ["limit:invalid"] },
    };

    /// <summary>
    /// A campaign's read asked the wrong way is refused by the field at fault before any plugin is asked: a preview names
    /// its audience by its number or by its definition, exactly one of them, and takes from 1 to 100 people per company;
    /// a filter's values name the filter in the provider's lowercase, a query of at most 200 characters and from 1 to 100
    /// values.
    /// </summary>
    [Theory]
    [MemberData(nameof(RefusedReads))]
    public async Task A_campaign_s_read_asked_the_wrong_way_is_refused_by_the_field_at_fault(string operation, string asked, string[] faults)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var body = JsonNode.Parse(asked)!.AsObject();
        body["campaign_id"] = await CampaignAsync(api, "Spring search");

        var error = await api.PostErrorAsync(operation, body, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(faults, error.Details!.Select(detail => $"{detail.Field}:{detail.Code}"));
    }

    private static async Task<RuntimeApiFixture> StartAsync(StandInAccount account)
    {
        // A clock that stands still, so the people a search lands are recorded at one moment; the poll then never comes due
        // by itself, and every read here is one the test made.
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            clock: new FixedClock(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero)),
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, Idle);
                TestPlugins.Write(
                    paths,
                    Keeping,
                    TestPlugins.Manifest(
                        Keeping,
                        "[campaign.get, audience.preview, audience.search, audience.search_status, audience.search_cancel, audience.filter_values, audience.availability]",
                        $"capabilities:\n  http:\n    hosts: [\"{account.HostKey}\"]\n" + Definition),
                    KeepingMain(account.BaseUrl));
                TestPlugins.Grant(paths, Keeping, http: [account.HostKey]);
                TestPlugins.Write(paths, Finishing, TestPlugins.Manifest(Finishing, "[audience.search, audience.search_status, audience.search_cancel]", Definition), FinishingMain);
                TestPlugins.Write(paths, Sizing, TestPlugins.Manifest(Sizing, "[audience.preview, audience.filter_values, audience.availability]", Definition), SizingMain);
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(Keeping));
            },
            configureServices: services =>
            {
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
                services.AddSingleton(TestPlugins.SearchPath);
            });

        return await DispatchHarness.ScannedOnceAsync(fixture, Ct);
    }

    private static async Task<string> CampaignAsync(RuntimeApiFixture api, string name)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        return campaign.Id;
    }

    /// <summary>
    /// A search for fifty people in software, or under <paramref name="definition"/>, parked for a person before the
    /// provider hears of it, approved, and run: the item as it ended.
    /// </summary>
    private static async Task<WorkItemDto> ApprovedSearchAsync(RuntimeApiFixture api, StandInAccount account, string campaign, JsonObject? definition = null)
    {
        definition ??= Software;
        var started = account.Started.Count;
        var item = await CreateAsync(api, campaign, "audience.search", new JsonObject { ["definition"] = definition.DeepClone(), ["count"] = 50 });
        Assert.Equal(0, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await ReadAsync(api, item)).Status);
        Assert.Equal(started, account.Started.Count);

        var approval = (await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items.Single(waiting => waiting.WorkItemId == item);
        var subject = (await api.PostOkAsync<ApprovalDto>(Operations.ApprovalGet, new { approval_id = approval.Id }, Ct)).Subject;
        Assert.True(JsonNode.DeepEquals(definition, subject["input"]!["definition"]));
        await api.PostOkAsync<ApprovalDto>(Operations.ApprovalApprove, new { approval_id = approval.Id, actor = new { type = "human", id = "ada" } }, Ct);

        await ScanAsync(api);
        var read = await ReadAsync(api, item);
        Assert.Equal(WorkItemStatus.Succeeded, read.Status);
        return read;
    }

    /// <summary>
    /// The item made and run: a campaign runs one item a scan, and a search that ended summons the manager's review, which
    /// may take the campaign's turn first, so the scan is repeated until the item has run.
    /// </summary>
    private static async Task<WorkItemDto> RunAsync(RuntimeApiFixture api, string campaign, string operation, JsonObject input)
    {
        var item = await CreateAsync(api, campaign, operation, input);
        var read = await ReadAsync(api, item);
        for (var scan = 0; scan < 3 && read.Status == WorkItemStatus.Created; scan++)
        {
            await ScanAsync(api);
            read = await ReadAsync(api, item);
        }

        return read;
    }

    private static async Task<string> CreateAsync(RuntimeApiFixture api, string campaign, string operation, JsonObject input) =>
        (await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "provider_op", operation, context = new JsonObject { ["input"] = input } },
            Ct)).Id;

    private static async Task ScanAsync(RuntimeApiFixture api)
    {
        await api.Resolve<ScanRunner>().ScanOnceAsync(Ct);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(30)));
    }

    /// <summary>How many audiences the runtime holds, every campaign's.</summary>
    private static async Task<int> AudiencesAsync(RuntimeApiFixture api)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<JasonDbContext>().Audiences.CountAsync(Ct);
    }

    /// <summary>How many lines say the search ended.</summary>
    private static async Task<int> EndedAsync(RuntimeApiFixture api, string search)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<JasonDbContext>().Journal
            .CountAsync(line => line.Kind == JournalKinds.AudienceSearchEnded && line.Key == search, Ct);
    }

    /// <summary>The work item each line that says the search ended names as the one that ended it.</summary>
    private static async Task<List<string?>> EndedByAsync(RuntimeApiFixture api, string search)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var lines = await scope.ServiceProvider.GetRequiredService<JasonDbContext>().Journal.AsNoTracking()
            .Where(line => line.Kind == JournalKinds.AudienceSearchEnded && line.Key == search)
            .ToListAsync(Ct);
        return [.. lines.Select(line => (string?)line.New!["ended_by"])];
    }

    private static Task<WorkItemDto> ReadAsync(RuntimeApiFixture api, string item) =>
        api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);

    /// <summary>
    /// The provider that keeps its searches: the reads are answered in the call, and a search, its status and its stop go
    /// to the stand-in account at <paramref name="baseUrl"/>.
    /// </summary>
    private static string KeepingMain(string baseUrl) => People + $$"""
        const base = "{{baseUrl}}";
        function call(method, path, body) {
          const request = { method, url: base + path, headers: { "Content-Type": "application/json" } };
          if (body !== undefined) { request.body = JSON.stringify(body); }
          return JSON.parse(host.http(request).body);
        }
        function sample(n) {
          const shown = person(n);
          delete shown.channels;
          shown.external_id = null;
          return shown;
        }
        export function invoke(operation, input) {
          switch (operation) {
            case "audience.preview":
              return { result: { count: 1840, companies: 412, sample: [sample(1), sample(2)] } };
            case "audience.filter_values":
              return { result: { values: ["Software", "Software Development", "Hardware"].filter(v => v.startsWith(input.args.query)).slice(0, input.args.limit) } };
            case "audience.availability":
              return { result: { available: true, reason: null, detail: null, credits: null, headroom: null, vendor: { status: null } } };
            case "audience.search": {
              const started = call("POST", "searches", { definition: input.definition, count: input.args.count });
              return { result: { items: [{ status: "accepted", reference: started.reference, found: null, people: [] }] }, external_ids: { search: started.reference } };
            }
            case "audience.search_status": {
              const search = call("GET", "searches/" + input.search.reference);
              if (search.gone) { throw host.fail({ class: "permanent", code: "search_not_found", message: "no such search" }); }
              const from = input.search.cursor === null ? 0 : Number(input.search.cursor.slice("after-".length));
              const shown = Math.min(search.found - from, input.args.limit);
              const people = [];
              for (let n = from + 1; n <= from + shown; n++) { people.push(person(n)); }
              return { result: { state: search.state, found: search.found, people, cursor: "after-" + (from + shown), has_more: search.found > from + shown, vendor: { status: search.state } } };
            }
            case "audience.search_cancel": {
              const answer = call("POST", "searches/" + input.search.reference + "/cancel", {});
              if (answer.gone) { throw host.fail({ class: "permanent", code: "search_not_found", message: "no such search" }); }
              return { result: { items: [{ status: answer.status, ended_as: answer.ended_as }] } };
            }
          }
          throw new Error("not performed here: " + operation);
        }
        """;

    /// <summary>The provider that sizes audiences and searches none: one person, one value, and an account that may not search.</summary>
    private const string SizingMain = """
        export function invoke(operation, input) {
          switch (operation) {
            case "audience.preview":
              return { result: { count: 1, companies: 1, sample: [] } };
            case "audience.filter_values":
              return { result: { values: ["Elsewhere"] } };
            case "audience.availability":
              return { result: { available: false, reason: "not_included", detail: null, credits: null, headroom: null, vendor: { status: null } } };
          }
          throw new Error("not performed here: " + operation);
        }
        """;

    /// <summary>The provider that finishes every search inside its call, with the two people it found.</summary>
    private const string FinishingMain = People + """
        export function invoke(operation, input) {
          return { result: { items: [{ status: "finished", reference: null, found: 2, people: [person(1), person(2)] }] } };
        }
        """;

    /// <summary>
    /// The stand-in account the keeping provider runs its searches at. A search is started at <c>POST /searches</c> and
    /// found three people; it runs until the test finishes it or a stop cancels it, and a stop of a search that has ended
    /// says how it ended. A search the test deletes is gone: a read or a stop of it is answered that there is none.
    /// </summary>
    private sealed class StandInAccount : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentDictionary<string, string> _states = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _started = new();
        private readonly Task _serving;

        public StandInAccount()
        {
            var port = LoopbackNtfy.FreePort();
            HostKey = string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{port}");
            BaseUrl = $"http://{HostKey}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _serving = Task.Run(ServeAsync);
        }

        public string HostKey { get; }

        public string BaseUrl { get; }

        /// <summary>The references of the searches started here, in the order they were started.</summary>
        public IReadOnlyList<string> Started => [.. _started];

        public void Finish(string reference) => _states[reference] = "finished";

        public void Delete(string reference) => _states.TryRemove(reference, out _);

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Close();
            try
            {
                _serving.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // The listener was closed under it; that is how this account stops.
            }

            _stopping.Dispose();
        }

        private string Answer(string method, string path)
        {
            var parts = path.Trim('/').Split('/');
            if (method == "POST" && parts is ["searches"])
            {
                var reference = string.Create(CultureInfo.InvariantCulture, $"srch-{_started.Count + 1}");
                _states[reference] = "in_progress";
                _started.Enqueue(reference);
                return new JsonObject { ["reference"] = reference }.ToJsonString();
            }

            if (parts is ["searches", var named, ..] && !_states.ContainsKey(named))
            {
                return """{"gone":true}""";
            }

            if (method == "GET" && parts is ["searches", var read])
            {
                return new JsonObject { ["state"] = _states[read], ["found"] = 3 }.ToJsonString();
            }

            if (method == "POST" && parts is ["searches", var stop, "cancel"])
            {
                var was = _states[stop];
                _states[stop] = "cancelled";
                return was == "in_progress"
                    ? """{"status":"cancelled","ended_as":null}"""
                    : new JsonObject { ["status"] = "already_ended", ["ended_as"] = was }.ToJsonString();
            }

            throw new InvalidOperationException($"The stand-in account has no {method} {path}.");
        }

        private async Task ServeAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                try
                {
                    var bytes = Encoding.UTF8.GetBytes(Answer(context.Request.HttpMethod, context.Request.Url?.AbsolutePath ?? "/"));
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, _stopping.Token).ConfigureAwait(false);
                    context.Response.Close();
                }
                catch (Exception ex) when (ex is HttpListenerException or IOException or OperationCanceledException or InvalidOperationException or KeyNotFoundException)
                {
                    context.Response.Abort();
                }
            }
        }
    }
}
