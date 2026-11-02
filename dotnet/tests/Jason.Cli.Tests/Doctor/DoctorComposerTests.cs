using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Doctor;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Configuration;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Tests;
using static Jason.Cli.Tests.Logs.LogFixtures;

namespace Jason.Cli.Tests.Doctor;

/// <summary>
/// What a diagnostic bundle is made of: nine sections in one order, each bounded, composed from reads whose values are
/// dropped in this process, with the installation's names rewritten and exception text capped last. Everything here
/// runs against a runtime that exists only in a handler, on a temporary data directory, with names and variables of
/// the test's own — so nothing is learned about the machine running the suite, and nothing reaches a network.
/// </summary>
public sealed class DoctorComposerTests
{
    private const string Token = "SENTINEL-TOKEN-5b1e9c";
    private const string BaseUrl = "http://127.0.0.1:47811";
    private const string InstanceId = "rt_01JDOCTOR";

    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The installation's names, made up: unlikely enough that none of them is in the real temporary directory a bundle
    /// here names, so what is rewritten is only what a test planted — and the data directory, which is outside this home.
    /// </summary>
    private static readonly IdentityFacts MadeUp = new(@"C:\Users\Quillon", "Quillon", "WORKSTATION-7", "CORP", "S-1-5-21-1111111111-2222222222-3333333333-1001");

    /// <summary>Every value the reads hand this process that no bundle may carry unless the journal was asked for.</summary>
    private static readonly string[] JournalSentinels = ["SENTINEL_KEY", "SENTINEL_OLD", "SENTINEL_CONTEXT", "SENTINEL_CONTACT_FIELD", "SENTINEL_DECISION", "SENTINEL_REASON", "SENTINEL_ACTOR_ID"];

    /// <summary>And those no bundle ever carries, whatever it was asked.</summary>
    private static readonly string[] NeverSentinels =
    [
        Token, "SENTINEL_WI_CONTEXT", "SENTINEL_WI_RESULT", "SENTINEL_ERROR_MESSAGE", "SENTINEL_CREATOR", "SENTINEL_PROBLEM_MESSAGE", "SENTINEL_CONTACT_NAME",
        "SENTINEL_SETTING_FEED", "SENTINEL_SETTING_COMMAND", "SENTINEL_ENV_FEED", "SENTINEL_ENV_NAME",
    ];

    private static readonly string[] Reads =
        [Operations.SystemInfo, Operations.RoleList, Operations.RouteList, Operations.PluginList, Operations.JournalList, Operations.WorkItemList, Operations.WorkItemGet];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- the sections, in order, with their bounds --------------------------------------------------------------

    [Fact]
    public async Task Every_section_is_written_in_its_order_and_the_bundle_names_what_it_did()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Equal(["bundle", "versions", "status", "runtime", "routes", "config", "logs", "journal", "work_items"], Keys(bundle.Document));

        var header = Section(bundle, "bundle");
        Assert.Equal(
            [
                "format", "created_at", "cli_version", "include_journal", "home_written_as", "data_written_as", "runtime_data_written_as",
                "harness_root_written_as", "install_dir_written_as", "user_written_as", "machine_written_as", "domain_written_as", "sid_written_as", "plugin_id_written_as",
                "role_written_as", "skill_pack_written_as", "skill_source_written_as", "aliases_numbered", "exception_text", "projected", "contents",
                "values_carried", "values_carried_truncated", "never_carried", "warnings",
            ],
            Keys(header));
        Assert.Equal("jason-doctor/1", Text(header["format"]));
        Assert.Equal("2026-10-04T12:00:00.000Z", Text(header["created_at"]));
        Assert.Equal(Jason.Contracts.JasonVersion.Current, Text(header["cli_version"]));
        Assert.False(header["include_journal"]!.GetValue<bool>());
        Assert.Equal("~", Text(header["home_written_as"]));
        Assert.Equal("<data>", Text(header["data_written_as"]));
        Assert.Equal("<runtime_data>", Text(header["runtime_data_written_as"]));
        Assert.Equal("<harness_root>", Text(header["harness_root_written_as"]));
        Assert.False(header["values_carried_truncated"]!.GetValue<bool>());
        Assert.Equal("<user>", Text(header["user_written_as"]));
        Assert.Equal("<machine>", Text(header["machine_written_as"]));
        Assert.Equal("<domain>", Text(header["domain_written_as"]));
        Assert.Equal("<sid>", Text(header["sid_written_as"]));
        Assert.Equal(ExceptionText.MaxLineCharacters, header["exception_text"]!["max_line_characters"]!.GetValue<int>());
        Assert.Equal(ExceptionText.MaxMessageCharacters, header["exception_text"]!["max_message_characters"]!.GetValue<int>());
        Assert.Equal(ExceptionText.MaxFrames, header["exception_text"]!["max_frames"]!.GetValue<int>());
        Assert.Empty(header["warnings"]!.AsArray());
        Assert.Empty(bundle.Warnings);
        Assert.Contains("the capability token", Strings(header["never_carried"]));
        Assert.Contains("journal values unless --include-journal: what the journal entries say: campaign names and fields as they changed, project names as they changed, the folders a project link recorded, campaign and work-item contexts as they changed, result formats, the addresses and numbers put on or taken off the do-not-contact register, the accounts a campaign's routes bind, the options decisions were answered with, the errors attempts ended with, provider identifiers, the effect, tool and operation a report names, the failure text and receipts notification channels reported, entries people and agents wrote themselves, the names actors gave, and the reasons they typed", Strings(header["never_carried"]));
        Assert.Equal(bundle.NeverCarried, Strings(header["never_carried"]));

