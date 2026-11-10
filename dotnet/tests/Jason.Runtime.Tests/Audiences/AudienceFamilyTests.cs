using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
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
    /// not perform; an audience's size and sample; a filter's values; whether the account may search. A definition off the
    /// plugin's schema, and an audience by a number the campaign holds none under, are refused before the provider is
    /// asked. Each exchange is recorded for the clients that render it, and none of them makes an audience.
    /// </summary>
    [Fact]
    public async Task A_campaign_s_reads_answer_through_its_route_and_describe_what_the_routed_plugin_takes()
    {
        using var account = new StandInAccount();
        await using var api = await StartAsync(account);
        var campaign = await CampaignAsync(api, "Spring search");

        var plugin = (await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct)).Plugins.Single(listed => listed.Id == Keeping);
        Assert.Equal("Industries the company is in, as the provider names them.", (string?)plugin.AudienceDefinition!["schema"]!["properties"]!["industries"]!["description"]);
        Assert.Equal("""{"industries":{"side":"company","typeahead":"industries"}}""", plugin.AudienceDefinition["keys"]!.ToJsonString());

        var search = await api.RecordAsync<OperationDescriptionDto>(Operations.OperationDescribe, "audience-search", new { campaign_id = campaign, operation = "audience.search" }, Ct);
        Assert.Equal((Keeping, "1.0.0"), (search.PluginId, search.PluginVersion));
        Assert.Equal("integer", (string?)search.Arguments["properties"]!["count"]!["type"]);
        Assert.True(JsonNode.DeepEquals(plugin.AudienceDefinition, search.Definition));
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

        // Reads are asked of the provider and recorded nowhere: not one search was started by any of them, and the campaign
        // holds no audience.
        Assert.Empty(account.Started);
        Assert.Equal(0, await AudiencesAsync(api));
    }

    /// <summary>
    /// A search a person approves: parked before the provider hears of it, started once approved under the definition the
    /// person read, and answered with the provider's reference, which the status read and the stop then act by. The
    /// status follows the provider until it finishes; a stop of a running search cancels it, and a second stop finds it
    /// ended, which is a success too. The two searches run under two definitions, the campaign's audiences 1 and 2, and a
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
        Assert.Equal(("finished", 3, false), ((string?)finished.Result!["state"], finished.Result["people"]!.AsArray().Count, (bool)finished.Result["has_more"]!));

        var second = await ApprovedSearchAsync(api, account, campaign, new JsonObject { ["industries"] = new JsonArray("Logistics") });
        await api.RecordAsync<AudiencePreviewResponse>(Operations.AudiencePreview, "by-number", new { campaign_id = campaign, audience = 2 }, Ct);
        var stopped = await RunAsync(api, campaign, "audience.search_cancel", new JsonObject { ["search"] = second.Id });
        Assert.Equal("""{"items":[{"status":"cancelled","ended_as":null}]}""", stopped.Result!.ToJsonString());
        var again = await RunAsync(api, campaign, "audience.search_cancel", new JsonObject { ["search"] = second.Id });
        Assert.Equal("""{"items":[{"status":"already_ended","ended_as":"cancelled"}]}""", again.Result!.ToJsonString());
    }

    /// <summary>
    /// A provider that finishes a search inside its call answers every person it found and keeps no reference, so there
    /// is nothing to read afterwards: a status read of that search is refused at the claim, before any child runs. And a
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

        var status = await RunAsync(api, campaign, "audience.search_status", new JsonObject { ["search"] = search.Id, ["limit"] = 10 });
        Assert.Equal(WorkItemStatus.Failed, status.Status);
        Assert.Equal(AttemptErrors.SearchReferenceAbsent, status.LastError!.Code);
        Assert.Empty(account.Started);

        Assert.Equal(
            (HttpStatusCode.Conflict, "plugin_operation_unsupported"),
            Refusal(await api.PostAsync(Operations.AudienceAvailability, new { campaign_id = campaign }, Ct)));
    }

    private static (HttpStatusCode Status, string? Code) Refusal((HttpStatusCode Status, string Body) answer) =>
        (answer.Status, (string?)JsonNode.Parse(answer.Body)!["error"]!["code"]);

    private static async Task<RuntimeApiFixture> StartAsync(StandInAccount account)
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
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
                TestPlugins.Write(paths, Finishing, TestPlugins.Manifest(Finishing, "[audience.search, audience.search_status]", Definition), FinishingMain);
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

    private static async Task<WorkItemDto> RunAsync(RuntimeApiFixture api, string campaign, string operation, JsonObject input)
    {
        var item = await CreateAsync(api, campaign, operation, input);
        await ScanAsync(api);
        return await ReadAsync(api, item);
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
              const shown = Math.min(search.found, input.args.limit);
              const people = [];
              for (let n = 1; n <= shown; n++) { people.push(person(n)); }
              return { result: { state: search.state, found: search.found, people, cursor: "after-" + shown, has_more: search.found > shown, vendor: { status: search.state } } };
            }
            case "audience.search_cancel": {
              const answer = call("POST", "searches/" + input.search.reference + "/cancel", {});
              return { result: { items: [{ status: answer.status, ended_as: answer.ended_as }] } };
            }
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
    /// says how it ended.
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