        // The listing is the sections after the header, each with the size it has in the file.
        Assert.Equal(["versions", "status", "runtime", "routes", "config", "logs", "journal", "work_items"], bundle.Contents.Select(section => section.Name));
        Assert.Equal(bundle.Contents.Select(section => section.Name), header["contents"]!.AsArray().Select(entry => Text(entry!["name"])));
        AssertSizesAreTheFilesOwn(bundle);
    }

    [Fact]
    public async Task Versions_say_which_cli_runtime_api_system_and_dotnet_this_is()
    {
        using var dir = new TempPaths();

        var versions = Section(await ComposeAsync(dir, Running(dir)), "versions");

        Assert.Equal(["cli", "runtime", "api", "os", "os_architecture", "process_architecture", "dotnet"], Keys(versions));
        Assert.Equal(Jason.Contracts.JasonVersion.Current, Text(versions["cli"]));
        Assert.Equal("0.3.0-dev", Text(versions["runtime"]));
        Assert.Equal("v1", Text(versions["api"]));
        Assert.False(string.IsNullOrEmpty(Text(versions["os"])));
        Assert.False(string.IsNullOrEmpty(Text(versions["dotnet"])));
    }

    [Fact]
    public async Task Status_is_the_report_the_status_verb_composes_and_runs_no_program()
    {
        using var dir = new TempPaths();
        var programs = new FakeProgramRunner();

        var status = Section(await ComposeAsync(dir, Running(dir), programs: programs), "status");

        Assert.Equal(["ready", "checks"], Keys(status));
        Assert.Contains(status["checks"]!.AsArray(), check => Text(check!["name"]) == "runtime" && Text(check["state"]) == "ok");
        Assert.Contains(status["checks"]!.AsArray(), check => Text(check!["name"]) == "provider_cli" && Text(check["state"]) == "absent");
        Assert.Empty(programs.Requested);
    }

    /// <summary>
    /// The status section names the plugin the runtime holds back by the words the runtime section already carries — its id,
    /// written as the alias the runtime section writes it as since this repository does not ship it, and its codes — and
    /// never by its problem's message, the place in its manifest, or where its package lies.
    /// </summary>
    [Fact]
    public async Task Status_names_an_unavailable_plugin_by_the_words_the_runtime_section_carries_and_no_more()
    {
        using var dir = new TempPaths();

        var bundle = await ComposeAsync(dir, Running(dir));

        var check = Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "plugin_availability")!;
        Assert.Equal("failed", Text(check["state"]));
        Assert.False(check["required"]!.GetValue<bool>());
        Assert.StartsWith("<plugin-2> is unavailable; its problems: executable_missing. ", Text(check["fact"]), StringComparison.Ordinal);
        Assert.DoesNotContain("<plugin-1>", Text(check["fact"]), StringComparison.Ordinal);

        var notifier = Section(bundle, "runtime")["plugins"]!.AsArray().Single(plugin => Text(plugin!["id"]) == "<plugin-2>")!;
        Assert.Equal(["executable_missing"], Strings(notifier["problem_codes"]));
        AssertAbsent(bundle, "SENTINEL_PROBLEM_MESSAGE", "capabilities.exec.executables", "/plugins/notifier");
    }

    [Fact]
    public async Task The_runtime_section_has_system_info_plugins_by_their_codes_and_whether_the_data_directory_agrees()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);

        var section = Section(await ComposeAsync(dir, runtime), "runtime");

        Assert.Equal(["answered", "system_info", "plugins", "plugins_truncated", "data_dir_agrees"], Keys(section));
        Assert.True(section["answered"]!.GetValue<bool>());
        Assert.Equal(InstanceId, Text(section["system_info"]!["instance_id"]));
        Assert.Equal(4242, section["system_info"]!["pid"]!.GetValue<int>());
        Assert.True(section["data_dir_agrees"]!.GetValue<bool>());
        Assert.False(section["plugins_truncated"]!.GetValue<bool>());

        var plugins = section["plugins"]!.AsArray();
        Assert.Equal(2, plugins.Count);
        foreach (var plugin in plugins)
        {
            Assert.Equal(["id", "version", "digest", "status", "problem_codes", "problem_codes_truncated"], Keys(plugin!));
        }

        var unavailable = plugins.Single(plugin => Text(plugin!["id"]) == "<plugin-2>")!;
        Assert.Equal("unavailable", Text(unavailable["status"]));
        Assert.Equal("sha256:9a8b", Text(unavailable["digest"]));
        Assert.Equal(["executable_missing"], Strings(unavailable["problem_codes"]));
    }

    [Theory]
    [InlineData("the same directory", true)]
    [InlineData("the same directory with a separator after it", true)]
    [InlineData("another directory", false)]
    public async Task Data_dir_agrees_says_whether_the_runtime_answering_owns_this_data_directory(string which, bool agrees)
    {
        using var dir = new TempPaths();
        var reported = which switch
        {
            "the same directory" => dir.Paths.Root,
            "the same directory with a separator after it" => dir.Paths.Root + Path.DirectorySeparatorChar,
            _ => Path.Combine(Path.GetTempPath(), "jason-cli-tests", "somebody-elses"),
        };

        var section = Section(await ComposeAsync(dir, Running(dir, dataDir: reported)), "runtime");

        Assert.Equal(agrees, section["data_dir_agrees"]!.GetValue<bool>());
    }

    [Fact]
    [Critical(Family.S4, "journal metadata only by default")]
    public async Task The_journal_is_counted_over_its_window_by_kind_and_its_recent_entries_carry_metadata_alone()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.JournalTotal = 120;

        var journal = Section(await ComposeAsync(dir, runtime), "journal");

        Assert.Equal(["window", "counted", "truncated", "by_kind", "recent"], Keys(journal));
        Assert.Equal("2026-09-27T12:00:00.000Z", Text(journal["window"]!["since"]));
        Assert.Equal(7, journal["window"]!["days"]!.GetValue<int>());
        Assert.Equal(120, journal["counted"]!.GetValue<int>());
        Assert.False(journal["truncated"]!.GetValue<bool>());
        Assert.Equal(120, journal["by_kind"]!.AsObject().Sum(kind => kind.Value!.GetValue<int>()));
        Assert.Equal(30, journal["by_kind"]!["decision_made"]!.GetValue<int>());

        var recent = journal["recent"]!.AsArray();
        Assert.Equal(DoctorComposer.RecentEntries, recent.Count);
        Assert.Equal(Text(runtime.Entry(0)["ts"]), Text(recent[0]!["ts"]));
        foreach (var entry in recent)
        {
            Assert.Equal(["ts", "kind", "campaign_id", "work_item_id", "attempt_id", "actor_type"], Keys(entry!));
        }

        var asked = runtime.Requests.First(request => request.Operation == Operations.JournalList).Body;
        Assert.Equal("2026-09-27T12:00:00.000Z", Text(asked["since"]));
        Assert.Equal(DoctorComposer.PageSize, asked["limit"]!.GetValue<int>());
    }

    /// <summary>Ten pages of a thousand, and no more requests than that: the cap bounds the calls as well as the count.</summary>
    [Fact]
    public async Task The_journal_and_the_work_items_stop_at_ten_pages_and_say_they_were_cut()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.JournalTotal = 12_000;
        runtime.WorkItemTotal = 10_500;

        var bundle = await ComposeAsync(dir, runtime);

        var journal = Section(bundle, "journal");
        Assert.Equal(10_000, journal["counted"]!.GetValue<int>());
        Assert.True(journal["truncated"]!.GetValue<bool>());
        Assert.Equal(DoctorComposer.MaxPages, runtime.Requests.Count(request => request.Operation == Operations.JournalList));

        var workItems = Section(bundle, "work_items");
        Assert.Equal(10_000, workItems["counted"]!.GetValue<int>());
        Assert.True(workItems["truncated"]!.GetValue<bool>());
        Assert.Equal(10_000, workItems["by_kind_and_status"]!.AsObject().Sum(kind => kind.Value!.AsObject().Sum(status => status.Value!.GetValue<int>())));
    }

    /// <summary>
    /// The newest failures are the journal's: its pages are read newest first, so the first twenty distinct items it says
    /// failed or expired are the twenty newest, each then read by itself — and of each, codes and never a message.
    /// </summary>
    [Fact]
    public async Task The_newest_failures_are_the_journals_newest_failed_or_expired_items_each_read_by_itself()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.WorkItemTotal = 400;

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        Assert.Equal(
            ["counted", "truncated", "by_kind_and_status", "recent_failures", "recent_failures_from", "recent_failures_window", "recent_failures_truncated"],
            Keys(workItems));
        Assert.Equal(400, workItems["counted"]!.GetValue<int>());
        Assert.Equal(50, workItems["by_kind_and_status"]!["provider_op"]!["failed"]!.GetValue<int>());
        Assert.Equal(50, workItems["by_kind_and_status"]!["ai_role"]!["expired"]!.GetValue<int>());
        Assert.Equal(DoctorComposer.FromJournal, Text(workItems["recent_failures_from"]));
        Assert.Equal("2026-09-27T12:00:00.000Z", Text(workItems["recent_failures_window"]!["since"]));

        // The journal names thirty failed items and twenty are read: ten more may be failures, so the list is cut short.
        Assert.True(workItems["recent_failures_truncated"]!.GetValue<bool>());

        // Every fourth entry of the stub's journal says an item failed, newest first.
        var newest = Enumerable.Range(0, DoctorComposer.RecentFailures).Select(k => StubRuntime.WorkItemId(3 + (4 * k))).ToList();
        var failures = workItems["recent_failures"]!.AsArray();
        Assert.Equal(newest, failures.Select(failure => Text(failure!["id"])));
        Assert.Equal(newest, runtime.Requests.Where(request => request.Operation == Operations.WorkItemGet).Select(request => Text(request.Body["work_item_id"])));
        Assert.DoesNotContain(runtime.Requests, request => request.Operation == Operations.WorkItemList && request.Body.ContainsKey("status"));

        foreach (var failure in failures)
        {
            Assert.Equal(["id", "campaign_id", "kind", "operation", "role", "status", "attempt_count", "last_error", "finished_at"], Keys(failure!));
            Assert.Equal(["code", "class"], Keys(failure!["last_error"]!));
        }

        var provider = failures.First(failure => Text(failure!["kind"]) == "provider_op")!;
        Assert.Equal("contact.add", Text(provider["operation"]));
        Assert.Null(provider["role"]);
        Assert.Equal("provider_refused", Text(provider["last_error"]!["code"]));
        Assert.Equal("permanent", Text(provider["last_error"]!["class"]));
        Assert.Contains(failures, failure => Text(failure!["kind"]) == "ai_role" && Text(failure["role"]) == "researcher");
    }

    /// <summary>Twelve thousand failures, more than ten pages of anything: the first one listed is still the newest.</summary>
    [Fact]
    public async Task Past_ten_thousand_failures_the_first_listed_is_the_newest()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        const int Failed = 12_000;
        runtime.WorkItemTotal = Failed;
        runtime.Fails = (operation, _) =>
        {
            if (operation != Operations.JournalList)
            {
                return null;
            }

            // Newest first: the nth entry is the failure of item 11999 - n.
            var offset = StubRuntime.Offset(runtime.Requests[^1].Body);
            var items = new JsonArray();
            for (var n = offset; n < Math.Min(offset + DoctorComposer.PageSize, Failed); n++)
            {
                var entry = runtime.Entry(n);
                entry["kind"] = "workitem_failed";
                entry["work_item_id"] = StubRuntime.WorkItemId(Failed - 1 - n);
                items.Add(entry);
            }

            return Respond(HttpStatusCode.OK, new JsonObject { ["items"] = items, ["next_cursor"] = offset + DoctorComposer.PageSize < Failed ? $"c{offset + DoctorComposer.PageSize}" : null }.ToJsonString());
        };

        var failures = Section(await ComposeAsync(dir, runtime), "work_items")["recent_failures"]!.AsArray();

        Assert.Equal(StubRuntime.WorkItemId(Failed - 1), Text(failures[0]!["id"]));
        Assert.Equal(StubRuntime.WorkItemId(Failed - DoctorComposer.RecentFailures), Text(failures[^1]!["id"]));
    }

    /// <summary>
    /// With no journal to read, the failures come from the listing, which the API gives oldest first: the newest of what
    /// it read are listed, and the section says where they came from.
    /// </summary>
    [Fact]
    public async Task When_the_journal_cannot_be_read_the_failures_come_from_the_listing_and_say_so()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.WorkItemTotal = 400;
        runtime.Fails = (operation, _) => operation == Operations.JournalList ? Envelope(HttpStatusCode.InternalServerError, "internal_error") : null;

        var bundle = await ComposeAsync(dir, runtime);
        var workItems = Section(bundle, "work_items");

        Assert.Equal("internal_error", Text(Section(bundle, "journal")["incomplete"]));
        Assert.Equal(DoctorComposer.FromListOldestFirst, Text(workItems["recent_failures_from"]));
        Assert.False(workItems.ContainsKey("recent_failures_window"));
        Assert.False(workItems["recent_failures_truncated"]!.GetValue<bool>());

        var newest = Enumerable.Range(0, 400)
            .Where(n => StubRuntime.StatusOf(n) is WorkItemStatus.Failed or WorkItemStatus.Expired)
            .OrderByDescending(StubRuntime.FinishedAt)
            .Take(DoctorComposer.RecentFailures)
            .Select(StubRuntime.WorkItemId);
        Assert.Equal(newest, workItems["recent_failures"]!.AsArray().Select(failure => Text(failure!["id"])));
        Assert.Contains(runtime.Requests, request =>
            request.Operation == Operations.WorkItemList && request.Body["status"] is JsonArray statuses && Strings(statuses).SequenceEqual(["failed", "expired"]));
    }

    /// <summary>
    /// A listing cut at its cap is cut short, however many failures it gave: failures it never read may be newer than any
    /// it listed.
    /// </summary>
    [Fact]
    public async Task A_listing_of_failures_cut_at_its_cap_is_cut_short_whatever_it_listed()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.WorkItemTotal = 21_000;
        runtime.Fails = (operation, _) => operation == Operations.JournalList ? Envelope(HttpStatusCode.InternalServerError, "internal_error") : null;

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        Assert.Equal(DoctorComposer.FromListOldestFirst, Text(workItems["recent_failures_from"]));
        Assert.Equal(DoctorComposer.RecentFailures, workItems["recent_failures"]!.AsArray().Count);
        Assert.True(workItems["recent_failures_truncated"]!.GetValue<bool>());
    }

    /// <summary>
    /// Fewer failures than are listed, from a journal that stopped partway, are cut short — and the section says why, since
    /// the failures in the pages it never read are missing from it.
    /// </summary>
    [Fact]
    public async Task Fewer_failures_from_a_journal_that_stopped_partway_are_cut_short_and_say_why()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, call) =>
        {
            if (operation != Operations.JournalList)
            {
                return null;
            }

            if (call > 1)
            {
                return Envelope(HttpStatusCode.InternalServerError, "internal_error");
            }

            var items = new JsonArray([.. Enumerable.Range(0, 3).Select(n => (JsonNode)Failed(runtime.Entry(n), 3 + (4 * n)))]);
            return Respond(HttpStatusCode.OK, new JsonObject { ["items"] = items, ["next_cursor"] = "c3" }.ToJsonString());
        };

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        Assert.Equal(DoctorComposer.FromJournal, Text(workItems["recent_failures_from"]));
        Assert.Equal(3, workItems["recent_failures"]!.AsArray().Count);
        Assert.True(workItems["recent_failures_truncated"]!.GetValue<bool>());
        Assert.Equal("internal_error", Text(workItems["incomplete"]));
    }

    /// <summary>An item that failed and has since been retried and succeeded is not a failure, whatever the journal said of it.</summary>
    [Fact]
    public async Task An_item_that_failed_and_then_succeeded_is_not_listed()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation switch
        {
            Operations.JournalList => Respond(
                HttpStatusCode.OK,
                new JsonObject { ["items"] = new JsonArray(Failed(runtime.Entry(0), 41), Failed(runtime.Entry(1), 42)), ["next_cursor"] = null }.ToJsonString()),
            Operations.WorkItemGet when Text(runtime.Requests[^1].Body["work_item_id"]) == StubRuntime.WorkItemId(41) =>
                Respond(HttpStatusCode.OK, JsonSerializer.Serialize(StubRuntime.Item(41, WorkItemStatus.Succeeded), JasonJson.Options)),
            _ => null,
        };

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        Assert.Equal([StubRuntime.WorkItemId(42)], workItems["recent_failures"]!.AsArray().Select(failure => Text(failure!["id"])));
        Assert.Equal(2, runtime.Requests.Count(request => request.Operation == Operations.WorkItemGet));
    }

    /// <summary>
    /// The key a caller created a work item with is the caller's text, and a bundle carries a work item only as the
    /// closed set of facts it lists: an item answered with a key, listed or read as a recent failure, leaves no part of
    /// the key in the bundle — with the journal flag on too, which adds nothing here because no journal line the runtime
    /// writes carries a key. A campaign is never asked for at all.
    /// </summary>
    [Fact]
    [Critical(Family.S4, "idempotency keys are withheld")]
    public async Task A_key_a_work_item_was_created_with_never_reaches_the_bundle()
    {
        const string Sentinel = "SENTINEL_DOCTORKEY_7f3c";
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation switch
        {
            Operations.JournalList => Respond(
                HttpStatusCode.OK,
                new JsonObject { ["items"] = new JsonArray(Failed(runtime.Entry(0), 42)), ["next_cursor"] = null }.ToJsonString()),
            Operations.WorkItemList => Respond(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(new Page<WorkItemDto>([StubRuntime.Item(42, WorkItemStatus.Failed) with { IdempotencyKey = Sentinel }], null), JasonJson.Options)),
            Operations.WorkItemGet => Respond(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(StubRuntime.Item(42, WorkItemStatus.Failed) with { IdempotencyKey = Sentinel }, JasonJson.Options)),
            _ => null,
        };

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        Assert.Equal([StubRuntime.WorkItemId(42)], Section(bundle, "work_items")["recent_failures"]!.AsArray().Select(failure => Text(failure!["id"])));
        Assert.DoesNotContain("DOCTORKEY", bundle.Document.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(runtime.Requests, request => request.Operation.StartsWith("campaign.", StringComparison.Ordinal));
    }

    /// <summary>An item the journal says failed twice is one failure, read once.</summary>
    [Fact]
    public async Task An_item_the_journal_names_twice_is_read_once()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation == Operations.JournalList
            ? Respond(
                HttpStatusCode.OK,
                new JsonObject { ["items"] = new JsonArray(Failed(runtime.Entry(0), 7), Failed(runtime.Entry(1), 8), Failed(runtime.Entry(2), 7)), ["next_cursor"] = null }.ToJsonString())
            : null;

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        Assert.Equal([StubRuntime.WorkItemId(7), StubRuntime.WorkItemId(8)], workItems["recent_failures"]!.AsArray().Select(failure => Text(failure!["id"])));
        Assert.Equal(2, runtime.Requests.Count(request => request.Operation == Operations.WorkItemGet));
    }

    /// <summary>The first twenty distinct items the journal names are read, and no more, whatever they turn out to be.</summary>
    [Fact]
    public async Task No_more_than_twenty_items_are_read_for_the_newest_failures()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation switch
        {
            Operations.JournalList => Respond(
                HttpStatusCode.OK,
                new JsonObject { ["items"] = new JsonArray([.. Enumerable.Range(0, 30).Select(n => (JsonNode)Failed(runtime.Entry(n), 100 + n))]), ["next_cursor"] = null }.ToJsonString()),
            Operations.WorkItemGet => Respond(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(StubRuntime.Item(int.Parse(Text(runtime.Requests[^1].Body["work_item_id"])[3..], CultureInfo.InvariantCulture), WorkItemStatus.Succeeded), JasonJson.Options)),
            _ => null,
        };

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        Assert.Equal(DoctorComposer.RecentFailures, runtime.Requests.Count(request => request.Operation == Operations.WorkItemGet));
        Assert.Empty(workItems["recent_failures"]!.AsArray());

        // Ten items the journal named were never read, so what is listed may not be all there is.
        Assert.True(workItems["recent_failures_truncated"]!.GetValue<bool>());
    }

    /// <summary>
    /// Five listed, and ten more still failed beyond the twentieth item the journal named: the list is cut short, whatever
    /// its count.
    /// </summary>
    [Fact]
    public async Task Failures_beyond_the_ones_read_make_the_list_cut_short_whatever_it_lists()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation switch
        {
            Operations.JournalList => Respond(
                HttpStatusCode.OK,
                new JsonObject { ["items"] = new JsonArray([.. Enumerable.Range(0, 30).Select(n => (JsonNode)Failed(runtime.Entry(n), 100 + n))]), ["next_cursor"] = null }.ToJsonString()),
            Operations.WorkItemGet => Answer(int.Parse(Text(runtime.Requests[^1].Body["work_item_id"])[3..], CultureInfo.InvariantCulture)),
            _ => null,
        };

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        // The first fifteen named have since succeeded; the rest are still failed, ten of them past the twentieth.
        static HttpResponseMessage Answer(int n) =>
            Respond(HttpStatusCode.OK, JsonSerializer.Serialize(StubRuntime.Item(n, n < 115 ? WorkItemStatus.Succeeded : WorkItemStatus.Failed), JasonJson.Options));

        Assert.Equal(5, workItems["recent_failures"]!.AsArray().Count);
        Assert.Equal(DoctorComposer.RecentFailures, runtime.Requests.Count(request => request.Operation == Operations.WorkItemGet));
        Assert.True(workItems["recent_failures_truncated"]!.GetValue<bool>());
    }

    [Fact]
    public void The_kinds_that_say_an_item_failed_are_kinds_the_runtime_writes()
    {
        Assert.Equal(["workitem_failed", "workitem_expired"], DoctorComposer.FailureKinds);
        Assert.All(DoctorComposer.FailureKinds, kind => Assert.Contains(kind, ManagerTriggerKinds.All));
    }

    [Fact]
    public async Task The_logs_section_lists_the_files_and_holds_the_newest_errors_and_a_slice_oldest_first()
    {
        using var dir = new TempPaths();
        var logs = Directory.CreateDirectory(dir.Paths.LogsDirectory).FullName;
        var lines = new List<string>();
        for (var n = 0; n < 2_300; n++)
        {
            lines.Add(n % 15 == 0 ? Event(At(n), $"failure {n}", "Error") : Event(At(n), $"line {n}"));
        }

        Write(logs, "runtime-20261003.jsonl", [.. lines.Take(1_000)]);
        Write(logs, "runtime-20261004.jsonl", [.. lines.Skip(1_000), "not an event"]);

        var section = Section(await ComposeAsync(dir, Running(dir)), "logs");

        Assert.Equal(
            ["directory", "exists", "files", "files_truncated", "recent_errors", "recent_errors_truncated", "slice", "slice_truncated", "skipped_lines"],
            Keys(section));
        Assert.Equal(Path.Combine("<data>", "logs"), Text(section["directory"]));
        Assert.Equal(["runtime-20261003.jsonl", "runtime-20261004.jsonl"], section["files"]!.AsArray().Select(file => Text(file!["name"])));
        Assert.Equal(["name", "bytes", "modified_at"], Keys(section["files"]![0]!));
        Assert.False(section["files_truncated"]!.GetValue<bool>());

        var errors = section["recent_errors"]!.AsArray();
        Assert.Equal(DoctorComposer.RecentErrors, errors.Count);
        Assert.Equal("failure 2295", Text(errors[^1]!["@mt"]));
        Assert.Equal("failure 810", Text(errors[0]!["@mt"]));
        Assert.True(section["recent_errors_truncated"]!.GetValue<bool>());

        var slice = section["slice"]!.AsArray();
        Assert.Equal(DoctorComposer.SliceEvents, slice.Count);
        Assert.Equal("line 2299", Text(slice[^1]!["@mt"]));
        Assert.Equal("line 301", Text(slice[1]!["@mt"]));
        Assert.True(section["slice_truncated"]!.GetValue<bool>());
        Assert.Equal(1, section["skipped_lines"]!.GetValue<int>());
    }

    /// <summary>A few enormous events end the slice by their size before the event count does.</summary>
    [Fact]
    public async Task The_slice_ends_at_four_mebibytes_before_two_thousand_events()
    {
        using var dir = new TempPaths();
        var logs = Directory.CreateDirectory(dir.Paths.LogsDirectory).FullName;
        Write(logs, "runtime-20261004.jsonl", [.. Enumerable.Range(0, 1_000).Select(n => Event(At(n), new string('w', 10 * 1024) + n))]);

        var section = Section(await ComposeAsync(dir, Running(dir)), "logs");

        var slice = section["slice"]!.AsArray();
        Assert.InRange(slice.Count, 300, 420);
        Assert.True(section["slice_truncated"]!.GetValue<bool>());
        Assert.EndsWith("999", Text(slice[^1]!["@mt"]), StringComparison.Ordinal);
    }

    /// <summary>
    /// Text outside ASCII is written six bytes to a character in the bundle, and the slice is measured where it is written:
    /// it holds as much as four mebibytes of the bundle, not of the log.
    /// </summary>
    [Fact]
    public async Task Text_outside_ascii_fills_the_slice_by_its_size_in_the_bundle()
    {
        using var dir = new TempPaths();
        var logs = Directory.CreateDirectory(dir.Paths.LogsDirectory).FullName;
        var cyrillic = string.Concat(Enumerable.Repeat("Привет", 300));
        Write(logs, "runtime-20261004.jsonl", [.. Enumerable.Range(0, 2_000).Select(n => Event(At(n), cyrillic + n))]);

        var bundle = await ComposeAsync(dir, Running(dir));

        var section = Section(bundle, "logs");
        var logsBytes = bundle.Contents.Single(entry => entry.Name == "logs").Bytes;
        Assert.True(section["slice_truncated"]!.GetValue<bool>());
        Assert.InRange(logsBytes, DoctorComposer.SliceBytes - (64 * 1024), DoctorComposer.SliceBytes + (64 * 1024));
        Assert.EndsWith("1999", Text(section["slice"]!.AsArray()[^1]!["@mt"]), StringComparison.Ordinal);
        Assert.True(bundle.Serialize().Length < DoctorComposer.MaxBundleBytes);
    }

    /// <summary>Errors of most of a mebibyte each: the newest are kept, as many as four mebibytes of the bundle hold.</summary>
    [Fact]
    public async Task Enormous_error_events_are_kept_to_their_budget_in_the_bundle()
    {
        using var dir = new TempPaths();
        var logs = Directory.CreateDirectory(dir.Paths.LogsDirectory).FullName;
        var big = new string('d', 900 * 1024);
        Write(logs, "runtime-20261004.jsonl", [.. Enumerable.Range(0, 70).Select(n => Event(At(n), "A failure {Detail}", "Error", ("Detail", big + n)))]);

        var bundle = await ComposeAsync(dir, Running(dir));

        var section = Section(bundle, "logs");
        var errors = section["recent_errors"]!.AsArray();
        Assert.InRange(errors.Count, 1, 4);
        Assert.EndsWith("69", Text(errors[^1]!["Detail"]), StringComparison.Ordinal);
        Assert.True(section["recent_errors_truncated"]!.GetValue<bool>());
        Assert.True(bundle.Contents.Single(entry => entry.Name == "logs").Bytes <= DoctorComposer.RecentErrorsBytes + DoctorComposer.SliceBytes + DoctorComposer.SectionBytes);
        Assert.True(bundle.Serialize().Length < DoctorComposer.MaxBundleBytes);
    }

    /// <summary>A section larger than its budget is said to be, by its size, rather than written: the bundle keeps its ceiling.</summary>
    [Fact]
    public async Task A_section_larger_than_its_budget_is_said_to_be_too_large_rather_than_written()
    {
        using var dir = new TempPaths();
        var variables = Enumerable.Range(0, 30_000).ToDictionary(n => $"JASON_Variable{n}", _ => "x");

        var bundle = await ComposeAsync(dir, Running(dir), variables: variables);

        var config = Section(bundle, "config");
        Assert.Equal(["omitted", "bytes", "budget"], Keys(config));
        Assert.Equal(DoctorComposer.TooLarge, Text(config["omitted"]));
        Assert.True(config["bytes"]!.GetValue<long>() > DoctorComposer.SectionBytes);
        Assert.Equal(DoctorComposer.SectionBytes, config["budget"]!.GetValue<long>());
        Assert.Equal(0, bundle.Contents.Single(entry => entry.Name == "config").Items);
        Assert.Equal(["ready", "checks"], Keys(Section(bundle, "status")));
        Assert.True(bundle.Serialize().Length < DoctorComposer.MaxBundleBytes);
    }

    /// <summary>
    /// Three hundred megabytes of log, the errors of its older day further from the end than the search goes: the bundle
    /// is composed in seconds and finds only the errors within the newest 64 MiB, which is how a reader that read the whole
    /// log would be told from one that did not.
    /// </summary>
    [Fact]
    public async Task Three_hundred_megabytes_of_log_are_read_within_the_bounds_and_within_seconds()
    {
        using var dir = new TempPaths();
        var logs = Directory.CreateDirectory(dir.Paths.LogsDirectory).FullName;
        WriteLog(Path.Combine(logs, "runtime-20261003.jsonl"), 200, first: [.. Enumerable.Range(0, 10).Select(n => Event(At(n), $"an error from long ago {n}", "Error"))], last: []);
        WriteLog(Path.Combine(logs, "runtime-20261004.jsonl"), 100, first: [], last: [.. Enumerable.Range(0, 3).Select(n => Event(At(50 + n), $"a recent error {n}", "Error")), Event(At(59), "the last line")]);

        var watch = Stopwatch.StartNew();
        var bundle = await ComposeAsync(dir, Running(dir));
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"Composing over 300 MB of log took {watch.Elapsed}.");
        var section = Section(bundle, "logs");
        Assert.Equal(["a recent error 0", "a recent error 1", "a recent error 2"], section["recent_errors"]!.AsArray().Select(evt => Text(evt!["@mt"])));
        Assert.Equal(DoctorComposer.SliceEvents, section["slice"]!.AsArray().Count);
        Assert.Equal("the last line", Text(section["slice"]!.AsArray()[^1]!["@mt"]));
        Assert.True(section["slice_truncated"]!.GetValue<bool>());
        Assert.True(bundle.Serialize().Length < DoctorComposer.MaxBundleBytes);
    }

    [Fact]
    public async Task Config_describes_the_settings_file_and_every_jason_variable_with_values_only_from_the_list()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(
            dir.Paths.UserSettingsFile,
            """{ "Dispatcher": { "MaxParallel": 2, "Enabled": true }, "Update": { "FeedUrl": "https://SENTINEL_SETTING_FEED" }, "Roles": { "DefaultEntryCommand": ["SENTINEL_SETTING_COMMAND"] } }""");
        var variables = new Dictionary<string, string>
        {
            ["JASON_Dispatcher__MaxParallel"] = "4",
            ["JASON_Logging__MinimumLevel"] = "Debug",
            ["JASON_Update__FeedUrl"] = "https://SENTINEL_ENV_FEED.example",
            ["JASON_SENTINEL_ENV_NAME"] = "a value",
            ["JASON_DATA_DIR"] = dir.Paths.Root,
            ["jason_dispatcher__enabled"] = "false",
            ["PATH"] = "SENTINEL_PATH_VALUE",
        };

        var bundle = await ComposeAsync(dir, Running(dir), variables: variables);
        var config = Section(bundle, "config");

        Assert.Equal(["settings_file", "environment"], Keys(config));
        Assert.True(config["settings_file"]!["parses"]!.GetValue<bool>());
        Assert.Equal(2, config["settings_file"]!["shape"]!["members"]!["Dispatcher"]!["members"]!["MaxParallel"]!["value"]!.GetValue<int>());

        var environment = config["environment"]!.AsArray().ToDictionary(variable => Text(variable!["name"]), variable => variable!.AsObject());
        Assert.Equal(
            ["JASON_<key>", "JASON_DATA_DIR", "JASON_Dispatcher__MaxParallel", "JASON_Logging__MinimumLevel", "JASON_Update__FeedUrl", "jason_dispatcher__enabled"],
            environment.Keys);
        Assert.Equal(4, environment["JASON_Dispatcher__MaxParallel"]["value"]!.GetValue<int>());
        Assert.Equal("Debug", Text(environment["JASON_Logging__MinimumLevel"]["value"]));
        Assert.False(environment["jason_dispatcher__enabled"]["value"]!.GetValue<bool>());
        Assert.Equal("string", Text(environment["JASON_Update__FeedUrl"]["shape"]!["type"]));
        Assert.False(environment["JASON_Update__FeedUrl"].ContainsKey("value"));
        Assert.False(environment["JASON_DATA_DIR"].ContainsKey("value"));
        Assert.False(environment["JASON_<key>"].ContainsKey("value"));

        Assert.Equal(["Dispatcher:MaxParallel", "Dispatcher:Enabled", "Logging:MinimumLevel"], bundle.ValuesCarried);
        Assert.Equal(bundle.ValuesCarried, Strings(Section(bundle, "bundle")["values_carried"]));
        AssertAbsent(bundle, "SENTINEL_SETTING_FEED", "SENTINEL_SETTING_COMMAND", "SENTINEL_ENV_FEED", "SENTINEL_ENV_NAME", "SENTINEL_PATH_VALUE");
    }

    /// <summary>A list's items set one variable each are one setting, spelled as the published list spells it.</summary>
    [Fact]
    public async Task A_list_item_set_by_a_variable_is_carried_as_the_lists_path()
    {
        using var dir = new TempPaths();
        var variables = new Dictionary<string, string>
        {
            ["JASON_Manager__Triggers__0"] = "workitem_failed",
            ["JASON_Manager__Triggers__1"] = "decision_raised",
        };

        var bundle = await ComposeAsync(dir, Running(dir), variables: variables);

        Assert.Equal(["Manager:Triggers[]"], bundle.ValuesCarried);
        Assert.Contains("Manager:Triggers[]", SettingsAllowlist.Default.Published.Select(line => line.Split(' ')[0]));
    }

    /// <summary>A variable is named with the separators the person typed: the bundle never names a variable that is not set.</summary>
    [Fact]
    public async Task A_variable_keeps_the_separators_it_was_typed_with()
    {
        using var dir = new TempPaths();
        var variables = new Dictionary<string, string>
        {
            ["JASON_Dispatcher:MaxParallel"] = "3",
            ["JASON_Dispatcher__Enabled"] = "true",
            ["JASON_Plugins:SENTINEL_ENV_NAME__Exec"] = "x",
        };

        var bundle = await ComposeAsync(dir, Running(dir), variables: variables);

        var names = Section(bundle, "config")["environment"]!.AsArray().Select(variable => Text(variable!["name"])).ToList();
        Assert.Equal(["JASON_Dispatcher:MaxParallel", "JASON_Dispatcher__Enabled", "JASON_Plugins:<key>__Exec"], names);
        Assert.Equal(["Dispatcher:MaxParallel", "Dispatcher:Enabled"], bundle.ValuesCarried);
    }

    // ---- the runtime down, a read that fails midway, and one that times out --------------------------------------

    [Theory]
    [InlineData("no_descriptor")]
    [InlineData("runtime_unreachable")]
    [InlineData("unauthorized")]
    [InlineData("stale_descriptor")]
    public async Task A_runtime_that_did_not_answer_is_said_and_the_rest_of_the_bundle_is_complete(string why)
    {
        using var dir = new TempPaths();
        var runtime = new StubRuntime(dir.Paths.Root);
        if (why != "no_descriptor")
        {
            dir.WriteDescriptor(Descriptor());
        }

        runtime.Fails = (_, _) => why switch
        {
            "runtime_unreachable" => throw new HttpRequestException("connection refused"),
            "unauthorized" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => null,
        };
        runtime.InstanceId = why == "stale_descriptor" ? "rt_SOMEBODY_ELSE" : InstanceId;

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        Assert.Equal(["bundle", "versions", "status", "runtime", "routes", "config", "logs", "journal", "work_items", "journal_entries"], Keys(bundle.Document));
        var section = Section(bundle, "runtime");
        Assert.Equal(["answered", "reason"], Keys(section));
        Assert.False(section["answered"]!.GetValue<bool>());
        Assert.Equal(why, Text(section["reason"]));

        Assert.Null(Section(bundle, "versions")["runtime"]);
        Assert.Null(Section(bundle, "versions")["api"]);
        Assert.Equal("runtime_did_not_answer", Text(Section(bundle, "routes")["not_read"]));
        Assert.Equal("runtime_did_not_answer", Text(Section(bundle, "journal")["not_read"]));
        Assert.Equal("runtime_did_not_answer", Text(Section(bundle, "work_items")["not_read"]));
        Assert.Equal("runtime_did_not_answer", Text(Section(bundle, "journal_entries")["not_read"]));

        Assert.False(Section(bundle, "status")["ready"]!.GetValue<bool>());
        Assert.NotEmpty(Section(bundle, "status")["checks"]!.AsArray());
        Assert.Equal(["settings_file", "environment"], Keys(Section(bundle, "config")));
        Assert.Equal(Path.Combine("<data>", "logs"), Text(Section(bundle, "logs")["directory"]));
        // The status checks read what they read of a runtime that answered them; past the system.info that says it did not
        // answer the bundle, the composer asks nothing.
        Assert.DoesNotContain(runtime.Requests, request => request.Operation is Operations.JournalList or Operations.WorkItemList);
        Assert.Equal(Operations.SystemInfo, runtime.Requests.Select(request => request.Operation).LastOrDefault(Operations.SystemInfo));
    }

    /// <summary>The runtime stops answering on the journal's third page: the section says how far it got, and nothing else is lost.</summary>
    [Fact]
    public async Task A_read_that_fails_midway_says_how_far_it_got_and_the_bundle_is_complete_otherwise()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.JournalTotal = 5_000;
        runtime.WorkItemTotal = 40;
        runtime.Fails = (operation, call) => operation == Operations.JournalList && call == 3 ? throw new HttpRequestException("connection reset") : null;

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        var journal = Section(bundle, "journal");
        Assert.Equal(2_000, journal["counted"]!.GetValue<int>());
        Assert.Equal("runtime_unreachable", Text(journal["incomplete"]));
        Assert.False(journal["truncated"]!.GetValue<bool>());
        Assert.Equal(DoctorComposer.RecentEntries, journal["recent"]!.AsArray().Count);
        Assert.Equal(2_000, Section(bundle, "journal_entries")["entries"]!.AsArray().Count);
        Assert.Equal("runtime_unreachable", Text(Section(bundle, "journal_entries")["incomplete"]));

        Assert.True(Section(bundle, "runtime")["answered"]!.GetValue<bool>());
        Assert.Equal(40, Section(bundle, "work_items")["counted"]!.GetValue<int>());
        Assert.Equal(DoctorComposer.FromJournal, Text(Section(bundle, "work_items")["recent_failures_from"]));
        // The failures in the pages the journal never read are missing, and the section says why.
        Assert.Equal("runtime_unreachable", Text(Section(bundle, "work_items")["incomplete"]));
        Assert.Equal(["settings_file", "environment"], Keys(Section(bundle, "config")));
        Assert.Contains(Section(bundle, "status")["checks"]!.AsArray(), check => Text(check!["name"]) == "runtime" && Text(check["state"]) == "ok");
    }

    /// <summary>
    /// A runtime that answers <c>system.info</c> and then accepts every request without answering it: the first read
    /// that times out is the last one made, so the bundle costs one timeout rather than one for every read left.
    /// </summary>
    [Fact]
    public async Task After_a_read_times_out_no_later_read_is_attempted_and_the_sections_say_so()
    {
        using var dir = new TempPaths();
        var clock = new PassingClock();
        var hangs = new HangsAfter(Running(dir), clock, Timeout, Operations.SystemInfo, Operations.RoleList, Operations.RouteList)
        {
            AnsweredOnce = [Operations.PluginList],
        };

        var bundle = await ComposeAsync(dir, hangs, includeJournal: true);

        Assert.Equal([Timeout], clock.Waits);
        Assert.Equal([Operations.PluginList], hangs.Hung);
        Assert.DoesNotContain(hangs.Seen, operation => operation is Operations.JournalList or Operations.WorkItemList or Operations.WorkItemGet);

        var runtime = Section(bundle, "runtime");
        Assert.True(runtime["answered"]!.GetValue<bool>());
        Assert.Null(runtime["plugins"]);
        Assert.Equal(DoctorComposer.TimedOut, Text(runtime["incomplete"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "routes")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "journal")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "work_items")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "journal_entries")["not_read"]));
    }

    /// <summary>The status checks' own reads share the latch inside a bundle: one of theirs timing out is the last read made.</summary>
    [Fact]
    public async Task A_status_read_that_times_out_leaves_every_read_after_it_unattempted()
    {
        using var dir = new TempPaths();
        var clock = new PassingClock();
        var hangs = new HangsAfter(Running(dir), clock, Timeout, Operations.SystemInfo, Operations.RoleList);

        var bundle = await ComposeAsync(dir, hangs);

        Assert.Equal([Timeout], clock.Waits);
        Assert.Equal([Operations.PluginList], hangs.Hung);
        Assert.Equal([Operations.SystemInfo, Operations.PluginList], hangs.Seen);

        var runtime = Section(bundle, "runtime");
        Assert.False(runtime["answered"]!.GetValue<bool>());
        Assert.Equal(DoctorComposer.NotAttempted, Text(runtime["reason"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "routes")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "journal")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "work_items")["not_read"]));
    }

    /// <summary>A refused connection fails at once, so it is no reason to stop: the reads after it are still made.</summary>
    [Fact]
    public async Task A_refused_connection_is_not_a_timeout_and_the_reads_after_it_are_still_made()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation == Operations.PluginList ? throw new HttpRequestException("connection refused") : null;

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Equal("runtime_unreachable", Text(Section(bundle, "runtime")["incomplete"]));
        Assert.Equal(runtime.JournalTotal, Section(bundle, "journal")["counted"]!.GetValue<int>());
        Assert.Equal(runtime.WorkItemTotal, Section(bundle, "work_items")["counted"]!.GetValue<int>());
    }

    /// <summary>
    /// A runtime that sends its headers and then stalls the body: the client gives up while reading it, and that is a
    /// timeout like any other — the last read the bundle makes.
    /// </summary>
    [Fact]
    public async Task A_body_that_stalls_after_the_headers_is_the_last_read_made()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        // The composer's own read, the second: the status checks read the plugins first.
        runtime.Fails = (operation, call) => operation == Operations.PluginList && call == 2 ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent(TimeSpan.Zero) } : null;

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        Assert.Equal(DoctorComposer.TimedOut, Text(Section(bundle, "runtime")["incomplete"]));
        Assert.DoesNotContain(runtime.Requests, request => request.Operation is Operations.JournalList or Operations.WorkItemList or Operations.WorkItemGet);
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "routes")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "journal")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "work_items")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "journal_entries")["not_read"]));
    }

    /// <summary>The same, held for as long as a client waits: one wait in all, not one for every read left.</summary>
    [Fact]
    public async Task A_body_that_stalls_costs_one_wait_in_all()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var clock = new PassingClock();
        runtime.Fails = (operation, _) => operation is Operations.PluginList or Operations.JournalList or Operations.WorkItemList
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent(Timeout, clock) }
            : null;

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Equal([Timeout], clock.Waits);
        Assert.Equal(1, runtime.Requests.Count(request => request.Operation is Operations.PluginList or Operations.JournalList or Operations.WorkItemList));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "work_items")["not_read"]));
    }

    // ---- the routes: which plugin performs which operation, never an account -----------------------------------------

    /// <summary>
    /// Which plugin performs which operation, here and per campaign, is the first question after whether the runtime is
    /// running, and the bundle answers it — by plugin id and operation name, and whether a route binds an account, never
    /// which account or the identity that stands for it: a hash of a short account id is a guess away from the account.
    /// An operation the catalogue does not know and a plugin this installation does not hold are written as a key nobody
    /// chose, as the settings are.
    /// </summary>
    [Fact]
    [Critical(Family.S4, "a route's account never travels")]
    public async Task The_routes_section_says_which_plugin_performs_which_operation_and_never_which_account()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, "reply"));
        File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, "reply", "plugin.yaml"), "id: reply");
        var runtime = Running(dir);
        var routes = new JsonObject
        {
            ["routing_snapshot_id"] = RoutingSnapshot,
            ["plugin_snapshot_id"] = PluginSnapshot,
            ["global"] = new JsonObject
            {
                ["default"] = Route("reply", new JsonObject { ["account"] = "SENTINEL_ROUTE_ACCOUNT" }, RouteIdentity),
                ["operations"] = new JsonObject
                {
                    ["campaign.get"] = Route("SENTINEL_PLUGIN", null, null),
                    ["SENTINEL_OPERATION"] = Route("reply", null, null),
                },
            },
            ["campaigns"] = new JsonArray(new JsonObject
            {
                ["campaign_id"] = RoutedCampaign,
                ["default"] = null,
                ["operations"] = new JsonObject { ["optout.record"] = Route("reply", new JsonObject { ["account"] = "SENTINEL_CAMPAIGN_ACCOUNT" }, null) },
            }),
        };
        runtime.Fails = (operation, _) => operation == Operations.RouteList ? Respond(HttpStatusCode.OK, routes.ToJsonString()) : null;

        var bundle = await ComposeAsync(dir, runtime);

        var section = Section(bundle, "routes");
        Assert.Equal(
            new JsonObject
            {
                ["routing_snapshot_id"] = RoutingSnapshot,
                ["plugin_snapshot_id"] = PluginSnapshot,
                ["global"] = new JsonArray(
                    Row("default", "reply", true),
                    Row("campaign.get", SettingKey.Hidden, false),
                    Row(SettingKey.Hidden, "reply", false)),
                ["campaigns"] = new JsonArray(new JsonObject
                {
                    ["campaign_id"] = RoutedCampaign,
                    ["routes"] = new JsonArray(Row("optout.record", "reply", true)),
                }),
            }.ToJsonString(),
            section.ToJsonString());
        Assert.Equal(4, bundle.Contents.Single(entry => entry.Name == "routes").Items);

        var file = bundle.Serialize();
        foreach (var withheld in (string[])["SENTINEL_ROUTE_ACCOUNT", "SENTINEL_CAMPAIGN_ACCOUNT", RouteIdentity, "SENTINEL_PLUGIN", "SENTINEL_OPERATION"])
        {
            Assert.DoesNotContain(withheld, file, StringComparison.Ordinal);
        }

        // And the promise is published where a person reads what a bundle never holds: the header and the help.
        Assert.Contains("a route's account and its identity", DoctorComposer.NeverCarried);
    }

    /// <summary>
    /// An id is written only when it is well formed. A campaign id or a snapshot id the runtime sent in any other shape is
    /// a value nobody vouched for, and it is written as the settings write a key nobody chose.
    /// </summary>
    [Fact]
    public async Task A_campaign_or_snapshot_id_that_is_not_well_formed_is_written_as_a_key_nobody_chose()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var routes = new JsonObject
        {
            ["routing_snapshot_id"] = "SENTINEL_RTS",
            ["plugin_snapshot_id"] = "SENTINEL_SNP",
            ["global"] = new JsonObject { ["default"] = null, ["operations"] = new JsonObject() },
            ["campaigns"] = new JsonArray(new JsonObject
            {
                ["campaign_id"] = "SENTINEL_CAMPAIGN_ID",
                ["default"] = Route("reply", null, null),
                ["operations"] = new JsonObject(),
            }),
        };
        runtime.Fails = (operation, _) => operation == Operations.RouteList ? Respond(HttpStatusCode.OK, routes.ToJsonString()) : null;

        var bundle = await ComposeAsync(dir, runtime);

        var section = Section(bundle, "routes");
        Assert.Equal(SettingKey.Hidden, Text(section["routing_snapshot_id"]));
        Assert.Equal(SettingKey.Hidden, Text(section["plugin_snapshot_id"]));
        Assert.Equal(SettingKey.Hidden, Text(section["campaigns"]![0]!["campaign_id"]));
        var file = bundle.Serialize();
        foreach (var unvouched in (string[])["SENTINEL_RTS", "SENTINEL_SNP", "SENTINEL_CAMPAIGN_ID"])
        {
            Assert.DoesNotContain(unvouched, file, StringComparison.Ordinal);
        }
    }

    private const string RoutingSnapshot = "rts_01J4ZQ8X5W3V2T1S0R9Q8P7N6A";

    private const string PluginSnapshot = "snp_01J4ZQ8X5W3V2T1S0R9Q8P7N6B";

    private const string RoutedCampaign = "cmp_01J4ZQ8X5W3V2T1S0R9Q8P7N6C";

    /// <summary>What route.list says stands for the bound account: a digest no bundle may carry.</summary>
    private const string RouteIdentity = "sha256:5e7a11e1d5e7a11e1d5e7a11e1d5e7a11e1d5e7a11e1d5e7a11e1d5e7a11e1d";

    private static JsonObject Route(string pluginId, JsonObject? binding, string? identity) =>
        new() { ["plugin_id"] = pluginId, ["binding"] = binding, ["binding_identity"] = identity };

    private static JsonObject Row(string operation, string pluginId, bool bindsAccount) =>
        new() { ["operation"] = operation, ["plugin_id"] = pluginId, ["binds_account"] = bindsAccount };

    // ---- loopback only, and what is never carried ---------------------------------------------------------------

    [Fact]
    [Critical(Family.S4, "doctor only reads")]
    public async Task Every_request_goes_to_the_descriptors_base_url_and_reads_only()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);

        await ComposeAsync(dir, runtime, includeJournal: true);

        Assert.NotEmpty(runtime.Requests);
        Assert.All(runtime.Requests, request =>
        {
            Assert.StartsWith(BaseUrl + "/v1/", request.Uri.AbsoluteUri, StringComparison.Ordinal);
            Assert.Contains(request.Operation, Reads);
            Assert.Equal("Bearer " + Token, request.Authorization);
        });
    }

    [Fact]
    public async Task With_no_descriptor_there_is_no_request_at_all()
    {
        using var dir = new TempPaths();
        var runtime = new StubRuntime(dir.Paths.Root);

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        Assert.Empty(runtime.Requests);
        Assert.Equal("no_descriptor", Text(Section(bundle, "runtime")["reason"]));
    }

    /// <summary>A descriptor that names an address off this machine is not a runtime of this installation, and nothing is sent there.</summary>
    [Theory]
    [Critical(Family.S4, "no network beyond loopback")]
    [InlineData("http://example.com:47811")]
    [InlineData("http://10.0.0.7:47811")]
    public async Task A_descriptor_whose_address_is_not_loopback_is_stale_and_never_asked(string baseUrl)
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor(baseUrl));
        var runtime = new StubRuntime(dir.Paths.Root);

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Empty(runtime.Requests);
        Assert.Equal("stale_descriptor", Text(Section(bundle, "runtime")["reason"]));
    }

    [Theory]
    [Critical(Family.S3, "no token, with the journal or without")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_capability_token_is_never_in_a_bundle(bool includeJournal)
    {
        using var dir = new TempPaths();

        var bundle = await ComposeAsync(dir, Running(dir), includeJournal: includeJournal);

        AssertAbsent(bundle, Token);
    }

    /// <summary>
    /// Every value the runtime hands this process — a campaign context, a contact's fields, a work item's context and
    /// result, its error's message, a decision, a reason, the actor's id, a plugin problem's message — and the settings
    /// file's and environment's values off the list: none reaches the bundle.
    /// </summary>
    [Fact]
    [Critical(Family.S4, "the values the reads return are dropped")]
    public async Task No_value_the_reads_hand_this_process_reaches_the_bundle()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{ "Update": { "FeedUrl": "SENTINEL_SETTING_FEED" }, "Roles": { "DefaultEntryCommand": ["SENTINEL_SETTING_COMMAND"] } }""");
        var runtime = Running(dir);
        runtime.WorkItemTotal = 60;

        var bundle = await ComposeAsync(
            dir,
            runtime,
            variables: new Dictionary<string, string> { ["JASON_Update__FeedUrl"] = "SENTINEL_ENV_FEED", ["JASON_SENTINEL_ENV_NAME"] = "x" });

        AssertAbsent(bundle, [.. NeverSentinels, .. JournalSentinels]);
        Assert.NotEmpty(Section(bundle, "work_items")["recent_failures"]!.AsArray());
        Assert.NotEmpty(Section(bundle, "journal")["recent"]!.AsArray());
    }

    /// <summary>
    /// The journal's entries are carried whole on the flag, and two kinds of them hold what a bundle without it never does:
    /// the old and new values of a work item's context key, and a failed item's last error with its message. Both travel,
    /// and the bundle's own words do not claim otherwise.
    /// </summary>
    [Fact]
    public async Task With_the_journal_asked_for_a_work_items_context_and_error_message_travel_and_the_bundle_says_so()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) =>
        {
            if (operation != Operations.JournalList)
            {
                return null;
            }

            var context = runtime.Entry(0);
            context["kind"] = "workitem_context_updated";
            context["old"] = new JsonObject { ["brief"] = "SENTINEL_CONTEXT_BEFORE" };
            context["new"] = new JsonObject { ["brief"] = "SENTINEL_CONTEXT_AFTER" };
            var failed = Failed(runtime.Entry(1), 1);
            failed["new"] = new JsonObject { ["code"] = "provider_refused", ["message"] = "SENTINEL_FAILURE_MESSAGE", ["retriable"] = false };
            return Respond(HttpStatusCode.OK, new JsonObject { ["items"] = new JsonArray(context, failed), ["next_cursor"] = null }.ToJsonString());
        };

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        var entries = Section(bundle, "journal_entries").ToJsonString();
        foreach (var value in (string[])["SENTINEL_CONTEXT_BEFORE", "SENTINEL_CONTEXT_AFTER", "SENTINEL_FAILURE_MESSAGE"])
        {
            Assert.Contains(value, entries, StringComparison.Ordinal);
        }

        // What the bundle says it never carries is what no bundle carries: the contexts and the errors travel on the flag,
        // and the list says so where it names them. The warning and the qualified entry are one constant, the same.
        var neverCarried = Strings(Section(bundle, "bundle")["never_carried"]);
        Assert.All(
            neverCarried.Where(entry => entry.Contains("context", StringComparison.Ordinal) || entry.Contains("error", StringComparison.Ordinal)),
            entry => Assert.StartsWith("journal values unless --include-journal: ", entry, StringComparison.Ordinal));
        Assert.Contains("work-item results", neverCarried);

        var qualified = Assert.Single(neverCarried, entry => entry.StartsWith("journal values", StringComparison.Ordinal));
        Assert.Equal("journal values unless --include-journal: " + DoctorComposer.JournalCarries, qualified);
        Assert.Equal("This bundle includes " + DoctorComposer.JournalCarries + ". Read it before you share it.", DoctorComposer.JournalWarning);
        Assert.Contains("campaign and work-item contexts as they changed", DoctorComposer.JournalCarries, StringComparison.Ordinal);
        Assert.Contains("the errors attempts ended with", DoctorComposer.JournalCarries, StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.S4, "--include-journal and its warning")]
    public async Task With_the_journal_asked_for_its_entries_are_carried_whole_and_the_warning_with_them()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.WorkItemTotal = 60;

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        var text = bundle.Serialize();
        foreach (var sentinel in JournalSentinels)
        {
            Assert.Contains(sentinel, text, StringComparison.Ordinal);
        }

        AssertAbsent(bundle, NeverSentinels);

        var header = Section(bundle, "bundle");
        Assert.True(header["include_journal"]!.GetValue<bool>());
        Assert.Equal([DoctorComposer.JournalWarning, ProblemReport.JournalToSupport], Strings(header["warnings"]));
        Assert.Equal([DoctorComposer.JournalWarning, ProblemReport.JournalToSupport], bundle.Warnings);
        Assert.Equal("journal_entries", bundle.Contents[^1].Name);

        var entries = Section(bundle, "journal_entries");
        Assert.Equal(["entries", "truncated"], Keys(entries));
        Assert.Equal(runtime.JournalTotal, entries["entries"]!.AsArray().Count);
        Assert.Equal("SENTINEL_ACTOR_ID", Text(entries["entries"]![0]!["actor"]!["id"]));
    }

    /// <summary>
    /// The CLI writes as <c>human:&lt;the account's name&gt;</c> when no actor is given, as the paired browser does, so the
    /// journal's actors carry the account's name. The entries travel on the flag, and that name in them is its mark.
    /// </summary>
    [Fact]
    public async Task With_the_journal_asked_for_the_account_s_name_as_an_actor_is_its_mark()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) =>
        {
            if (operation != Operations.JournalList)
            {
                return null;
            }

            var entry = runtime.Entry(0);
            entry["actor"] = new JsonObject { ["type"] = "human", ["id"] = MadeUp.User };
            return Respond(HttpStatusCode.OK, new JsonObject { ["items"] = new JsonArray(entry), ["next_cursor"] = null }.ToJsonString());
        };

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        var actor = Section(bundle, "journal_entries")["entries"]![0]!["actor"]!;
        Assert.Equal("human", Text(actor["type"]));
        Assert.Equal("<user>", Text(actor["id"]));
        AssertAbsent(bundle, MadeUp.User);
    }

    /// <summary>Entries of most of a mebibyte each: as many of the newest as sixteen mebibytes of the bundle hold.</summary>
    [Fact]
    public async Task The_journals_entries_stop_at_sixteen_mebibytes_in_the_bundle_and_keep_the_newest()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var big = new string('n', 700 * 1024);
        runtime.Fails = (operation, _) =>
        {
            if (operation != Operations.JournalList)
            {
                return null;
            }

            var items = new JsonArray([.. Enumerable.Range(0, 40).Select(n =>
            {
                var entry = runtime.Entry(n);
                entry["new"] = big + n;
                return (JsonNode)entry;
            })]);
            return Respond(HttpStatusCode.OK, new JsonObject { ["items"] = items, ["next_cursor"] = null }.ToJsonString());
        };

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        var entries = Section(bundle, "journal_entries");
        var kept = entries["entries"]!.AsArray();
        Assert.InRange(kept.Count, 20, 23);
        Assert.Equal(Text(runtime.Entry(0)["id"]), Text(kept[0]!["id"]));
        Assert.True(entries["truncated"]!.GetValue<bool>());
        Assert.Equal(40, Section(bundle, "journal")["counted"]!.GetValue<int>());
        Assert.True(bundle.Contents.Single(entry => entry.Name == "journal_entries").Bytes <= DoctorComposer.JournalEntriesBytes + DoctorComposer.SectionBytes);
        Assert.True(bundle.Serialize().Length < DoctorComposer.MaxBundleBytes);
    }

    /// <summary>
    /// The rewrite can lengthen what was read — a person called <c>x</c> turns every lone <c>x</c> into <c>&lt;user&gt;</c>
    /// — so the entries are held to their budget again once rewritten, the newest kept, rather than the section being
    /// refused whole.
    /// </summary>
    [Fact]
    public async Task Entries_the_rewrite_lengthens_are_held_to_their_budget_again_the_newest_kept()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var spaced = string.Concat(Enumerable.Repeat(" x", 350 * 1024));
        runtime.Fails = (operation, _) =>
        {
            if (operation != Operations.JournalList)
            {
                return null;
            }

            var items = new JsonArray([.. Enumerable.Range(0, 20).Select(n =>
            {
                var entry = runtime.Entry(n);
                entry["new"] = spaced;
                return (JsonNode)entry;
            })]);
            return Respond(HttpStatusCode.OK, new JsonObject { ["items"] = items, ["next_cursor"] = null }.ToJsonString());
        };

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true, identity: new IdentityFacts(@"C:\Users\x", "x", "WORKSTATION-7", null, null));

        var entries = Section(bundle, "journal_entries");
        Assert.Equal(["entries", "truncated"], Keys(entries));
        var kept = entries["entries"]!.AsArray();
        Assert.InRange(kept.Count, 1, 19);
        Assert.Equal(Text(runtime.Entry(0)["id"]), Text(kept[0]!["id"]));
        Assert.True(entries["truncated"]!.GetValue<bool>());
        Assert.True(bundle.Contents.Single(entry => entry.Name == "journal_entries").Bytes <= DoctorComposer.JournalEntriesBytes + DoctorComposer.SectionBytes);
    }

    /// <summary>An error the runtime answers with travels as its code alone, and only a code that is a word: never a message, never a body.</summary>
    [Fact]
    public async Task A_runtime_error_is_carried_by_its_code_alone()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation switch
        {
            Operations.JournalList => Respond(HttpStatusCode.BadRequest, """{"error":{"code":"input_invalid","message":"SENTINEL_RUNTIME_MESSAGE","retryable":false}}"""),
            Operations.WorkItemList => Respond(HttpStatusCode.InternalServerError, """{"error":{"code":"Quillon said SENTINEL_CODE","message":"x","retryable":false}}"""),
            Operations.PluginList => Respond(HttpStatusCode.OK, "not json SENTINEL_BODY"),
            _ => null,
        };

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Equal("input_invalid", Text(Section(bundle, "journal")["incomplete"]));
        Assert.Equal(DoctorComposer.UnreadableAnswer, Text(Section(bundle, "work_items")["incomplete"]));
        Assert.Equal(DoctorComposer.UnreadableAnswer, Text(Section(bundle, "runtime")["incomplete"]));
        AssertAbsent(bundle, "SENTINEL");
    }

    /// <summary>Two keys that differ only in case are two keys somebody typed, and the page that holds them is still read.</summary>
    [Fact]
    public async Task An_entry_with_two_keys_that_differ_only_in_case_is_counted_and_carried()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) =>
        {
            if (operation != Operations.JournalList)
            {
                return null;
            }

            var entry = runtime.Entry(0);
            entry["new"] = JsonNode.Parse("""{"Email":"SENTINEL_UPPER","email":"SENTINEL_LOWER"}""");
            return Respond(HttpStatusCode.OK, new JsonObject { ["items"] = new JsonArray(entry), ["next_cursor"] = null }.ToJsonString());
        };

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        Assert.Equal(1, Section(bundle, "journal")["counted"]!.GetValue<int>());
        var carried = Section(bundle, "journal_entries")["entries"]![0]!["new"]!;
        Assert.Equal("SENTINEL_UPPER", Text(carried["Email"]));
        Assert.Equal("SENTINEL_LOWER", Text(carried["email"]));
    }

    // ---- what the runtime may answer that is malformed or too large ----------------------------------------------

    [Fact]
    public async Task A_null_or_unworded_problem_in_the_plugin_listing_is_left_out_and_never_throws()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation == Operations.PluginList
            ? Respond(
                HttpStatusCode.OK,
                """{"snapshot":{"id":"snp_1","loaded_at":"2026-10-04T11:00:00.000Z","source":"startup","plugin_count":1},"routing_snapshot_id":"rts_1","plugins":[{"id":"p","version":"1.0.0","kind":"provider","digest":"sha256:1","status":"unavailable","problems":[null,{"code":"SENTINEL code","path":"p","message":"m"},{"code":"executable_missing","path":"p","message":"m"}]}],"last_reload":null,"activated":true}""")
            : null;

        var bundle = await ComposeAsync(dir, runtime);

        var plugin = Section(bundle, "runtime")["plugins"]!.AsArray().Single()!;
        Assert.Equal(["executable_missing"], Strings(plugin["problem_codes"]));
        AssertAbsent(bundle, "SENTINEL");
    }

    /// <summary>A page larger than was asked for is held to a page, and the listing to ten of them, whatever the runtime sends.</summary>
    [Fact]
    public async Task Pages_larger_than_asked_are_held_to_the_page_size_and_the_cap()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, call) =>
        {
            if (operation != Operations.JournalList)
            {
                return null;
            }

            var start = (call - 1) * 5_000;
            var items = new JsonArray([.. Enumerable.Range(start, 5_000).Select(n => (JsonNode)runtime.Entry(n))]);
            return Respond(HttpStatusCode.OK, new JsonObject { ["items"] = items, ["next_cursor"] = "c" + (start + 5_000).ToString(CultureInfo.InvariantCulture) }.ToJsonString());
        };

        var journal = Section(await ComposeAsync(dir, runtime), "journal");

        Assert.Equal(DoctorComposer.MaxPages * DoctorComposer.PageSize, journal["counted"]!.GetValue<int>());
        Assert.True(journal["truncated"]!.GetValue<bool>());
        Assert.Equal(DoctorComposer.MaxPages * DoctorComposer.PageSize, journal["by_kind"]!.AsObject().Sum(kind => kind.Value!.GetValue<int>()));
    }

    [Fact]
    public async Task More_plugins_than_a_page_are_listed_to_a_page_and_said_to_be_cut()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Registry = StubRuntime.Plugins(1_500);

        var section = Section(await ComposeAsync(dir, runtime), "runtime");

        Assert.Equal(DoctorComposer.PageSize, section["plugins"]!.AsArray().Count);
        Assert.True(section["plugins_truncated"]!.GetValue<bool>());
    }

    /// <summary>
    /// A plugin with more problem codes than are listed, or an id, a version or a digest longer than any package has, costs
    /// its own line and no more: the rest of the runtime section, its own report among it, is still written.
    /// </summary>
    [Fact]
    public async Task A_plugin_too_large_to_list_whole_costs_its_own_line_and_no_more()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var problems = new JsonArray([.. Enumerable.Range(0, 50).Select(n => (JsonNode)new JsonObject { ["code"] = $"problem_{n:D2}", ["path"] = "p", ["message"] = "m" })]);
        var listing = new JsonObject
        {
            ["snapshot"] = new JsonObject { ["id"] = "snp_1", ["loaded_at"] = "2026-10-04T11:00:00.000Z", ["source"] = "startup", ["plugin_count"] = 1 },
            ["routing_snapshot_id"] = "rts_1",
            ["plugins"] = new JsonArray(new JsonObject
            {
                ["id"] = "p" + new string('i', 200),
                ["version"] = "1." + new string('9', 200),
                ["digest"] = "sha256:" + new string('d', 300),
                ["kind"] = "provider",
                ["status"] = "unavailable",
                ["problems"] = problems,
            }),
            ["last_reload"] = null,
            ["activated"] = true,
        }.ToJsonString();
        runtime.Fails = (operation, _) => operation == Operations.PluginList ? Respond(HttpStatusCode.OK, listing) : null;

        var section = Section(await ComposeAsync(dir, runtime), "runtime");

        var plugin = section["plugins"]!.AsArray().Single()!;
        Assert.Equal(DoctorComposer.TooLong, Text(plugin["id"]));
        Assert.Equal(DoctorComposer.TooLong, Text(plugin["version"]));
        Assert.Equal(DoctorComposer.TooLong, Text(plugin["digest"]));
        Assert.Equal(Enumerable.Range(0, DoctorComposer.MaxProblemCodes).Select(n => $"problem_{n:D2}"), Strings(plugin["problem_codes"]));
        Assert.True(plugin["problem_codes_truncated"]!.GetValue<bool>());
        Assert.Equal(InstanceId, Text(section["system_info"]!["instance_id"]));
    }

    /// <summary>
    /// The header is held to its budget by measuring it: settings whose values travel, many enough to pass it, are cut from
    /// the end of the list, and the header says so.
    /// </summary>
    [Fact]
    public async Task The_header_is_held_to_its_budget_and_says_when_values_carried_was_cut()
    {
        using var dir = new TempPaths();
        var variables = new Dictionary<string, string>();
        for (var n = 0; n < 2_000; n++)
        {
            var id = $"plugin-{n:D4}";
            Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, id));
            File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, id, "plugin.yaml"), $"id: {id}");
            variables[$"JASON_Notifications__Plugins__{id}__Enabled"] = "true";
        }

        var bundle = await ComposeAsync(dir, Running(dir), variables: variables);

        var header = Section(bundle, "bundle");
        Assert.True(header["values_carried_truncated"]!.GetValue<bool>());
        Assert.InRange(bundle.ValuesCarried.Count, 1, 1_999);
        Assert.Equal(bundle.ValuesCarried, Strings(header["values_carried"]));
        Assert.True(Encoding.UTF8.GetByteCount(header.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) <= DoctorComposer.HeaderBytes);
        Assert.Equal(["settings_file", "environment"], Keys(Section(bundle, "config")));
        Assert.True(bundle.Serialize().Length < DoctorComposer.MaxBundleBytes);
    }

    // ---- the added fields, when what they report goes wrong -----------------------------------------------------

    [Fact]
    public async Task A_plugin_listing_that_fails_leaves_the_plugins_unknown_and_says_why()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation == Operations.PluginList ? Envelope(HttpStatusCode.InternalServerError, "internal_error") : null;

        var section = Section(await ComposeAsync(dir, runtime), "runtime");

        Assert.Equal(["answered", "system_info", "plugins", "data_dir_agrees", "incomplete"], Keys(section));
        Assert.Null(section["plugins"]);
        Assert.Equal("internal_error", Text(section["incomplete"]));
    }

    [Fact]
    public async Task A_failure_that_cannot_be_read_by_itself_leaves_the_rest_and_says_why()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, call) => operation == Operations.WorkItemGet && call == 1 ? Envelope(HttpStatusCode.NotFound, "work_item_not_found") : null;

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        Assert.Equal("work_item_not_found", Text(workItems["incomplete"]));
        Assert.Equal(DoctorComposer.RecentFailures - 1, workItems["recent_failures"]!.AsArray().Count);
        Assert.True(workItems["recent_failures_truncated"]!.GetValue<bool>());
    }

    /// <summary>
    /// Failures may exist that were not read when one of the items the journal names could not be read, or was never
    /// asked about because a read before it timed out: the list says it was cut short, though the journal named fewer
    /// than it reads.
    /// </summary>
    [Theory]
    [InlineData("all_read", false)]
    [InlineData("second_times_out", true)]
    [InlineData("second_fails", true)]
    public async Task Recent_failures_say_they_were_cut_short_when_a_named_item_was_not_read(string scenario, bool truncated)
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.JournalTotal = 24;
        runtime.Fails = (operation, call) =>
            operation != Operations.WorkItemGet || call != 2 ? null
            : scenario == "second_times_out" ? throw new TaskCanceledException("The request was canceled.", new TimeoutException())
            : scenario == "second_fails" ? Envelope(HttpStatusCode.InternalServerError, "internal_error")
            : null;

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        Assert.True(workItems["recent_failures"]!.AsArray().Count < DoctorComposer.RecentFailures);
        Assert.Equal(truncated, workItems["recent_failures_truncated"]!.GetValue<bool>());
    }

    /// <summary>
    /// The role-skills fact is rewritten only where it is made of withheld problems: a runtime that refuses no role keeps
    /// the fact the status verb composed.
    /// </summary>
    [Fact]
    public async Task A_role_skills_fact_with_no_refused_role_is_the_status_verbs_own()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with
        {
            Skills = new SkillsInfo(Path.Combine(dir.Paths.Root, "skills", "roles"), 4096, [new DeployedRoleSkill("planner", 100, null)]),
        };

        var bundle = await ComposeAsync(dir, runtime);

        var fact = Text(Section(bundle, "status")["checks"]!.AsArray().Single(each => Text(each!["name"]) == "role_skills")!["fact"]);
        Assert.Contains("roles taught", fact, StringComparison.Ordinal);
        Assert.DoesNotContain(DoctorComposer.Withheld, fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_listing_of_failures_that_fails_on_its_first_page_says_why()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) =>
            operation == Operations.JournalList ? Envelope(HttpStatusCode.InternalServerError, "internal_error")
            : operation == Operations.WorkItemList && runtime.Requests[^1].Body.ContainsKey("status") ? Envelope(HttpStatusCode.ServiceUnavailable, "runtime_busy")
            : null;

        var workItems = Section(await ComposeAsync(dir, runtime), "work_items");

        Assert.Equal(DoctorComposer.FromListOldestFirst, Text(workItems["recent_failures_from"]));
        Assert.Equal("runtime_busy", Text(workItems["incomplete"]));
        Assert.Empty(workItems["recent_failures"]!.AsArray());
        Assert.Equal(runtime.WorkItemTotal, workItems["counted"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_file_where_the_logs_directory_belongs_is_said_to_be_unreadable()
    {
        using var dir = new TempPaths();
        File.WriteAllText(dir.Paths.LogsDirectory, "not a directory");

        var section = Section(await ComposeAsync(dir, Running(dir)), "logs");

        Assert.False(section["exists"]!.GetValue<bool>());
        Assert.Equal("logs_unreadable", Text(section["incomplete"]));
        Assert.Empty(section["slice"]!.AsArray());
    }

    [Fact]
    public async Task A_settings_file_that_cannot_be_read_is_said_to_be_there_and_unreadable()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{ "Dispatcher": { "MaxParallel": 2 } }""");

        DoctorBundle bundle;
        using (new FileStream(dir.Paths.UserSettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bundle = await ComposeAsync(dir, Running(dir));
        }

        Assert.Equal("""{"exists":true,"readable":false}""", Section(bundle, "config")["settings_file"]!.ToJsonString());
        Assert.Empty(bundle.ValuesCarried);
    }

    // ---- what runtime.system_info leaves out, and says it does ---------------------------------------------------

    /// <summary>
    /// <c>system_info</c> is the runtime's answer less what can hold text nobody controls — a helper's last line of error
    /// output, pointers into somebody's binding, an operating system's message about a skill, an address a configured feed
    /// chose — and the bundle names each field it withholds, so a reader knows what is absent rather than wondering whether
    /// the runtime omitted it. Planted with a value in every field, the fields that go missing or say "withheld" are exactly
    /// the declared ones; a presence detail that is one of the runtime's own words travels as written; and the status
    /// section, whose facts are composed from the same answer, quotes none of what is withheld and the word as written.
    /// </summary>
    [Fact]
    [Critical(Family.S4, "system_info travels projected")]
    public async Task System_info_and_status_withhold_exactly_the_declared_fields_and_carry_the_runtimes_own_words()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var every = StubRuntime.EveryField(dir.Paths.Root);
        runtime.Info = every with { Skills = every.Skills! with { Roles = [.. every.Skills!.Roles, new DeployedRoleSkill("sentinel-own-skill", 10, null)] } };

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Equal(
            [
                ("notifications.channels[].detail", WithheldAs.Dropped),
                ("notifications.channels[].presence_detail", WithheldAs.DroppedUnlessOwnWord),
                ("skills.problem", WithheldAs.ReplacedByWord),
                ("skills.problem_code", WithheldAs.ReplacedUnlessOwnWord),
                ("skills.problem_exception", WithheldAs.ReplacedUnlessOwnWord),
                ("skills.roles[].problem", WithheldAs.ReplacedByWord),
                ("skills.roles[].problem_code", WithheldAs.ReplacedUnlessOwnWord),
                ("skills.roles[].problem_exception", WithheldAs.ReplacedUnlessOwnWord),
                ("update.release_notes_url", WithheldAs.Dropped),
            ],
            DoctorComposer.SystemInfoWithheld.Select(field => (field.Path, field.How)));

        // Item by item: what is absent from the bundle, or says "withheld" where the runtime said something else.
        var projected = Section(bundle, "runtime")["system_info"]!;
        var full = JsonSerializer.SerializeToNode(runtime.Info, JasonJson.Options)!;
        var kept = Values(projected, string.Empty);
        var withheld = Values(full, string.Empty)
            .Where(leaf => !kept.TryGetValue(leaf.Key, out var value) || (value == DoctorComposer.Withheld && leaf.Value != DoctorComposer.Withheld))
            .Select(leaf => Indexless(leaf.Key))
            .Distinct()
            .Order(StringComparer.Ordinal);
        Assert.Equal(DoctorComposer.SystemInfoWithheld.Select(field => field.Path).Order(StringComparer.Ordinal), withheld);

        Assert.True(projected["update"]!["release_notes_offered"]!.GetValue<bool>());
        Assert.Equal(DoctorComposer.Withheld, Text(projected["skills"]!["problem"]));
        Assert.Equal(DoctorComposer.Withheld, Text(projected["skills"]!["roles"]![0]!["problem"]));
        Assert.Null(projected["skills"]!["roles"]![1]!["problem"]);
        Assert.Equal("session 0", Text(projected["notifications"]!["channels"]![1]!["presence_detail"]));
        Assert.False(projected["notifications"]!["channels"]![2]!.AsObject().ContainsKey("presence_detail"), "A word that is the runtime's own in another case is not one of its own words.");

        var sentinels = new[]
        {
            "SENTINEL_CHANNEL_DETAIL", "SENTINEL_SECOND_DETAIL", "SENTINEL_THIRD_DETAIL", "SENTINEL_PRESENCE_DETAIL", "SENTINEL_ROLE_PROBLEM", "SENTINEL_SKILLS_PROBLEM",
            "SENTINEL_RELEASE_NOTES", "SENTINEL_ROLE_CODE", "SENTINEL_ROLE_EXCEPTION", "SENTINEL_SKILLS_CODE", "SENTINEL_SKILLS_EXCEPTION", "sentinel-own-skill",
        };
        AssertAbsent(bundle, sentinels);
        Assert.Contains("session 0", Section(bundle, "status").ToJsonString(), StringComparison.Ordinal);
        Assert.Contains(Section(bundle, "status")["checks"]!.AsArray(), check => Text(check!["name"]) == "role_skills" && Text(check["fact"]) == DoctorComposer.Withheld + " (1)");

        var described = DoctorComposer.SystemInfoWithheld.Select(field => field.Described).ToList();
        var declared = Section(bundle, "bundle")["projected"]!.AsArray();
        Assert.Equal(["runtime.system_info", "status"], declared.Select(entry => Text(entry!["field"])));
        Assert.All(declared, entry => Assert.Equal(described, Strings(entry!["withheld"])));
        Assert.Equal(["runtime.system_info", "status"], bundle.Projected.Select(projection => projection.Field));
        Assert.All(bundle.Projected, projection => Assert.Equal(described, projection.Withheld));
    }

    // ---- the rewrite and the cap, last ----------------------------------------------------------------------------

    [Fact]
    public async Task The_installations_names_are_rewritten_everywhere_and_exception_text_is_capped_after()
    {
        using var dir = new TempPaths();
        var logs = Directory.CreateDirectory(dir.Paths.LogsDirectory).FullName;
        var frames = string.Concat(Enumerable.Range(0, 80).Select(n => $"\n   at Jason.Frame{n}() in C:\\Users\\Quillon\\src\\Frame{n}.cs:line {n}"));
        var exception = @"System.IO.IOException: C:\Users\Quillon\.jason\config\settings.json on WORKSTATION-7: " + new string('m', 3_000) + frames;
        Write(
            logs,
            "runtime-20261004.jsonl",
            Event(At(1), "Jason runtime starting in {DataDir}", null, ("DataDir", @"C:\Users\Quillon\.jason")),
            Event(At(2), "A failure for {Account}", "Error", ("Account", @"CORP\Quillon S-1-5-21-9-8-7-6"), ("@x", exception)));
        var runtime = Running(dir, dataDir: @"C:\Users\Quillon\.jason");

        var bundle = await ComposeAsync(dir, runtime);

        var everything = Everything(JsonNode.Parse(bundle.Serialize())!).ToList();
        Assert.DoesNotContain(everything, text => text.Contains(@"C:\Users\Quillon", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(everything, text => text.Contains("WORKSTATION-7", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(everything, text => text.Contains("CORP", StringComparison.Ordinal));
        Assert.DoesNotContain(everything, text => text.Contains("S-1-5-21-", StringComparison.Ordinal));

        Assert.Equal(@"~\.jason", Text(Section(bundle, "runtime")["system_info"]!["data_dir"]));
        Assert.Contains(Section(bundle, "status")["checks"]!.AsArray(), check => Text(check!["fact"]).Contains(@"data directory ~\.jason", StringComparison.Ordinal));

        var failure = Section(bundle, "logs")["recent_errors"]!.AsArray().Single()!;
        Assert.Equal(@"<domain>\<user> <sid>", Text(failure["Account"]));
        var capped = Text(failure["@x"]);
        Assert.StartsWith(@"System.IO.IOException: ~\.jason\config\settings.json on <machine>: mmm", capped, StringComparison.Ordinal);
        Assert.Contains("... [truncated ", capped, StringComparison.Ordinal);
        Assert.Contains("... [20 more frames]", capped, StringComparison.Ordinal);
        Assert.Contains(@"in ~\src\Frame59.cs:line 59", capped, StringComparison.Ordinal);
        Assert.DoesNotContain("Frame60", capped, StringComparison.Ordinal);
    }

    /// <summary>
    /// The harness root reaches a bundle as the runtime reports it, and only so: as its mark, inside home as well — a
    /// folder such as <c>~\Clients\Acme\Jason</c> says whose work it holds — and its state, which says whether it is the
    /// default, in the runtime's report and in the readiness check composed from it. The bundle has no line of its own
    /// about the root and withholds neither field.
    /// </summary>
    [Fact]
    public async Task The_harness_root_travels_as_its_mark_inside_home_too_with_its_state()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with { Harness = new HarnessInfo(@"C:\Users\Quillon\Clients\SENTINEL_TAMARIND\Jason", HarnessRootState.Marked) };

        var bundle = await ComposeAsync(dir, runtime);

        AssertAbsent(bundle, "SENTINEL_TAMARIND");
        var harness = Section(bundle, "runtime")["system_info"]!["harness"]!;
        Assert.Equal("<harness_root>", Text(harness["root"]));
        Assert.Equal("marked", Text(harness["root_state"]));
        var check = Section(bundle, "status")["checks"]!.AsArray().Single(item => Text(item!["name"]) == "harness_root")!;
        Assert.Contains("<harness_root>", Text(check["fact"]), StringComparison.Ordinal);
        Assert.DoesNotContain(DoctorComposer.SystemInfoWithheld, field => field.Path.StartsWith("harness", StringComparison.Ordinal));
    }

    /// <summary>
    /// A harness root outside the home and the data directory is a name of its own — a folder such as
    /// <c>D:\Clients\Acme\Jason</c> says whose work an installation does — so it is written as its own mark, in the
    /// runtime's report, in the readiness fact composed from it and in a log line, and the header says so. Its state still
    /// travels beside the mark.
    /// </summary>
    [Fact]
    public async Task A_harness_root_outside_home_and_data_is_its_own_mark()
    {
        using var dir = new TempPaths();
        var root = OperatingSystem.IsWindows() ? @"D:\Clients\SENTINEL_CLIENT\Jason" : "/srv/clients/SENTINEL_CLIENT/Jason";
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(1), "Root {A}", null, ("A", Path.Combine(root, "projects"))));
        var runtime = Running(dir);
        runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with { Harness = new HarnessInfo(root, HarnessRootState.Marked) };

        var bundle = await ComposeAsync(dir, runtime);

        var harness = Section(bundle, "runtime")["system_info"]!["harness"]!;
        Assert.Equal("<harness_root>", Text(harness["root"]));
        Assert.Equal("marked", Text(harness["root_state"]));
        var check = Section(bundle, "status")["checks"]!.AsArray().Single(item => Text(item!["name"]) == "harness_root")!;
        Assert.Contains("<harness_root>", Text(check["fact"]), StringComparison.Ordinal);
        Assert.Equal(Path.Combine("<harness_root>", "projects"), Text(Section(bundle, "logs")["slice"]![0]!["A"]));
        Assert.Equal("<harness_root>", Text(Section(bundle, "bundle")["harness_root_written_as"]));
        AssertAbsent(bundle, "SENTINEL_CLIENT");
    }

    /// <summary>
    /// How the installed plugin packages stand against the release's travels whole: ids, versions, digests and states are
    /// the runtime's own words about packages, none of them a person's, so the bundle withholds no field of the section, and
    /// the readiness check composed from it travels beside it.
    /// </summary>
    [Fact]
    public async Task The_released_plugin_packages_travel_as_the_runtime_reports_them()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with
        {
            ReleasedPlugins = [new ReleasedPluginInfo("reply", ReleasedPluginState.Behind, new PluginPackageInfo("0.7.0", "sha256:b"), new PluginPackageInfo("0.6.1", "sha256:a"))],
        };

        var bundle = await ComposeAsync(dir, runtime);

        var entry = Section(bundle, "runtime")["system_info"]!["released_plugins"]!.AsArray().Single()!;
        Assert.Equal("behind", Text(entry["state"]));
        Assert.Equal("0.7.0", Text(entry["released"]!["version"]));
        Assert.Equal("sha256:a", Text(entry["installed"]!["digest"]));
        var check = Section(bundle, "status")["checks"]!.AsArray().Single(item => Text(item!["name"]) == "plugin_package")!;
        Assert.Equal("failed", Text(check["state"]));
        Assert.DoesNotContain(DoctorComposer.SystemInfoWithheld, field => field.Path.StartsWith("released_plugins", StringComparison.Ordinal));
    }

    /// <summary>
    /// And nothing read from under the root does: the bundle lists and opens no folder or file of it, so a word of a memory
    /// file and a memory file's name never reach it, the journal's entries asked for or not, and it says so among what it
    /// never carries. A folder under the root travels only as the journal recorded it: a project link's entry holds the
    /// project's folder and its campaign's, before and after, and with <c>--include-journal</c> those travel in the
    /// journal's entries and nowhere else; without it, nowhere.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nothing_under_the_harness_root_reaches_a_bundle(bool includeJournal)
    {
        using var dir = new TempPaths();
        var home = Path.Combine(Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(home, "Jason");
        var campaign = Path.Combine(root, "projects", "sentinel-project-folder", "campaigns", "sentinel-campaign-folder");
        foreach (var (folder, words) in new[] { (root, "SENTINEL_ROOT_MEMORY"), (Path.GetDirectoryName(Path.GetDirectoryName(campaign))!, "SENTINEL_PROJECT_MEMORY"), (campaign, "SENTINEL_CAMPAIGN_MEMORY") })
        {
            Directory.CreateDirectory(Path.Combine(folder, ".jason"));
            File.WriteAllText(Path.Combine(folder, ".jason", "marker.json"), """{"marker_version":1,"kind":"root"}""");
            File.WriteAllText(Path.Combine(folder, "AGENTS.md"), words + "\n");
            File.WriteAllText(Path.Combine(folder, "CLAUDE.md"), "@AGENTS.md SENTINEL_CLAUDE_WORD\n");
        }

        try
        {
            var runtime = Running(dir);
            runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with { Harness = new HarnessInfo(root, HarnessRootState.Marked) };
            var project = Path.Combine(root, "projects", "sentinel-project-folder");
            var linked = runtime.Entry(0);
            linked["kind"] = "project_linked";
            linked["key"] = "prj_01JB0000000000000000000000";
            linked["old"] = new JsonObject { ["folder_path"] = project, ["campaigns"] = new JsonObject { ["cmp_01JB0000000000000000000000"] = campaign } };
            linked["new"] = new JsonObject { ["folder_path"] = Path.Combine(home, "elsewhere", "acme"), ["campaigns"] = new JsonObject() };
            runtime.Fails = (operation, _) => operation == Operations.JournalList
                ? Respond(HttpStatusCode.OK, new JsonObject { ["items"] = new JsonArray(linked), ["next_cursor"] = null }.ToJsonString())
                : null;

            var bundle = await ComposeAsync(dir, runtime, includeJournal);

            var file = bundle.Serialize();
            Assert.Equal("<harness_root>", Text(Section(bundle, "runtime")["system_info"]!["harness"]!["root"]));
            foreach (var word in (string[])["SENTINEL_ROOT_MEMORY", "SENTINEL_PROJECT_MEMORY", "SENTINEL_CAMPAIGN_MEMORY", "SENTINEL_CLAUDE_WORD", "AGENTS.md", "CLAUDE.md", "marker.json"])
            {
                Assert.DoesNotContain(word, file, StringComparison.Ordinal);
            }

            var outsideTheJournal = bundle.Document.DeepClone().AsObject();
            outsideTheJournal.Remove("journal_entries");
            foreach (var folder in (string[])["sentinel-project-folder", "sentinel-campaign-folder"])
            {
                Assert.DoesNotContain(folder, outsideTheJournal.ToJsonString(), StringComparison.Ordinal);
                Assert.True(
                    file.Contains(folder, StringComparison.Ordinal) == includeJournal,
                    $"'{folder}', which a project link recorded, is {(includeJournal ? "not " : string.Empty)}in a bundle composed {(includeJournal ? "with" : "without")} the journal's entries.");
            }

            var item = Assert.Single(DoctorComposer.NeverCarried, entry => entry.StartsWith("anything read from the harness root", StringComparison.Ordinal));
            Assert.Contains("no folder or file under it is listed or opened", item, StringComparison.Ordinal);
            Assert.Contains("the folders a project link recorded travel only with --include-journal", item, StringComparison.Ordinal);
            Assert.Contains("the folders a project link recorded", DoctorComposer.JournalCarries, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    /// <summary>A data directory outside home is a name of its own, and it is written as its mark wherever it stands.</summary>
    [Fact]
    public async Task A_data_directory_outside_home_is_written_as_its_mark_everywhere()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(1), "Content root path: {ContentRoot}", null, ("ContentRoot", dir.Paths.Root)));

        var bundle = await ComposeAsync(dir, Running(dir));

        Assert.Equal("<data>", Text(Section(bundle, "runtime")["system_info"]!["data_dir"]));
        Assert.Equal("<data>", Text(Section(bundle, "logs")["slice"]![0]!["ContentRoot"]));
        Assert.DoesNotContain(Everything(JsonNode.Parse(bundle.Serialize())!), text => text.Contains(dir.Paths.Root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A runtime that keeps its data inside this installation's data directory: both directories are written as their own
    /// marks, the longer first, and the disagreement itself stays in the bundle beside the mark — it is the fact a supporter
    /// reads, and the mark only keeps the path from travelling as typed.
    /// </summary>
    [Fact]
    public async Task A_runtimes_data_directory_inside_this_one_is_its_own_mark_beside_the_disagreement()
    {
        using var dir = new TempPaths();
        var other = Path.Combine(dir.Paths.Root, "other");
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(1), "Paths {A} {B}", null, ("A", Path.Combine(other, "logs")), ("B", dir.Paths.LogsDirectory)));

        var bundle = await ComposeAsync(dir, Running(dir, dataDir: other));

        var runtime = Section(bundle, "runtime");
        Assert.False(runtime["data_dir_agrees"]!.GetValue<bool>());
        Assert.Equal("<runtime_data>", Text(runtime["system_info"]!["data_dir"]));
        var evt = Section(bundle, "logs")["slice"]![0]!;
        Assert.Equal(Path.Combine("<runtime_data>", "logs"), Text(evt["A"]));
        Assert.Equal(Path.Combine("<data>", "logs"), Text(evt["B"]));
        Assert.Contains(Section(bundle, "status")["checks"]!.AsArray(), check => Text(check!["fact"]).Contains("data directory <runtime_data>", StringComparison.Ordinal));
        Assert.DoesNotContain(Everything(JsonNode.Parse(bundle.Serialize())!), text => text.Contains(dir.Paths.Root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>And the other way round: this installation's data directory inside the runtime's.</summary>
    [Fact]
    public async Task This_data_directory_inside_the_runtimes_is_its_own_mark_beside_the_disagreement()
    {
        using var dir = new TempPaths();
        var outer = Path.GetDirectoryName(dir.Paths.Root)!;
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(1), "Paths {A} {B}", null, ("A", Path.Combine(outer, "x")), ("B", dir.Paths.LogsDirectory)));

        var bundle = await ComposeAsync(dir, Running(dir, dataDir: outer));

        var runtime = Section(bundle, "runtime");
        Assert.False(runtime["data_dir_agrees"]!.GetValue<bool>());
        Assert.Equal("<runtime_data>", Text(runtime["system_info"]!["data_dir"]));
        var evt = Section(bundle, "logs")["slice"]![0]!;
        Assert.Equal(Path.Combine("<runtime_data>", "x"), Text(evt["A"]));
        Assert.Equal(Path.Combine("<data>", "logs"), Text(evt["B"]));
        Assert.Equal(Path.Combine("<data>", "logs"), Text(Section(bundle, "logs")["directory"]));
        Assert.DoesNotContain(Everything(JsonNode.Parse(bundle.Serialize())!), text => text.Contains(outer, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A runtime's data directory under another account's profile is written whole, the other account's name not left in a piece of it.</summary>
    [Fact]
    public async Task A_runtimes_data_directory_in_another_profile_is_written_whole()
    {
        using var dir = new TempPaths();

        var bundle = await ComposeAsync(dir, Running(dir, dataDir: @"C:\Users\someoneelse\jason"));

        Assert.False(Section(bundle, "runtime")["data_dir_agrees"]!.GetValue<bool>());
        Assert.Equal("<runtime_data>", Text(Section(bundle, "runtime")["system_info"]!["data_dir"]));
        AssertAbsent(bundle, "someoneelse");
    }

    /// <summary>One directory has one mark: where the runtime's is this installation's own, however it is spelled, it is only <c>&lt;data&gt;</c>.</summary>
    [Fact]
    public async Task Where_the_two_data_directories_agree_only_data_is_written()
    {
        using var dir = new TempPaths();

        var bundle = await ComposeAsync(dir, Running(dir, dataDir: dir.Paths.Root + Path.DirectorySeparatorChar));

        Assert.True(Section(bundle, "runtime")["data_dir_agrees"]!.GetValue<bool>());
        Assert.Equal("<data>" + Path.DirectorySeparatorChar, Text(Section(bundle, "runtime")["system_info"]!["data_dir"]));
        Assert.DoesNotContain("<runtime_data>", Section(bundle, "runtime").ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// On Windows a long folder name has a short form, and a runtime that reports its data directory by it reports this
    /// one: the two agree, and the short form is written as <c>&lt;data&gt;</c> too.
    /// </summary>
    [Fact]
    public async Task A_data_directory_reported_by_its_short_form_is_this_one()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows keeps a short form of a folder's name.");
        using var dir = new TempPaths();
        var shortForm = ShortFormOf(dir.Paths.Root);
        Assert.SkipWhen(shortForm is null || string.Equals(shortForm, dir.Paths.Root, StringComparison.OrdinalIgnoreCase), "This volume keeps no short names.");

        var bundle = await ComposeAsync(dir, Running(dir, dataDir: shortForm));

        Assert.True(Section(bundle, "runtime")["data_dir_agrees"]!.GetValue<bool>());
        Assert.Equal("<data>", Text(Section(bundle, "runtime")["system_info"]!["data_dir"]));
    }

    /// <summary>
    /// The frame is this program's own text: a person called <c>status</c> and a machine called <c>user</c> rename no
    /// section and change no mark, and a person called <c>x</c>, whose name the key of an exception becomes, still has the
    /// exception capped, since it is found where it stands rather than by its key.
    /// </summary>
    [Theory]
    [InlineData("status", "user")]
    [InlineData("x", "logs")]
    public async Task Names_that_are_the_bundles_own_words_change_none_of_its_frame(string user, string machine)
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        var exception = "System.Exception: boom" + string.Concat(Enumerable.Range(0, 70).Select(n => $"\n   at Jason.F{n}()"));
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(1), "A failure", "Error", ("@x", exception)));

        var bundle = await ComposeAsync(dir, Running(dir), identity: new IdentityFacts(@"C:\Users\" + user, user, machine, null, null));

        Assert.Equal(["bundle", "versions", "status", "runtime", "routes", "config", "logs", "journal", "work_items"], Keys(bundle.Document));
        Assert.Equal(
            ["~", "<data>", "<runtime_data>", "<harness_root>", "<install_dir>", "<user>", "<machine>", "<domain>", "<sid>", "<plugin-N>", "<role-N>", "<skill-pack-N>", "<skill-source>"],
            Section(bundle, "bundle").Where(member => member.Key.EndsWith("_written_as", StringComparison.Ordinal)).Select(member => Text(member.Value)));
        Assert.Equal(["versions", "status", "runtime", "routes", "config", "logs", "journal", "work_items"], bundle.Contents.Select(section => section.Name));

        var failure = bundle.Document["logs"]!.AsObject().First(member => member.Value is JsonArray { Count: 1 } list && list[0]!["@mt"] is not null).Value![0]!.AsObject();
        var capped = failure.Select(member => member.Value).OfType<JsonValue>().Select(value => value.GetValue<string>()).Single(text => text.StartsWith("System.Exception", StringComparison.Ordinal));
        Assert.EndsWith("... [10 more frames]", capped, StringComparison.Ordinal);
    }

    // ---- what a runtime that is not the real one can answer, and what it cannot cost ------------------------------

    /// <summary>
    /// A plugin listing at its caps, or one built to be hostile, is held to a budget of its own inside the runtime section,
    /// in the bundle's own bytes and in the listing's order: the runtime's report and whether the data directories agree
    /// are never lost to it.
    /// </summary>
    [Theory]
    [InlineData(1_500, 1_000, 200)]
    [InlineData(1_000, 128, 20)]
    public async Task A_plugin_listing_never_costs_the_runtimes_report(int count, int idLength, int codes)
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var problems = new JsonArray([.. Enumerable.Range(0, codes).Select(n => (JsonNode)new JsonObject { ["code"] = "c" + new string('x', 59) + n.ToString("D3", CultureInfo.InvariantCulture), ["path"] = "p", ["message"] = "m" })]);
        var plugins = new JsonArray([.. Enumerable.Range(0, count).Select(n => (JsonNode)new JsonObject
        {
            ["id"] = (n.ToString("D4", CultureInfo.InvariantCulture) + new string('i', idLength))[..idLength],
            ["version"] = new string('9', Math.Min(idLength, 128)),
            ["kind"] = "provider",
            ["digest"] = new string('d', Math.Min(idLength, 128)),
            ["status"] = "unavailable",
            ["problems"] = problems.DeepClone(),
        })]);
        var body = new JsonObject
        {
            ["snapshot"] = new JsonObject { ["id"] = "snp_1", ["loaded_at"] = "2026-10-04T11:00:00.000Z", ["source"] = "startup", ["plugin_count"] = count },
            ["routing_snapshot_id"] = "rts_1",
            ["plugins"] = plugins,
            ["last_reload"] = null,
            ["activated"] = true,
        }.ToJsonString();
        runtime.Fails = (operation, _) => operation == Operations.PluginList ? Respond(HttpStatusCode.OK, body) : null;

        var bundle = await ComposeAsync(dir, runtime);

        var section = Section(bundle, "runtime");
        Assert.Equal(InstanceId, Text(section["system_info"]!["instance_id"]));
        Assert.True(section["data_dir_agrees"]!.GetValue<bool>());
        Assert.True(section["plugins_truncated"]!.GetValue<bool>());
        var listed = section["plugins"]!.AsArray();
        Assert.InRange(listed.Count, 1, count - 1);
        if (idLength <= DoctorComposer.MaxPluginText)
        {
            // In the listing's order, the first ones kept: newest first means nothing for plugins. Their ids are no plugin
            // this repository ships, so each is its alias, numbered in the ids' order, which is the listing's here.
            Assert.Equal(
                Enumerable.Range(1, listed.Count).Select(n => $"<plugin-{n}>"),
                listed.Select(plugin => Text(plugin!["id"])));
        }

        Assert.True(Encoding.UTF8.GetByteCount(listed.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) <= DoctorComposer.PluginsBytes);
        Assert.Equal(listed.Count, bundle.Contents.Single(entry => entry.Name == "runtime").Items);

        // The plugins take at most half of the section, so what system.info and the rest need is never left to luck.
        Assert.InRange(DoctorComposer.PluginsBytes, 1, DoctorComposer.SectionBytes / 2);
    }

    /// <summary>On Windows a path is the same path in another case, and the runtime reporting this directory so reports this one.</summary>
    [Fact]
    public async Task Data_dir_agrees_ignores_case_where_paths_have_none()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows paths are the same path in another case.");
        using var dir = new TempPaths();

        var bundle = await ComposeAsync(dir, Running(dir, dataDir: dir.Paths.Root.ToUpperInvariant()));

        Assert.True(Section(bundle, "runtime")["data_dir_agrees"]!.GetValue<bool>());
        Assert.Equal("<data>", Text(Section(bundle, "runtime")["system_info"]!["data_dir"]));
    }

    /// <summary>
    /// The status checks read first, and catch a timeout themselves: a body of theirs that stalls closes the latch all the
    /// same, so it is the last read the bundle makes.
    /// </summary>
    [Fact]
    public async Task A_status_checks_body_that_stalls_is_the_last_read_made()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, call) => operation == Operations.SystemInfo && call == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent(TimeSpan.Zero) }
            : null;

        var bundle = await ComposeAsync(dir, runtime, includeJournal: true);

        Assert.Equal([Operations.SystemInfo], runtime.Requests.Select(request => request.Operation));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "runtime")["reason"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "routes")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "journal")["not_read"]));
        Assert.Equal(DoctorComposer.NotAttempted, Text(Section(bundle, "work_items")["not_read"]));
    }

    /// <summary>A key written twice is a malformed answer, and a bundle is still composed and written from it.</summary>
    [Fact]
    public async Task A_system_info_with_a_key_written_twice_still_gives_a_bundle()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var body = JsonSerializer.Serialize(StubRuntime.EveryField(dir.Paths.Root), JasonJson.Options)
            .Replace("\"update\":", "\"update\":null,\"update\":", StringComparison.Ordinal);
        runtime.Fails = (operation, _) => operation == Operations.SystemInfo ? Respond(HttpStatusCode.OK, body) : null;

        var bundle = await ComposeAsync(dir, runtime);

        Assert.True(Section(bundle, "runtime")["answered"]!.GetValue<bool>());
        Assert.NotEmpty(Section(bundle, "status")["checks"]!.AsArray());

        using var verb = new TempPaths();
        var again = Running(verb);
        again.Fails = (operation, _) => operation == Operations.SystemInfo ? Respond(HttpStatusCode.OK, body) : null;
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["doctor", "--dry-run"], Machine(verb, again), Ct));
    }

    /// <summary>
    /// An answer that leaves out a section every reader of <c>system.info</c> goes into is not a runtime's: a runtime
    /// sends every one, and something else answering at the address may not. It is said to be unreadable, the readiness
    /// checks read it as no answer, and a bundle is still written — read as an answer, the checks failed on it and the
    /// verb wrote nothing.
    /// </summary>
    [Theory]
    [InlineData("nothing")]
    [InlineData("no_database")]
    [InlineData("empty_sections")]
    [InlineData("no_roles")]
    [InlineData("no_channels")]
    [InlineData("no_migrations")]
    public async Task A_system_info_without_a_section_its_readers_need_is_unreadable_and_still_gives_a_bundle(string variant)
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var answer = variant == "nothing" ? new JsonObject() : JsonSerializer.SerializeToNode(StubRuntime.EveryField(dir.Paths.Root), JasonJson.Options)!.AsObject();
        switch (variant)
        {
            case "no_database":
                answer.Remove("database");
                break;
            case "empty_sections":
                answer["database"] = new JsonObject();
                answer["dispatcher"] = new JsonObject();
                answer["plugins"] = new JsonObject();
                answer["routes"] = new JsonObject();
                break;
            case "no_roles":
                answer["skills"]!["roles"] = null;
                break;
            case "no_channels":
                answer["notifications"]!["channels"] = null;
                break;
            case "no_migrations":
                answer["database"]!["applied_migrations"] = null;
                break;
        }

        var body = answer.ToJsonString();
        runtime.Fails = (operation, _) => operation == Operations.SystemInfo ? Respond(HttpStatusCode.OK, body) : null;

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Equal(DoctorComposer.UnreadableAnswer, Text(Section(bundle, "runtime")["reason"]));
        var check = Section(bundle, "status")["checks"]!.AsArray().Single(each => Text(each!["name"]) == "runtime")!;
        Assert.Contains("nothing answered there", Text(check["fact"]), StringComparison.Ordinal);

        using var verb = new TempPaths();
        var again = Running(verb);
        again.Fails = (operation, _) => operation == Operations.SystemInfo ? Respond(HttpStatusCode.OK, body) : null;
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["doctor", "--dry-run"], Machine(verb, again), Ct));
    }

    /// <summary>An answer that cannot be read at all is said to be one, and the rest of the bundle is as it would be.</summary>
    [Fact]
    public async Task A_system_info_that_cannot_be_read_is_said_to_be_unreadable()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation == Operations.SystemInfo ? Respond(HttpStatusCode.OK, "{ not json") : null;

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Equal(["answered", "reason"], Keys(Section(bundle, "runtime")));
        Assert.Equal(DoctorComposer.UnreadableAnswer, Text(Section(bundle, "runtime")["reason"]));
        Assert.NotEmpty(Section(bundle, "status")["checks"]!.AsArray());
        Assert.Equal(["settings_file", "environment"], Keys(Section(bundle, "config")));
    }

    /// <summary>
    /// Only what the contract names travels: a withheld field spelled in another case is no field of the answer's, and
    /// the readiness facts quote none of it either.
    /// </summary>
    [Fact]
    public async Task A_withheld_field_spelled_in_another_case_travels_nowhere()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var answer = JsonSerializer.SerializeToNode(StubRuntime.EveryField(dir.Paths.Root), JasonJson.Options)!.AsObject();
        foreach (var channel in answer["notifications"]!["channels"]!.AsArray())
        {
            channel!.AsObject().Remove("detail");
            channel.AsObject()["Detail"] = "SENTINEL_CASED_DETAIL";
        }

        answer["skills"]!.AsObject().Remove("problem");
        answer["skills"]!["Problem"] = "SENTINEL_CASED_PROBLEM";
        runtime.Fails = (operation, _) => operation == Operations.SystemInfo ? Respond(HttpStatusCode.OK, answer.ToJsonString()) : null;

        var bundle = await ComposeAsync(dir, runtime);

        AssertAbsent(bundle, "SENTINEL_CASED_DETAIL", "SENTINEL_CASED_PROBLEM");
    }

    /// <summary>
    /// Two roles Jason ships whose skills the runtime refused: the readiness fact says so as one closed value and how many, never the
    /// operating system's words for it — a projection of the bundle's own; <c>jason status</c> says what it always said.
    /// </summary>
    [Fact]
    public async Task Two_withheld_role_problems_read_as_one_word_and_a_count()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Roster = [("researcher", true), ("planner", true)];
        runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with
        {
            Skills = new SkillsInfo(
                Path.Combine(dir.Paths.Root, "skills", "roles"),
                4096,
                [
                    new DeployedRoleSkill("researcher", 100, "SENTINEL_ROLE_PROBLEM: access denied", Jason.Contracts.Skills.SkillProblemCodes.SkillTooLarge),
                    new DeployedRoleSkill("planner", 100, "SENTINEL_OTHER_PROBLEM: in use", Jason.Contracts.Skills.SkillProblemCodes.SkillFileMissing),
                ],
                null),
        };

        var bundle = await ComposeAsync(dir, runtime);

        // How many, and which: the codes are the closed set's words, so they travel where the sentences do not.
        var fact = Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "role_skills")!;
        Assert.Equal(DoctorComposer.Withheld + " (2: skill_too_large, skill_file_missing)", Text(fact["fact"]));
        AssertAbsent(bundle, "SENTINEL_ROLE_PROBLEM", "SENTINEL_OTHER_PROBLEM");
    }

    /// <summary>
    /// A skill problem's code and the word for a refusal travel as written when they are words of their closed sets, and
    /// are withheld when they are anything else: a runtime that is not this build cannot put text through these keys.
    /// </summary>
    [Fact]
    public async Task A_skill_problem_s_code_travels_when_it_is_one_of_the_set_s_words_and_is_withheld_otherwise()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Roster = [("researcher", true), ("planner", true)];
        runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with
        {
            Skills = new SkillsInfo(
                Path.Combine(dir.Paths.Root, "skills", "roles"),
                4096,
                [
                    new DeployedRoleSkill("researcher", null, "SENTINEL_ROLE_PROBLEM: denied", Jason.Contracts.Skills.SkillProblemCodes.RoleDirectoryUnreadable, "UnauthorizedAccessException"),
                    new DeployedRoleSkill("planner", null, "SENTINEL_OTHER_PROBLEM: denied", "SENTINEL_STRANGE_CODE", "SENTINEL_STRANGE_EXCEPTION"),
                ],
                null),
        };

        var bundle = await ComposeAsync(dir, runtime);

        var roles = Section(bundle, "runtime")["system_info"]!["skills"]!["roles"]!.AsArray();
        Assert.Equal(("role_directory_unreadable", "UnauthorizedAccessException"), (Text(roles[0]!["problem_code"]), Text(roles[0]!["problem_exception"])));
        Assert.Equal((DoctorComposer.Withheld, DoctorComposer.Withheld), (Text(roles[1]!["problem_code"]), Text(roles[1]!["problem_exception"])));
        AssertAbsent(bundle, "SENTINEL_ROLE_PROBLEM", "SENTINEL_OTHER_PROBLEM", "SENTINEL_STRANGE_CODE", "SENTINEL_STRANGE_EXCEPTION");

        // And the status fact names only the code it may: the strange one is counted, not named.
        var fact = Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "role_skills")!;
        Assert.Equal(DoctorComposer.Withheld + " (2: role_directory_unreadable)", Text(fact["fact"]));
    }

    /// <summary>
    /// A refused skill of a role the person added, beside one of a role Jason ships: the withheld problems are still one
    /// closed value and a count, and the sentence the status verb adds for the person's own role follows it as composed,
    /// its name as its alias — a role's name is the word of the person who added it — and its directory as the bundle
    /// writes paths, and nothing else: the shipped role's problem
    /// text, planted with a sentinel, is in the count and nowhere in the bundle. Were the sentence to stop the summary, the
    /// problems' codes would travel one by one again.
    /// </summary>
    [Fact]
    public async Task A_refused_role_of_the_person_s_own_keeps_its_sentence_after_the_withheld_summary()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Roster = [("researcher", true), ("sentinel-scout", false)];
        runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with
        {
            Skills = new SkillsInfo(
                Path.Combine(dir.Paths.Root, "skills", "roles"),
                4096,
                [
                    new DeployedRoleSkill("researcher", 100, "SENTINEL_ROLE_PROBLEM: access denied", Jason.Contracts.Skills.SkillProblemCodes.SkillTooLarge),
                    new DeployedRoleSkill("sentinel-scout", 100, "SENTINEL_OTHER_PROBLEM: in use", Jason.Contracts.Skills.SkillProblemCodes.SkillFileMissing),
                ],
                null),
        };

        var bundle = await ComposeAsync(dir, runtime);

        var fact = Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "role_skills")!;
        Assert.Equal(
            DoctorComposer.Withheld + " (2: skill_too_large, skill_file_missing) '<role-1>' is no role Jason ships, so no command of Jason's"
                + $" repairs its skill: correct it or remove it from {Path.Combine("<data>", "skills", "roles")}.",
            Text(fact["fact"]));
        AssertAbsent(bundle, "SENTINEL_ROLE_PROBLEM", "SENTINEL_OTHER_PROBLEM", "sentinel-scout");
    }

    /// <summary>
    /// A role's name is the word of the person who added it — <c>acme-closer</c> says whose work an installation does — so
    /// only the names of the roles Jason ships travel. A role of the person's own is written as its alias wherever a bundle
    /// would name it: among the role skills the runtime reports, in the readiness fact that notes it, whether it is on the
    /// roster without a skill or has a skill and is on no roster, and as the role of a failed work item. One alias per
    /// name, numbered in the names' order, so two roles are told apart and one role is seen to be one; a role Jason ships
    /// travels as written in every one of these places.
    /// </summary>
    [Fact]
    public async Task A_role_of_the_person_s_own_travels_nowhere_by_its_name()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        runtime.Roster = [.. Jason.Contracts.Api.ShippedRoles.Names.Select(name => (name, true)), ("sentinel-roster-role", false)];
        runtime.RoleOf = "sentinel-failed-role";
        runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with
        {
            Skills = new SkillsInfo(
                Path.Combine(dir.Paths.Root, "skills", "roles"),
                4096,
                [.. Jason.Contracts.Api.ShippedRoles.Names.Select(name => new DeployedRoleSkill(name, 100, null)), new DeployedRoleSkill("sentinel-stray-skill", 100, null)],
                null),
        };

        var bundle = await ComposeAsync(dir, runtime);

        AssertAbsent(bundle, "sentinel-roster-role", "sentinel-failed-role", "sentinel-stray-skill");
        var roles = Section(bundle, "runtime")["system_info"]!["skills"]!["roles"]!.AsArray().Select(role => Text(role!["role"])).ToList();
        // sentinel-failed-role, sentinel-roster-role, sentinel-stray-skill: 1, 2 and 3, in the names' order.
        Assert.Equal([.. Jason.Contracts.Api.ShippedRoles.Names, "<role-3>"], roles);
        var failures = Section(bundle, "work_items")["recent_failures"]!.AsArray();
        Assert.Contains(failures, failure => Text(failure!["kind"]) == "ai_role" && Text(failure["role"]) == "<role-1>");
        var fact = Text(Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "role_skills")!["fact"]);
        Assert.Contains("'<role-3>' is deployed and is no role on this runtime's roster", fact, StringComparison.Ordinal);
        Assert.Contains("'<role-2>' is a role of your own", fact, StringComparison.Ordinal);
        Assert.Equal("<role-N>", Text(Section(bundle, "bundle")["role_written_as"]));
        Assert.Equal(NameAliases.Numbering, Text(Section(bundle, "bundle")["aliases_numbered"]));
    }

    /// <summary>
    /// A plugin's id is its package's name, and a package this repository does not ship is one a person chose — an in-house
    /// <c>acme-crm</c> says whose work an installation does. Its id is written as one alias wherever the bundle would carry
    /// it: the runtime's plugins, the routes, the readiness facts, a notification channel's name, the settings' keys and
    /// the log slice, a folder in a path included. A plugin this repository ships travels by name beside it, and a package's
    /// digest is not a name and stays.
    /// </summary>
    [Fact]
    public async Task A_plugin_this_repository_does_not_ship_travels_nowhere_by_its_id()
    {
        using var dir = new TempPaths();
        const string Own = "sentinel-crm";
        var shipped = ShippedPlugins.Ids[0];
        foreach (var id in (string[])[Own, shipped])
        {
            Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, id));
            File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, id, "plugin.yaml"), $"id: {id}");
        }

        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        var grants = new JsonObject { [Own] = new JsonObject { ["Exec"] = new JsonArray("x") }, [shipped] = new JsonObject { ["Exec"] = new JsonArray("y") } };
        File.WriteAllText(dir.Paths.UserSettingsFile, new JsonObject { ["Plugins"] = new JsonObject { ["Grants"] = grants } }.ToJsonString());
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(
            dir.Paths.LogsDirectory,
            "runtime-20261004.jsonl",
            Event(At(1), "Plugin {Directory} is unavailable on this machine: {Codes}", "Warning", ("Directory", Own), ("Codes", "executable_missing")),
            Event(At(2), "Read {Path}", null, ("Path", Path.Combine(dir.Paths.PluginsDirectory, Own, "main.js"))));

        var runtime = Running(dir);
        runtime.Registry = new PluginRegistryDto(
            new SnapshotDto("snp_01JDOCTOR", Now, SnapshotSource.Startup, 2),
            "rts_01JDOCTOR",
            [StubRuntime.Plugin(Own, "sha256:own", PluginStatus.Unavailable, [new PluginProblemDto("executable_missing", "p", "m")]), StubRuntime.Plugin(shipped, "sha256:shipped", PluginStatus.Valid, [])],
            LastReload: null,
            Activated: true);
        var every = StubRuntime.EveryField(dir.Paths.Root);
        runtime.Info = every with
        {
            Notifications = every.Notifications! with
            {
                Channels = [.. every.Notifications!.Channels, new NotificationChannelInfo(Own, "plugin", true, null, null, null, null, null, null, null, null)],
            },
        };
        var routes = new JsonObject
        {
            ["routing_snapshot_id"] = "rts_01JDOCTOR",
            ["plugin_snapshot_id"] = "snp_01JDOCTOR",
            ["global"] = new JsonObject { ["default"] = new JsonObject { ["plugin_id"] = Own, ["binding"] = null, ["binding_identity"] = null }, ["operations"] = new JsonObject() },
            ["campaigns"] = new JsonArray(),
        };
        runtime.Fails = (operation, _) => operation == Operations.RouteList ? Respond(HttpStatusCode.OK, routes.ToJsonString()) : null;

        var bundle = await ComposeAsync(dir, runtime);

        AssertAbsent(bundle, Own);
        var plugins = Section(bundle, "runtime")["plugins"]!.AsArray();
        Assert.Equal(["<plugin-1>", shipped], plugins.Select(plugin => Text(plugin!["id"])).Order(StringComparer.Ordinal));
        Assert.Contains(plugins, plugin => Text(plugin!["id"]) == "<plugin-1>" && Text(plugin["digest"]) == "sha256:own");
        Assert.Equal("<plugin-1>", Text(Section(bundle, "routes")["global"]![0]!["plugin_id"]));
        var availability = Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "plugin_availability")!;
        Assert.StartsWith("<plugin-1> is unavailable", Text(availability["fact"]), StringComparison.Ordinal);
        Assert.Contains(Section(bundle, "runtime")["system_info"]!["notifications"]!["channels"]!.AsArray(), channel => Text(channel!["channel"]) == "<plugin-1>");
        var keys = Section(bundle, "config")["settings_file"]!["shape"]!["members"]!["Plugins"]!["members"]!["Grants"]!["members"]!.AsObject();
        Assert.Equal(["<plugin-1>", shipped], keys.Select(member => member.Key).Order(StringComparer.Ordinal));
        var slice = Section(bundle, "logs")["slice"]!.AsArray();
        Assert.Equal("<plugin-1>", Text(slice[0]!["Directory"]));
        Assert.Equal(Path.Combine("<data>", "plugins", "<plugin-1>", "main.js"), Text(slice[1]!["Path"]));
        Assert.Equal("<plugin-N>", Text(Section(bundle, "bundle")["plugin_id_written_as"]));
    }

    /// <summary>
    /// A skill pack is named in a skills record, and a pack Jason does not ship, deployed from a source of the person's own,
    /// is named by them: the readiness fact that lists what is deployed into an agent's harness writes it as its alias, and
    /// a pack Jason ships beside it by name.
    /// </summary>
    [Fact]
    public async Task A_skill_pack_jason_does_not_ship_travels_nowhere_by_its_name()
    {
        using var dir = new TempPaths();
        var harness = Path.Combine(dir.Paths.Root, "no-harness-here");
        Directory.CreateDirectory(harness);
        File.WriteAllText(Path.Combine(harness, "SKILL.md"), "x");
        IReadOnlyList<Jason.Contracts.Skills.DeployedFile> files = [new("SKILL.md", "sha256:x")];
        var record = new Jason.Contracts.Skills.SkillsRecord(
            Jason.Contracts.Skills.SkillsRecord.CurrentVersion,
            [
                new("sentinel-own-pack", SkillsSource.DefaultSource, "v0.3.0", false, null, Now, files),
                new(SkillPacks.Runtime, SkillsSource.DefaultSource, "v0.3.0", false, null, Now, files),
            ]);
        File.WriteAllText(Path.Combine(harness, Jason.Contracts.Skills.SkillsRecord.FileName), JsonSerializer.Serialize(record, JasonJson.Options));

        var bundle = await ComposeAsync(dir, Running(dir));

        AssertAbsent(bundle, "sentinel-own-pack");
        var fact = Text(Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "harness_skills")!["fact"]);
        Assert.Contains("<skill-pack-1> at v0.3.0", fact, StringComparison.Ordinal);
        Assert.Contains(SkillPacks.Runtime + " at v0.3.0", fact, StringComparison.Ordinal);
        Assert.Equal("<skill-pack-N>", Text(Section(bundle, "bundle")["skill_pack_written_as"]));
    }

    /// <summary>
    /// A skill source given with <c>--source</c> other than the public repository — a fork's address, a folder of the
    /// person's own — names whose copy it is, and so does the branch or tag it was taken at. Both are written as
    /// <c>&lt;skill-source&gt;</c> in the two readiness facts that quote a deployment, the role skills and the skills
    /// deployed into an agent's folder; the public repository's address is never in a fact, and a release tag says nothing
    /// of anybody, so both travel as they are.
    /// </summary>
    [Fact]
    public async Task A_skill_source_of_the_person_s_own_travels_as_its_mark_with_its_ref()
    {
        using var dir = new TempPaths();
        var fork = "https://example.com/sentinel-fork/jason.git";
        var folder = new Uri(Path.Combine(dir.Paths.Root, "sentinel-folder")).AbsoluteUri;
        var roles = Path.Combine(dir.Paths.Root, "skills", "roles");
        var harness = Path.Combine(dir.Paths.Root, "no-harness-here");
        IReadOnlyList<Jason.Contracts.Skills.DeployedFile> files = [new("SKILL.md", "sha256:x")];
        foreach (var root in (string[])[roles, harness])
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "SKILL.md"), "x");
        }

        void Record(string root, params Jason.Contracts.Skills.SkillsDeployment[] packs) =>
            File.WriteAllText(
                Path.Combine(root, Jason.Contracts.Skills.SkillsRecord.FileName),
                JsonSerializer.Serialize(new Jason.Contracts.Skills.SkillsRecord(Jason.Contracts.Skills.SkillsRecord.CurrentVersion, packs), JasonJson.Options));
        Record(roles, new Jason.Contracts.Skills.SkillsDeployment(SkillPacks.Runtime, fork, "sentinel-branch", true, "c0ffee", Now, files));
        Record(
            harness,
            new Jason.Contracts.Skills.SkillsDeployment(SkillPacks.Runtime, folder, "sentinel-tag", true, "c0ffee", Now, files),
            new Jason.Contracts.Skills.SkillsDeployment(SkillPacks.Business, SkillsSource.DefaultSource, "v1.2.3", true, "c0ffee", Now, files));
        var runtime = Running(dir);
        runtime.Info = StubRuntime.EveryField(dir.Paths.Root) with
        {
            RuntimeVersion = "1.2.4",
            Skills = new SkillsInfo(roles, 4096, [], null),
        };

        var bundle = await ComposeAsync(dir, runtime, build: "1.2.4");

        AssertAbsent(bundle, "sentinel-fork", "sentinel-folder", "sentinel-branch", "sentinel-tag");
        var checks = Section(bundle, "status")["checks"]!.AsArray();
        var roleSkills = Text(checks.Single(check => Text(check!["name"]) == "role_skills")!["fact"]);
        Assert.Contains("Role skills are from <skill-source>;", roleSkills, StringComparison.Ordinal);
        Assert.Contains("--source <skill-source>`", roleSkills, StringComparison.Ordinal);
        var harnessSkills = Text(checks.Single(check => Text(check!["name"]) == "harness_skills")!["fact"]);
        Assert.Contains($"{SkillPacks.Runtime} at <skill-source>", harnessSkills, StringComparison.Ordinal);
        Assert.Contains($"--pack {SkillPacks.Runtime} --source <skill-source>`", harnessSkills, StringComparison.Ordinal);
        Assert.Contains($"{SkillPacks.Business} at v1.2.3", harnessSkills, StringComparison.Ordinal);
        Assert.Contains($"`jason skills install --pack {SkillPacks.Business}` moves it", harnessSkills, StringComparison.Ordinal);
        Assert.Equal("<skill-source>", Text(Section(bundle, "bundle")["skill_source_written_as"]));
        Assert.Contains(DoctorComposer.NeverCarried, item => item.Contains("<skill-source>", StringComparison.Ordinal));
    }

    /// <summary>
    /// A folder under <c>plugins/</c> whose name is not a well-formed id is never loaded, so neither the registry nor the
    /// settings name it — but the loader's line about it does, by the folder's name, which the person chose. It is written as
    /// its alias, whether the folder is still there or the line is all that is left of it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_plugin_folder_that_is_no_well_formed_id_travels_as_its_alias(bool folderThere)
    {
        using var dir = new TempPaths();
        const string Folder = "Sentinel_Marmot_CRM";
        if (folderThere)
        {
            Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, Folder));
            File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, Folder, "plugin.yaml"), "id: x");
        }

        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(1), "Plugin candidate {Directory} is not loadable: {Codes}", "Warning", ("Directory", Folder), ("Codes", "manifest_invalid")));

        var bundle = await ComposeAsync(dir, Running(dir));

        AssertAbsent(bundle, "Marmot");
        Assert.Equal("<plugin-1>", Text(Section(bundle, "logs")["slice"]![0]!["Directory"]));
    }

    /// <summary>
    /// A package used and then removed is named by nothing but the log: an older day's lines of the plugin invoker, at
    /// information and warning, by its id. Every event the bundle carries is read for the id, so it is its alias there.
    /// </summary>
    [Fact]
    public async Task A_removed_plugin_that_only_older_log_lines_name_travels_as_its_alias()
    {
        using var dir = new TempPaths();
        const string Removed = "sentinel-falconbridge";
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(
            dir.Paths.LogsDirectory,
            "runtime-20261003.jsonl",
            Event(At(1), "Plugin {PluginId} {Version} {Digest} invocation {InvocationId} for {Operation} started as pid {Pid}", null, ("PluginId", Removed), ("Version", "1.0.0"), ("Digest", "sha256:ab"), ("InvocationId", "inv_01"), ("Operation", "contact.find"), ("Pid", "4242")),
            Event(At(2), "Plugin invocation {InvocationId} of {PluginId} {Operation} was refused before launch: {Code}", "Warning", ("InvocationId", "inv_02"), ("PluginId", Removed), ("Operation", "contact.find"), ("Code", "plugin_not_found")),
            Event(At(3), "Plugin invocation {InvocationId} of {PluginId} {Operation} was refused before launch: {Code}", "Error", ("InvocationId", "inv_03"), ("PluginId", Removed), ("Operation", "contact.find"), ("Code", "plugin_not_found")));
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(4), "Runtime {InstanceId} stopped", null, ("InstanceId", InstanceId)));

        var bundle = await ComposeAsync(dir, Running(dir));

        AssertAbsent(bundle, "falconbridge");
        var logs = Section(bundle, "logs");
        var named = logs["slice"]!.AsArray().Where(evt => evt!["PluginId"] is not null).Select(evt => Text(evt!["PluginId"])).ToList();
        Assert.Equal(3, named.Count);
        var alias = Assert.Single(named.Distinct());
        Assert.Matches("^<plugin-[0-9]+>$", alias);
        Assert.Equal(alias, Text(logs["recent_errors"]![0]!["PluginId"]));
    }

    /// <summary>
    /// Each line of the runtime's that names a plugin under a property of its own, by the template the client holds for
    /// it, has that property written as its alias; the runtime's own channel, <c>toast</c>, names no plugin and travels.
    /// </summary>
    [Fact]
    public async Task Every_line_that_names_a_plugin_by_its_template_has_it_written_as_its_alias()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        var templates = LoggedNames.PluginTemplates.ToList();
        Write(
            dir.Paths.LogsDirectory,
            "runtime-20261004.jsonl",
            [
                .. templates.Select((template, n) => Event(At(n), template.Key, "Warning", (template.Value, $"sentinel-logged-{n}"))),
                Event(At(templates.Count), "Notification channel {Channel} failed ({Code}, {Class})", "Warning", ("Channel", "toast"), ("Code", "toast_failed"), ("Class", "transient")),
            ]);

        var bundle = await ComposeAsync(dir, Running(dir));

        AssertAbsent(bundle, "sentinel-logged");
        var slice = Section(bundle, "logs")["slice"]!.AsArray();
        var aliases = Enumerable.Range(0, templates.Count).Select(n => Text(slice[n]![templates[n].Value])).ToList();
        Assert.All(aliases, alias => Assert.Matches("^<plugin-[0-9]+>$", alias));
        Assert.Equal(templates.Count, aliases.Distinct().Count());

        Assert.Equal("toast", Text(slice[templates.Count]!["Channel"]));
    }

    /// <summary>The lines that name a plugin under a property of their own, but for the line about a notification channel.</summary>
    public static TheoryData<string> LinesOtherThanAChannels() =>
        [.. LoggedNames.PluginTemplates.Keys.Where(template => template != "Notification channel {Channel} failed ({Code}, {Class})").Order(StringComparer.Ordinal)];

    /// <summary>
    /// <c>toast</c> names no plugin only on the line about a notification channel, the one line whose property can be the
    /// runtime's own channel: on any other line that names a plugin the same word is a plugin's id, and travels as its
    /// alias.
    /// </summary>
    [Theory]
    [MemberData(nameof(LinesOtherThanAChannels))]
    public async Task The_runtime_s_own_channel_is_left_as_it_is_only_on_the_line_about_a_channel(string template)
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        var property = LoggedNames.PluginTemplates[template];
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(0), template, "Warning", (property, "toast")));

        var bundle = await ComposeAsync(dir, Running(dir));

        Assert.Matches("^<plugin-[0-9]+>$", Text(Section(bundle, "logs")["slice"]![0]![property]));
    }

    /// <summary>
    /// A bundle is most wanted when the runtime does not answer, and then the plugins directory is the only place that
    /// says which packages are there: a package named only in the settings — a notification plugin's key, and the
    /// value the bundle carries for it — is written as its alias with the runtime stopped too.
    /// </summary>
    [Fact]
    public async Task With_the_runtime_stopped_a_package_named_only_in_the_settings_travels_as_its_alias()
    {
        using var dir = new TempPaths();
        const string Own = "sentinel-lynxnotify";
        Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, Own));
        File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, Own, "plugin.yaml"), $"id: {Own}");
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Notifications":{"Plugins":{"sentinel-lynxnotify":{"Enabled":true}}}}""");

        var bundle = await ComposeAsync(dir, new StubRuntime(dir.Paths.Root));

        AssertAbsent(bundle, Own);
        Assert.Equal(DoctorComposer.RuntimeDidNotAnswer, Text(Section(bundle, "routes")["not_read"]));
        var keys = Section(bundle, "config")["settings_file"]!["shape"]!["members"]!["Notifications"]!["members"]!["Plugins"]!["members"]!.AsObject();
        Assert.Equal(["<plugin-1>"], keys.Select(member => member.Key));
        Assert.Contains("Notifications:Plugins:<plugin-1>:Enabled", bundle.ValuesCarried);
    }

    /// <summary>
    /// A ref is a release's tag or somebody's branch. A fork's deployment at a release tag says nothing of anybody, so the
    /// tag travels beside the fork's mark; at a branch the ref is the mark too.
    /// </summary>
    [Fact]
    public async Task A_release_tag_travels_by_name_and_a_branch_is_a_mark()
    {
        using var dir = new TempPaths();
        var fork = "https://example.com/sentinel-fork/jason.git";
        var harness = Path.Combine(dir.Paths.Root, "no-harness-here");
        Directory.CreateDirectory(harness);
        File.WriteAllText(Path.Combine(harness, "SKILL.md"), "x");
        IReadOnlyList<Jason.Contracts.Skills.DeployedFile> files = [new("SKILL.md", "sha256:x")];
        var record = new Jason.Contracts.Skills.SkillsRecord(
            Jason.Contracts.Skills.SkillsRecord.CurrentVersion,
            [
                new(SkillPacks.Runtime, fork, "v1.2.3", true, "c0ffee", Now, files),
                new(SkillPacks.Business, fork, "sentinel-branch", true, "c0ffee", Now, files),
            ]);
        File.WriteAllText(Path.Combine(harness, Jason.Contracts.Skills.SkillsRecord.FileName), JsonSerializer.Serialize(record, JasonJson.Options));

        var bundle = await ComposeAsync(dir, Running(dir));

        AssertAbsent(bundle, "sentinel-fork", "sentinel-branch");
        var fact = Text(Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "harness_skills")!["fact"]);
        Assert.Contains($"{SkillPacks.Runtime} at v1.2.3", fact, StringComparison.Ordinal);
        Assert.Contains($"{SkillPacks.Business} at <skill-source>", fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder deployed where it stands has no ref, and its record says so in a sentence of Jason's own, which travels:
    /// a supporter tells a folder from a fork's branch by it. The folder itself is the mark.
    /// </summary>
    [Fact]
    public async Task A_folder_deployment_s_sentence_travels_in_place_of_a_ref()
    {
        using var dir = new TempPaths();
        var folder = Path.Combine(dir.Paths.Root, "sentinel-wombat-skills");
        var harness = Path.Combine(dir.Paths.Root, "no-harness-here");
        Directory.CreateDirectory(harness);
        File.WriteAllText(Path.Combine(harness, "SKILL.md"), "x");
        IReadOnlyList<Jason.Contracts.Skills.DeployedFile> files = [new("SKILL.md", "sha256:x")];
        var record = new Jason.Contracts.Skills.SkillsRecord(
            Jason.Contracts.Skills.SkillsRecord.CurrentVersion,
            [new(SkillPacks.Runtime, folder, SkillsSource.DirectoryRef, false, null, Now, files)]);
        File.WriteAllText(Path.Combine(harness, Jason.Contracts.Skills.SkillsRecord.FileName), JsonSerializer.Serialize(record, JasonJson.Options));

        var bundle = await ComposeAsync(dir, Running(dir));

        AssertAbsent(bundle, "sentinel-wombat");
        var fact = Text(Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "harness_skills")!["fact"]);
        Assert.Contains($"{SkillPacks.Runtime} at {SkillsSource.DirectoryRef}", fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fork's ref is found only where a fact quotes a deployment, beside the pack it was taken for: a branch called
    /// <c>main</c> does not rewrite every <c>main</c> in the bundle.
    /// </summary>
    [Fact]
    public async Task A_fork_s_ref_is_its_mark_only_where_a_deployment_is_quoted()
    {
        using var dir = new TempPaths();
        var fork = "https://example.com/sentinel-fork/jason.git";
        var harness = Path.Combine(dir.Paths.Root, "no-harness-here");
        Directory.CreateDirectory(harness);
        File.WriteAllText(Path.Combine(harness, "SKILL.md"), "x");
        IReadOnlyList<Jason.Contracts.Skills.DeployedFile> files = [new("SKILL.md", "sha256:x")];
        var record = new Jason.Contracts.Skills.SkillsRecord(
            Jason.Contracts.Skills.SkillsRecord.CurrentVersion,
            [new(SkillPacks.Runtime, fork, "main", true, "c0ffee", Now, files)]);
        File.WriteAllText(Path.Combine(harness, Jason.Contracts.Skills.SkillsRecord.FileName), JsonSerializer.Serialize(record, JasonJson.Options));
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(1), "Console at start: {Console}", null, ("Console", "main")));

        var bundle = await ComposeAsync(dir, Running(dir));

        var fact = Text(Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "harness_skills")!["fact"]);
        Assert.Contains($"{SkillPacks.Runtime} at <skill-source>", fact, StringComparison.Ordinal);
        Assert.Equal("main", Text(Section(bundle, "logs")["slice"]![0]!["Console"]));
    }

    /// <summary>
    /// Of the routes the runtime answers, only the plugin each route names is read for a name. A route's binding is the
    /// account's, and its keys are whatever the plugin's binding names: a key called <c>role</c> or <c>plugin_id</c> in it
    /// is no role and no plugin, so its value is never taken for a name to write as an alias, which would turn a word of
    /// the bundle's own, such as a check's state, into one. The plugin a route names is the person's, here one that only
    /// the routes name, and the route check that quotes it writes its alias.
    /// </summary>
    [Fact]
    public async Task A_route_s_plugin_is_an_alias_and_its_binding_s_keys_are_never_read_for_names()
    {
        using var dir = new TempPaths();
        var binding = new JsonObject { ["role"] = "ok", ["plugin_id"] = "ok" };
        var routes = new JsonObject
        {
            ["routing_snapshot_id"] = "rts_01JDOCTOR",
            ["plugin_snapshot_id"] = "snp_01JDOCTOR",
            ["global"] = new JsonObject
            {
                ["default"] = new JsonObject { ["plugin_id"] = "sentinelquokka", ["binding"] = binding, ["binding_identity"] = "sha256:ab" },
                ["operations"] = new JsonObject { ["campaign.get"] = new JsonObject { ["plugin_id"] = ShippedPlugins.Ids[0], ["binding"] = binding.DeepClone(), ["binding_identity"] = "sha256:ab" } },
            },
            ["campaigns"] = new JsonArray(),
        };
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation == Operations.RouteList ? Respond(HttpStatusCode.OK, routes.ToJsonString()) : null;

        var bundle = await ComposeAsync(dir, runtime);

        var checks = Section(bundle, "status")["checks"]!.AsArray();
        Assert.Matches("^Routed: default <plugin-[0-9]+>[.]$", Text(checks.Single(check => Text(check!["name"]) == "route")!["fact"]));
        Assert.Contains(checks, check => Text(check!["state"]) == "ok");
        AssertAbsent(bundle, "sentinelquokka", "<role-1>");
    }

    /// <summary>
    /// Of the routes the runtime answers, nothing but a route's own <c>plugin_id</c> is read for a name: a <c>role</c> or a
    /// <c>plugin_id</c> anywhere else in the answer, beside a section's routes or on a campaign's entry, names nothing the
    /// bundle writes as an alias, so a word of the bundle's own, such as a check's state, travels as it is.
    /// </summary>
    [Theory]
    [InlineData("global", "role")]
    [InlineData("global", "plugin_id")]
    [InlineData("campaign", "role")]
    [InlineData("campaign", "plugin_id")]
    public async Task Nothing_of_the_routes_but_a_route_s_plugin_is_read_for_a_name(string where, string key)
    {
        using var dir = new TempPaths();
        var global = new JsonObject
        {
            ["default"] = new JsonObject { ["plugin_id"] = ShippedPlugins.Ids[0], ["binding"] = new JsonObject(), ["binding_identity"] = "sha256:ab" },
            ["operations"] = new JsonObject(),
        };
        var campaign = new JsonObject { ["campaign_id"] = "cmp_01JDOCTOR", ["default"] = null, ["operations"] = new JsonObject() };
        (where == "global" ? global : campaign)[key] = "ok";
        var routes = new JsonObject
        {
            ["routing_snapshot_id"] = "rts_01JDOCTOR",
            ["plugin_snapshot_id"] = "snp_01JDOCTOR",
            ["global"] = global,
            ["campaigns"] = new JsonArray(campaign),
        };
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation == Operations.RouteList ? Respond(HttpStatusCode.OK, routes.ToJsonString()) : null;

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Contains(Section(bundle, "status")["checks"]!.AsArray(), check => Text(check!["state"]) == "ok");
        AssertAbsent(bundle, "<role-1>");
    }

    /// <summary>
    /// The folder the <c>jason</c> on the PATH is in, outside home, is a name somebody chose — <c>Q:\Tools\Acme</c> — and
    /// the path check quotes it: it is written as its mark, and the header says so.
    /// </summary>
    [Fact]
    public async Task The_executable_s_directory_outside_home_is_its_mark()
    {
        using var dir = new TempPaths();
        var tools = Path.Combine(dir.Paths.Root, "SENTINEL_PELICORP");
        Directory.CreateDirectory(tools);
        File.WriteAllText(Path.Combine(tools, OperatingSystem.IsWindows() ? "jason.exe" : "jason"), "x");

        var bundle = await ComposeAsync(dir, Running(dir), searchPath: tools);

        AssertAbsent(bundle, "SENTINEL_PELICORP");
        var path = Section(bundle, "status")["checks"]!.AsArray().Single(check => Text(check!["name"]) == "path")!;
        Assert.StartsWith("'jason' resolves to <install_dir>", Text(path["fact"]), StringComparison.Ordinal);
        Assert.Equal("<install_dir>", Text(Section(bundle, "bundle")["install_dir_written_as"]));
    }

    /// <summary>
    /// The list of the plugins this repository ships is read from the tree when the program is built: exactly the
    /// directories under <c>plugins/</c> that hold a manifest, so it cannot name a package the tree does not have or miss
    /// one it does.
    /// </summary>
    [Fact]
    public void The_shipped_plugins_are_the_packages_under_plugins_in_the_tree()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var tree = Directory.EnumerateDirectories(Path.Combine(root.FullName, "plugins"))
            .Where(directory => File.Exists(Path.Combine(directory, "plugin.yaml")))
            .Select(directory => Path.GetFileName(directory))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(tree);
        Assert.Equal(tree, ShippedPlugins.Ids);
    }

    /// <summary>
    /// A plugin or a role of the person's own whose name is one of the bundle's own words renames no section and changes
    /// no mark: the frame is written outside the rewrite, as it is for an account so named.
    /// </summary>
    [Fact]
    public async Task A_plugin_or_role_named_as_one_of_the_bundles_words_changes_none_of_its_frame()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, "status"));
        File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, "status", "plugin.yaml"), "id: status");
        var runtime = Running(dir);
        runtime.Roster = [("logs", false)];

        var bundle = await ComposeAsync(dir, runtime);

        Assert.Equal(["bundle", "versions", "status", "runtime", "routes", "config", "logs", "journal", "work_items"], Keys(bundle.Document));
        Assert.Equal(["versions", "status", "runtime", "routes", "config", "logs", "journal", "work_items"], bundle.Contents.Select(section => section.Name));
        Assert.Equal("<plugin-N>", Text(Section(bundle, "bundle")["plugin_id_written_as"]));
    }

    /// <summary>
    /// What follows the withheld problems travels only where it is, whole, the status verb's own sentences for the refused
    /// roles and the directory of the report it read. Text of any other shape after them — beside a sentence, before it,
    /// inside its name or its directory — leaves the summary alone, so a change that put the runtime's words into this fact
    /// turns these rows red rather than getting through on a sentence that happens to precede them.
    /// </summary>
    [Theory]
    [InlineData("", true)]
    [InlineData(" SENTINEL_RUNTIME_TEXT", false)]
    [InlineData(" SENTINEL_RUNTIME_TEXT{own}", false)]
    [InlineData("{own} SENTINEL_RUNTIME_TEXT", false)]
    [InlineData("{own}{other}", false)]
    [InlineData("{elsewhere}", false)]
    [InlineData("{own}", true)]
    public void Only_the_status_verb_s_own_sentences_follow_the_withheld_summary(string after, bool carried)
    {
        const string directory = "/srv/jason/skills/roles";
        var skills = new SkillsInfo(
            directory,
            4096,
            [
                new DeployedRoleSkill("researcher", 100, DoctorComposer.Withheld, Jason.Contracts.Skills.SkillProblemCodes.SkillTooLarge),
                new DeployedRoleSkill("scout", 100, DoctorComposer.Withheld, Jason.Contracts.Skills.SkillProblemCodes.SkillFileMissing),
            ],
            null);
        var tail = after
            .Replace("{own}", " " + Jason.Cli.Status.StatusChecks.OwnRoleRefused("scout", directory), StringComparison.Ordinal)
            .Replace("{other}", " " + Jason.Cli.Status.StatusChecks.OwnRoleRefused("SENTINEL_NAME", directory), StringComparison.Ordinal)
            .Replace("{elsewhere}", " " + Jason.Cli.Status.StatusChecks.OwnRoleRefused("scout", "/SENTINEL_DIRECTORY"), StringComparison.Ordinal);
        var fact = "withheld [skill_too_large] withheld [skill_file_missing]" + tail;

        var summary = DoctorComposer.WithheldSummary(fact, skills);

        const string run = "withheld (2: skill_too_large, skill_file_missing)";
        Assert.Equal(carried ? run + tail : run, summary);
        Assert.DoesNotContain("SENTINEL", summary, StringComparison.Ordinal);
    }

    /// <summary>With no report of the skills to read the sentences against, nothing after the withheld problems travels.</summary>
    [Fact]
    public void Without_the_report_s_skills_only_the_summary_travels() =>
        Assert.Equal(
            "withheld (1: skill_file_missing)",
            DoctorComposer.WithheldSummary("withheld [skill_file_missing] " + Jason.Cli.Status.StatusChecks.OwnRoleRefused("scout", "/srv/jason/skills/roles"), null));

    /// <summary>A data directory the runtime reports as no full path names nothing, and no word that spells it is rewritten.</summary>
    [Fact]
    public async Task A_runtime_data_directory_that_is_no_full_path_is_no_mark()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event(At(1), "run jason doctor; jason.exe started"));

        var bundle = await ComposeAsync(dir, Running(dir, dataDir: "jason"));

        Assert.False(Section(bundle, "runtime")["data_dir_agrees"]!.GetValue<bool>());
        Assert.Equal("run jason doctor; jason.exe started", Text(Section(bundle, "logs")["slice"]![0]!["@mt"]));
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    /// <summary>How long a hanging runtime keeps a request before the client gives up, as a test plays it.</summary>
    private static TimeSpan Timeout => TimeSpan.FromSeconds(2);

    private static Task<DoctorBundle> ComposeAsync(
        TempPaths dir,
        HttpMessageHandler runtime,
        bool includeJournal = false,
        IReadOnlyDictionary<string, string>? variables = null,
        FakeProgramRunner? programs = null,
        IdentityFacts? identity = null,
        string? build = null,
        string? searchPath = null) =>
        DoctorComposer.ComposeAsync(Machine(dir, runtime, variables, programs, identity, build, searchPath), includeJournal, new FixedClock(Now), Ct);

    /// <summary>The machine a bundle is composed on, and every way in which it is not the one running the suite.</summary>
    private static CliEnvironment Machine(
        TempPaths dir,
        HttpMessageHandler runtime,
        IReadOnlyDictionary<string, string>? variables = null,
        FakeProgramRunner? programs = null,
        IdentityFacts? identity = null,
        string? build = null,
        string? searchPath = null) =>
        new(
            new StringWriter(),
            new StringWriter(),
            dir.Paths,
            runtime,
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "no-harness-here")),
            Programs: programs ?? new FakeProgramRunner(),
            SearchPath: searchPath ?? Path.Combine(dir.Paths.Root, "nowhere"),
            Identity: identity ?? MadeUp,
            Variables: variables ?? new Dictionary<string, string>(),
            BuildVersion: build);

    /// <summary>A descriptor on disk and the runtime it names behind the handler.</summary>
    private static StubRuntime Running(TempPaths dir, string? dataDir = null)
    {
        dir.WriteDescriptor(Descriptor());
        return new StubRuntime(dataDir ?? dir.Paths.Root);
    }

    private static RuntimeDescriptor Descriptor(string baseUrl = BaseUrl) => new("v1", "0.3.0-dev", InstanceId, 4242, baseUrl, Token, Now.AddHours(-1));

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Envelope(HttpStatusCode status, string code) =>
        Respond(status, new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = "SENTINEL_RUNTIME_MESSAGE", ["retryable"] = true } }.ToJsonString());

    /// <summary>A journal entry made to say that the nth work item failed.</summary>
    private static JsonObject Failed(JsonObject entry, int n)
    {
        entry["kind"] = "workitem_failed";
        entry["work_item_id"] = StubRuntime.WorkItemId(n);
        return entry;
    }

    private static string At(int n) => Now.AddMinutes(-90).AddSeconds(n).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static JsonObject Section(DoctorBundle bundle, string name) => bundle.Document[name]!.AsObject();

    private static List<string> Keys(JsonNode node) => [.. node.AsObject().Select(member => member.Key)];

    private static string Text(JsonNode? node) => node!.GetValue<string>();

    private static List<string> Strings(JsonNode? node) => [.. node!.AsArray().Select(Text)];

    private static void AssertAbsent(DoctorBundle bundle, params string[] values)
    {
        var text = bundle.Serialize();
        var document = bundle.Document.ToJsonString();
        foreach (var value in values)
        {
            Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(value, document, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Every key and every string of a document.</summary>
    private static IEnumerable<string> Everything(JsonNode node)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var (key, value) in members)
                {
                    yield return key;
                    if (value is not null)
                    {
                        foreach (var text in Everything(value))
                        {
                            yield return text;
                        }
                    }
                }

                break;
            case JsonArray items:
                foreach (var item in items.OfType<JsonNode>())
                {
                    foreach (var text in Everything(item))
                    {
                        yield return text;
                    }
                }

                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                yield return value.GetValue<string>();
                break;
        }
    }

    /// <summary>
    /// Every place in a document that holds a value, a null or an empty container, by its path with each item's index, and
    /// what it holds there: a string as it is, anything else as JSON, a null as null.
    /// </summary>
    private static Dictionary<string, string?> Values(JsonNode? node, string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        switch (node)
        {
            case JsonObject members when members.Count > 0:
                foreach (var (key, value) in members)
                {
                    foreach (var leaf in Values(value, path.Length == 0 ? key : path + "." + key))
                    {
                        values[leaf.Key] = leaf.Value;
                    }
                }

                break;
            case JsonArray items when items.Count > 0:
                for (var at = 0; at < items.Count; at++)
                {
                    foreach (var leaf in Values(items[at], path + "[" + at.ToString(CultureInfo.InvariantCulture) + "]"))
                    {
                        values[leaf.Key] = leaf.Value;
                    }
                }

                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                values[path] = value.GetValue<string>();
                break;
            default:
                values[path] = node?.ToJsonString();
                break;
        }

        return values;
    }

    /// <summary>A path with every item's index written as <c>[]</c>, as the declared list writes it.</summary>
    private static string Indexless(string path) => System.Text.RegularExpressions.Regex.Replace(path, @"\[[0-9]+\]", "[]");

    /// <summary>The short form Windows keeps for an existing directory, asked of it here so the test does not use the code under test.</summary>
    private static string? ShortFormOf(string directory)
    {
        var buffer = new char[1024];
        var written = GetShortPathNameW(directory, buffer, (uint)buffer.Length);
        return written == 0 || written >= buffer.Length ? null : new string(buffer, 0, (int)written);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, [System.Runtime.InteropServices.Out] char[] shortPath, uint length);

    /// <summary>
    /// Each section's size is the size of its text in the file: found where the file has it, after its name, that many
    /// characters long, and the same document as the section.
    /// </summary>
    private static void AssertSizesAreTheFilesOwn(DoctorBundle bundle)
    {
        var text = bundle.Serialize();
        var from = 0;
        foreach (var section in bundle.Contents)
        {
            var name = $"\"{section.Name}\":";
            var at = text.IndexOf(name, from, StringComparison.Ordinal);
            Assert.True(at > 0, $"{section.Name} is not in the file.");
            var start = at + name.Length;
            Assert.True(text[start + (int)section.Bytes] is ',' or '}', $"{section.Name} does not end where its size says.");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(text.Substring(start, (int)section.Bytes)), bundle.Document[section.Name]), section.Name);
            Assert.Equal(section.Bytes, ((JsonArray)bundle.Document["bundle"]!["contents"]!).Single(entry => Text(entry!["name"]) == section.Name)!["bytes"]!.GetValue<long>());
            from = start + (int)section.Bytes;
        }
    }

    /// <summary>
    /// A log file of this many mebibytes of ordinary events, with lines of the test's own at its start and its end. One
    /// block of lines is made once and written again and again, so three hundred megabytes cost a disk's time and no more.
    /// </summary>
    private static void WriteLog(string path, int mebibytes, string[] first, string[] last)
    {
        var block = new StringBuilder();
        for (var n = 0; block.Length < 1024 * 1024; n++)
        {
            block.Append(Event(At(n % 60), "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms", null, ("RequestMethod", "POST"), ("RequestPath", "/v1/workitem.list"), ("StatusCode", "200"), ("Elapsed", "1.2345"))).Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(block.ToString());
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024);
        file.Write(Encoding.UTF8.GetBytes(string.Concat(first.Select(line => line + "\n"))));
        for (var written = 0L; written < mebibytes * 1024L * 1024L; written += bytes.Length)
        {
            file.Write(bytes);
        }

        file.Write(Encoding.UTF8.GetBytes(string.Concat(last.Select(line => line + "\n"))));
    }
}

/// <summary>
/// A body that starts and never finishes: a runtime that sent its headers and stalled. After the wait it plays the
/// client's timeout giving up while the body is read, on the clock a test gives it.
/// </summary>
internal sealed class StallingContent(TimeSpan wait, TimeProvider? clock = null) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await stream.WriteAsync("{\"snapshot\":"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await Task.Delay(wait, clock ?? TimeProvider.System, cancellationToken).ConfigureAwait(false);
        throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout elapsing.", new TimeoutException());
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

/// <summary>
/// A runtime that answers some operations and then holds every other request until the client would give up: what a
/// runtime looks like whose process is alive and stuck. It plays the client's own timeout, on the clock a test gives it.
/// </summary>
internal sealed class HangsAfter(StubRuntime inner, TimeProvider clock, TimeSpan timeout, params string[] answered) : DelegatingHandler(inner)
{
    public List<string> Seen { get; } = [];

    public List<string> Hung { get; } = [];

    /// <summary>Operations answered the first time they are asked and held every time after: a reader that comes later hangs.</summary>
    public IReadOnlyList<string> AnsweredOnce { get; init; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
        bool first;
        lock (Seen)
        {
            first = !Seen.Contains(operation);
            Seen.Add(operation);
        }

        if (answered.Contains(operation) || (first && AnsweredOnce.Contains(operation)))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        lock (Hung)
        {
            Hung.Add(operation);
        }

        await Task.Delay(timeout, clock, cancellationToken).ConfigureAwait(false);
        throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout elapsing.", new TimeoutException());
    }
}

/// <summary>
/// A clock on which every wait passes at once and is kept with its length: what a stand-in's play of the client's timeout
/// cost a bundle is read from here, and never from how busy the machine running the suite is.
/// </summary>
internal sealed class PassingClock : TimeProvider
{
    private readonly List<TimeSpan> _waits = [];

    /// <summary>Each wait begun on this clock, in turn, by its length.</summary>
    public IReadOnlyList<TimeSpan> Waits
    {
        get
        {
            lock (_waits)
            {
                return [.. _waits];
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_waits)
        {
            _waits.Add(dueTime);
        }

        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new Passed();
    }

    private sealed class Passed : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// A runtime that exists only in this handler. It answers every read a bundle makes — the status checks' and the
/// composer's own — records each request, and fails where a test says. Its journal and its work items are made up as
/// they are asked for, and carry a sentinel in every place a value would be.
/// </summary>
internal sealed class StubRuntime(string dataDir) : HttpMessageHandler
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The kinds of the journal's entries in turn: every fourth one says a work item failed.</summary>
    private static readonly string[] Kinds = ["campaign_context_set", "contact_added", "decision_made", "workitem_failed"];

    public List<(Uri Uri, string Operation, JsonObject Body, string? Authorization)> Requests { get; } = [];

    public string InstanceId { get; set; } = "rt_01JDOCTOR";

    /// <summary>How many entries the journal holds within any window, newest first.</summary>
    public int JournalTotal { get; set; } = 120;

    /// <summary>How many work items there are, oldest first.</summary>
    public int WorkItemTotal { get; set; } = 12;

    /// <summary>What <c>system.info</c> answers, where a test says; otherwise an ordinary answer.</summary>
    public SystemInfoResponse? Info { get; set; }

    /// <summary>The role a failed item an AI role worked is read with, by itself; one Jason ships unless a test says.</summary>
    public string RoleOf { get; set; } = "researcher";

    /// <summary>The roles <c>role.list</c> answers with, each with whether Jason ships it; none unless a test says.</summary>
    public IReadOnlyList<(string Name, bool Builtin)> Roster { get; set; } = [];

    /// <summary>What <c>plugin.list</c> answers, where a test says; otherwise two plugins.</summary>
    public PluginRegistryDto? Registry { get; set; }

    /// <summary>A response, or a throw, for the nth call of an operation; null answers it as usual.</summary>
    public Func<string, int, HttpResponseMessage?>? Fails { get; set; }

    public static WorkItemStatus StatusOf(int n) => (n % 8) switch
    {
        0 => WorkItemStatus.Failed,
        1 => WorkItemStatus.Succeeded,
        2 => WorkItemStatus.Expired,
        3 => WorkItemStatus.Created,
        4 => WorkItemStatus.Failed,
        5 => WorkItemStatus.Processing,
        6 => WorkItemStatus.Expired,
        _ => WorkItemStatus.Succeeded,
    };

    /// <summary>When a finished item finished: not in the order the items were made, so "newest" means the time.</summary>
    public static DateTimeOffset FinishedAt(int n) => Now.AddMinutes(-((n * 7_919) % 5_003));

    public static string WorkItemId(int n) => $"wi_{n:D26}";

    public static int Offset(JsonObject body) =>
        body["cursor"] is JsonValue cursor ? int.Parse(cursor.GetValue<string>()[1..], CultureInfo.InvariantCulture) : 0;

    public JsonObject Entry(int n) => JsonSerializer.SerializeToNode(
        new JournalEntryDto(
            $"jrn_{n:D26}",
            Now.AddMinutes(-n),
            new ActorRef(ActorType.Role, "SENTINEL_ACTOR_ID"),
            Kinds[n % Kinds.Length],
            "cmp_00000000000000000000000001",
            WorkItemId(n),
            $"att_{n:D26}",
            "SENTINEL_KEY",
            new JsonObject { ["note"] = "SENTINEL_OLD" },
            new JsonObject { ["context"] = "SENTINEL_CONTEXT", ["fields"] = new JsonObject { ["email"] = "SENTINEL_CONTACT_FIELD" }, ["decision"] = "SENTINEL_DECISION" },
            "SENTINEL_REASON",
            ContactId: "cnt_00000000000000000000000001",
            ContactName: "SENTINEL_CONTACT_NAME"),
        JasonJson.Options)!.AsObject();

    /// <summary>An answer of <c>system.info</c> with a value in every field, a sentinel in each one a bundle withholds.</summary>
    public static SystemInfoResponse EveryField(string dataDir) => new(
        "0.3.0-dev",
        "v1",
        "rt_01JDOCTOR",
        4242,
        Now.AddHours(-1),
        dataDir,
        new DatabaseInfo(["20260913225419_InitialCreate"], ["20260913225419_InitialCreate"], Path.Combine(dataDir, "state", "backups", "jason.db.bak")),
        new DispatcherInfo(DispatcherState.Running, 10, 4, 1, Now, 12, 3),
        new PluginsInfo(2, "snp_01JDOCTOR", Now.AddHours(-1), true),
        new RoutesInfo("rts_01JDOCTOR", Now.AddHours(-1), "fake-provider", 1, 2),
        new UpdateInfo(true, "0.4.0", Now, "https://SENTINEL_RELEASE_NOTES.example/notes"),
        new SkillsInfo(
            Path.Combine(dataDir, "skills", "roles"),
            4096,
            [
                new DeployedRoleSkill("researcher", 100, "SENTINEL_ROLE_PROBLEM: access denied", "SENTINEL_ROLE_CODE", "SENTINEL_ROLE_EXCEPTION"),
                new DeployedRoleSkill("planner", 100, null),
            ],
            "SENTINEL_SKILLS_PROBLEM: the directory could not be read",
            "SENTINEL_SKILLS_CODE",
            "SENTINEL_SKILLS_EXCEPTION"),
        new NotificationsInfo(
            true,
            [
                new NotificationChannelInfo("toast", "toast", true, "failed", "a reason", "toast_failed", "transient", "SENTINEL_CHANNEL_DETAIL", Now, "no_desktop", "SENTINEL_PRESENCE_DETAIL"),
                new NotificationChannelInfo("desktop", "toast", true, "not_attempted", "no_desktop", null, null, "SENTINEL_SECOND_DETAIL", Now, "no_desktop", "session 0"),
                new NotificationChannelInfo("third", "toast", true, "not_attempted", "no_desktop", null, null, "SENTINEL_THIRD_DETAIL", Now, "no_desktop", "Session 0"),
            ],
            "ntf_01JDOCTOR",
            Now));

    public static PluginRegistryDto Plugins(int count) => new(
        new SnapshotDto("snp_01JDOCTOR", Now, SnapshotSource.Startup, count),
        "rts_01JDOCTOR",
        [.. Enumerable.Range(0, count).Select(n => Plugin($"plugin-{n}", "sha256:1", PluginStatus.Valid, []))],
        null,
        Activated: true);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
        var text = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var body = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        int call;
        lock (Requests)
        {
            Requests.Add((request.RequestUri, operation, body, request.Headers.Authorization?.ToString()));
            call = Requests.Count(made => made.Operation == operation);
        }

        if (Fails?.Invoke(operation, call) is { } failure)
        {
            return failure;
        }

        return operation switch
        {
            Operations.SystemInfo => Answer(Info ?? SystemInfo()),
            Operations.RoleList => Answer(new Page<RoleDto>([.. Roster.Select(role => new RoleDto($"rol_{role.Name}", role.Name, role.Builtin, null, [], new JsonObject(), null, Now, Now))], null)),
            Operations.RouteList => Answer(new RoutesDto("rts_EMPTY", "snp_EMPTY", new RouteSetDto(null, new Dictionary<string, RouteDto>()), [])),
            Operations.PluginList => Answer(Registry ?? TwoPlugins()),
            Operations.JournalList => Answer(JournalPage(body)),
            Operations.WorkItemList => Answer(WorkItemPage(body)),
            Operations.WorkItemGet => Answer(WithRole(WorkItem(int.Parse(Text(body["work_item_id"])[3..], CultureInfo.InvariantCulture), WorkItemStatus.Failed))),
            _ => throw new HttpRequestException($"nothing answers {operation} here"),
        };
    }

    private static string Text(JsonNode? node) => node!.GetValue<string>();

    private static HttpResponseMessage Answer<T>(T value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, JasonJson.Options), Encoding.UTF8, "application/json") };

    private SystemInfoResponse SystemInfo() => new(
        "0.3.0-dev",
        "v1",
        InstanceId,
        4242,
        Now.AddHours(-1),
        dataDir,
        new DatabaseInfo(["20260913225419_InitialCreate"], [], null),
        new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0),
        new PluginsInfo(2, "snp_01JDOCTOR", Now.AddHours(-1), true),
        new RoutesInfo("rts_01JDOCTOR", Now.AddHours(-1), null, 0, 0));

    private static PluginRegistryDto TwoPlugins() => new(
        new SnapshotDto("snp_01JDOCTOR", Now, SnapshotSource.Startup, 2),
        "rts_01JDOCTOR",
        [
            Plugin("fake-provider", "sha256:3f2a", PluginStatus.Valid, []),
            Plugin("notifier", "sha256:9a8b", PluginStatus.Unavailable, [new PluginProblemDto("executable_missing", "capabilities.exec.executables[0]", "SENTINEL_PROBLEM_MESSAGE")]),
        ],
        null,
        Activated: true);

    internal static PluginDto Plugin(string id, string digest, PluginStatus status, IReadOnlyList<PluginProblemDto> problems) => new(
        id,
        "1.0.0",
        PluginKind.Provider,
        null,
        null,
        null,
        $"/plugins/{id}",
        digest,
        new PluginContractsDto([1], [1], []),
        [],
        new PluginEntry("main.js", "invoke"),
        new PluginCapabilitiesDto(null, null, null),
        new PluginLimitsDto(60000, 64),
        null,
        status,
        problems);

    private Page<JsonObject> JournalPage(JsonObject body)
    {
        var offset = Offset(body);
        var limit = body["limit"]?.GetValue<int>() ?? 100;
        var items = Enumerable.Range(offset, Math.Max(0, Math.Min(limit, JournalTotal - offset))).Select(Entry).ToList();
        return new Page<JsonObject>(items, offset + limit < JournalTotal ? $"c{offset + limit}" : null);
    }

    private Page<WorkItemDto> WorkItemPage(JsonObject body)
    {
        var offset = Offset(body);
        var limit = body["limit"]?.GetValue<int>() ?? 100;
        var wanted = body["status"] is JsonArray statuses
            ? statuses.Select(status => Enum.Parse<WorkItemStatus>(status!.GetValue<string>().Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true)).ToHashSet()
            : null;
        var matching = Enumerable.Range(0, WorkItemTotal).Where(n => wanted is null || wanted.Contains(StatusOf(n))).ToList();
        var items = matching.Skip(offset).Take(limit).Select(n => WorkItem(n)).ToList();
        return new Page<WorkItemDto>(items, offset + limit < matching.Count ? $"c{offset + limit}" : null);
    }

    /// <summary>The nth work item as <c>workitem.get</c> answers for it, in the status a test says it is in now.</summary>
    public static WorkItemDto Item(int n, WorkItemStatus status) => WorkItem(n, status);

    /// <summary>An item an AI role worked, with <see cref="RoleOf"/> as its role; any other item as it is.</summary>
    private WorkItemDto WithRole(WorkItemDto item) => item.Role is null ? item : item with { Role = RoleOf };

    private static WorkItemDto WorkItem(int n, WorkItemStatus? asked = null)
    {
        var status = asked ?? StatusOf(n);
        var provider = n / 8 % 2 == 0;
        var finished = status is WorkItemStatus.Failed or WorkItemStatus.Expired or WorkItemStatus.Succeeded ? FinishedAt(n) : (DateTimeOffset?)null;
        var error = status switch
        {
            WorkItemStatus.Failed => new AttemptErrorDto("provider_refused", "SENTINEL_ERROR_MESSAGE for cnt_00000000000000000000000001", false, Class: FailureClass.Permanent),
            WorkItemStatus.Expired => new AttemptErrorDto("attempt_timed_out", "SENTINEL_ERROR_MESSAGE after 30 s", true),
            _ => null,
        };
        return new WorkItemDto(
            WorkItemId(n),
            "cmp_00000000000000000000000001",
            "cnt_00000000000000000000000001",
            provider ? WorkItemKind.ProviderOp : WorkItemKind.AiRole,
            provider ? null : "researcher",
            provider ? "contact.add" : null,
            null,
            status,
            false,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            new ActorRef(ActorType.Attempt, "SENTINEL_CREATOR"),
            new JsonObject { ["secret"] = "SENTINEL_WI_CONTEXT" },
            null,
            new JsonObject { ["answer"] = "SENTINEL_WI_RESULT" },
            2,
            null,
            error,
            null,
            null,
            Now.AddDays(-1).AddMinutes(n),
            Now.AddDays(-1).AddMinutes(n),
            finished);
    }
}
