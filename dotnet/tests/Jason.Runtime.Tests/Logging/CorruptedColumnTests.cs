using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Logging;

/// <summary>
/// A stored value this runtime can no longer read — a JSON column edited by hand with the database's checks turned
/// off, an object whose key is written twice, which those checks accept, a name no enum has — fails whatever reads it,
/// and what the reader says about it quotes it: for a value that starts like a literal, everything from there to the
/// end of the column; for a key written twice, the key. That is a person's text. The failure is refused where the
/// value is read, in words that name the column and the kind of failure and nothing else, so every place that lives
/// through it — the scan, the notifier, the load at startup — and the start that cannot, says only that. EF Core's own
/// event for the failed query would write it whole, and is not written. Read from the file the real sink wrote, at the
/// loudest level the settings accept.
/// </summary>
public sealed class CorruptedColumnTests
{
    /// <summary>A dispatcher that scans every second, so a scan that fails is followed by another the test can wait for.</summary>
    private const string Settings = """
        {"Logging":{"MinimumLevel":"Verbose"},
         "Dispatcher":{"TickSeconds":1,"DrainSeconds":1,"RetryDelaySeconds":0},
         "Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    /// <summary>
    /// Where the reader stops in <see cref="Unreadable"/>: it takes the <c>n</c> for the start of <c>null</c> and stops on
    /// the byte after it, the twelfth of the first line as an editor counts.
    /// </summary>
    private const string Position = "JsonException at line 1, byte 12";

    private const string ScanFailed = "Dispatcher scan failed; the loop continues: {Failure}";

    private const string NotifierFailed = "The notifier failed; the scan's work was already handed out: {Failure}";

    private const string StartupFailed = "The plugin load at startup failed; the runtime continues with no plugins active: {Failure}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stored_column_that_no_longer_parses_is_logged_by_where_the_reader_stopped_and_not_by_what_it_held()
    {
        var mark = Guid.NewGuid().ToString("N");
        var stored = Unreadable(mark);

        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, Settings);

                // Before the runtime starts, so its first reads are of these rows: the route the load at startup reads,
                // and the line the notifier resumes from.
                SeedRouteAndNotificationLine(paths, stored);
            },
            configureServices: services =>
            {
                // The fixture turns notifications off after binding, and the notifier is one of the readers here: they
                // are turned on again, over a queue that keeps what it is handed, so nothing is shown to anybody.
                services.PostConfigure<NotificationsOptions>(notifications => notifications.Enabled = true);
                services.AddSingleton<INotificationQueue>(new DispatchHarness.RecordingQueue());
            });

        // The load at startup ran before the fixture was handed back, and the first scan ends with the notifier.
        Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));

        // A reload reads the route the load at startup read.
        var (reload, _) = await api.PostAsync(Operations.PluginReload, null, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, reload);

        // And a claim reads the campaign of the item it claims.
        SeedClaimable(api.Paths, (campaign, _) => $"UPDATE campaigns SET context_json = {stored} WHERE public_id = {campaign}");
        Assert.True(await LoggedAsync(api.Paths, "Dispatcher scan failed"), "The dispatcher never logged a failed scan for the campaign it could not read.");

        // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
        await api.Runtime.StopAsync();
        var events = Events(api.Paths);

        // Every line, the framework's included: EF Core's event for a failed query carries the exception it was handed,
        // whole, and says nothing the runtime's own line does not.
        AssertNoMark(api.Paths, events, mark);
        Assert.DoesNotContain(events, line => (string?)line["EventId"]?["Name"] == CoreEventId.QueryIterationFailed.Name);
        AssertSaidBy(events, ScanFailed, Unreadable("campaigns.context_json", Position));
        AssertSaidBy(events, NotifierFailed, Unreadable("journal.new_json", Position));
        AssertSaidBy(events, StartupFailed, Unreadable("campaign_routes.binding_json", Position));
    }

    /// <summary>
    /// An object whose key is written twice passes the database's own check, and the reader that used to find it did so
    /// only when somebody first looked inside — by name, in an error that is not a read failure at all. It is refused
    /// when the column is read, by the column.
    /// </summary>
    [Fact]
    public async Task A_stored_object_with_a_key_written_twice_is_refused_by_its_column_and_not_by_the_key()
    {
        var mark = Guid.NewGuid().ToString("N");
        var key = "SENTINEL_STORED_" + mark;
        var stored = "{\"" + key + "\": 1, \"" + key + "\": 2}";

        var (api, events) = await ScanUntilItFailsAsync(
            (campaign, _) => $"UPDATE campaigns SET context_json = {stored} WHERE public_id = {campaign}");
        await using (api)
        {
            AssertNoMark(api.Paths, events, mark);
            AssertSaidBy(events, ScanFailed, Unreadable("campaigns.context_json", "JsonException"));
        }
    }

    /// <summary>A name no enum has is refused by its column and is not repeated: whoever edited it could have typed anything.</summary>
    [Fact]
    public async Task A_stored_name_no_enum_has_is_refused_by_its_column_and_not_repeated()
    {
        var mark = Guid.NewGuid().ToString("N");
        var stored = "SENTINEL_STORED_" + mark;

        var (api, events) = await ScanUntilItFailsAsync(
            (_, item) => $"UPDATE work_items SET created_by_type = {stored} WHERE public_id = {item}");
        await using (api)
        {
            AssertNoMark(api.Paths, events, mark);
            AssertSaidBy(events, ScanFailed, Unreadable("work_items.created_by_type", "not a stored ActorType"));
        }
    }

    /// <summary>
    /// A timestamp is read by the database provider itself, not by a converter of the runtime's, so a hand-edited one
    /// fails with the provider's own parse error, whose message quotes the text. It is said as the read failure it is, by
    /// its kind, and the text is not repeated.
    /// </summary>
    [Fact]
    public async Task A_stored_timestamp_that_no_longer_parses_is_said_by_the_kind_of_failure_and_not_repeated()
    {
        var mark = Guid.NewGuid().ToString("N");
        var stored = "SENTINEL_STORED_" + mark;

        var (api, events) = await ScanUntilItFailsAsync(
            (_, item) => $"UPDATE work_items SET created_at = {stored} WHERE public_id = {item}");
        await using (api)
        {
            AssertNoMark(api.Paths, events, mark);
            AssertSaidBy(events, ScanFailed, "FormatException");
        }
    }

    /// <summary>
    /// A start that reads an unreadable column cannot go on — the dispatcher's recovery of what the last run left behind
    /// is the first thing it does — and the host says so in its own line, with the exception beside it. What that line
    /// and the refusal carry is the refusal's own sentence: the column, that its value cannot be read, and what to do.
    /// The same for valid JSON of the wrong shape, which the database's check accepts and which fails in the reader with
    /// something other than the parser's exception: a start survives it as a refusal, not as an exception nothing caught.
    /// </summary>
    [Theory]
    [Critical(Family.B3, "a corrupt column is named, not quoted")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_start_that_finds_an_unreadable_column_refuses_in_words_a_person_can_act_on_and_quotes_none_of_it(bool wrongShape)
    {
        var mark = Guid.NewGuid().ToString("N");
        var stored = wrongShape ? "[]" : Unreadable(mark);
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        await File.WriteAllTextAsync(dir.Paths.UserSettingsFile, Settings, Ct);
        Migrate(dir.Paths);

        // An item the last run had claimed and not started: the recovery at start reads it, context and all.
        using (var db = Open(dir.Paths))
        {
            var campaign = WorkItemFactory.NewCampaign(now: DateTime.UtcNow);
            var item = WorkItemFactory.NewAiRole(campaign, now: DateTime.UtcNow, configure: w => w.Status = WorkItemStatus.Scheduled);
            db.Campaigns.Add(campaign);
            db.WorkItems.Add(item);
            Corrupt(db, () => db.SaveChanges(), $"UPDATE work_items SET context_json = {stored} WHERE public_id = {item.PublicId}");
        }

        using var refusals = new StringWriter();
        var exit = 0;
        var escaped = await Record.ExceptionAsync(async () => exit = await RuntimeHost.RunAsync(dir.Paths, TestRuntimeOptions.Quiet with { Refusals = refusals }, Ct));

        // The logger is disposed by the start that failed, so what is on disk now is everything there is.
        var events = Events(dir.Paths);
        AssertNoMark(dir.Paths, events, mark);
        Assert.Null(escaped);
        Assert.Equal(1, exit);

        var said = refusals.ToString();
        Assert.Contains("work_items.context_json", said, StringComparison.Ordinal);
        Assert.Contains("cannot be read", said, StringComparison.Ordinal);
        Assert.DoesNotContain(mark, said, StringComparison.Ordinal);

        // And the host's own line is there, carrying that sentence: otherwise the absence of a leak would only be the
        // absence of the line.
        Assert.Contains(events, line => line["@x"]?.GetValue<string>().Contains("work_items.context_json", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// However the host hands back the failure that stopped its start — as itself, or inside another, as when more than
    /// one of its services failed — an unreadable stored value inside it is the refusal, said in its own sentence.
    /// </summary>
    [Fact]
    public async Task A_start_stopped_by_an_unreadable_stored_value_inside_another_failure_still_refuses_in_its_sentence()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        await File.WriteAllTextAsync(dir.Paths.UserSettingsFile, Settings, Ct);
        var stopped = new AggregateException(new InvalidOperationException("another service failed"), UnreadableStoredValueException.From("work_items.context_json", new System.Text.Json.JsonException("SENTINEL_WRAPPED")));
        using var refusals = new StringWriter();
        var options = TestRuntimeOptions.Quiet with
        {
            Refusals = refusals,
            ConfigureServices = services =>
            {
                TestRuntimeOptions.TurnTheOutsideOff(services);
                services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(new FailsToStart(stopped));
            },
        };

        var exit = 0;
        var escaped = await Record.ExceptionAsync(async () => exit = await RuntimeHost.RunAsync(dir.Paths, options, Ct));

        Assert.Null(escaped);
        Assert.Equal(1, exit);
        Assert.Contains("work_items.context_json", refusals.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL_WRAPPED", refusals.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A read that meets a stored value this runtime cannot read is the runtime's failure, said by the column and never by
    /// the value, and is not worth sending again: every read of that row fails the same way until somebody repairs it.
    /// </summary>
    [Fact]
    public async Task A_read_that_meets_an_unreadable_stored_value_names_its_column_and_is_not_retryable()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths => File.WriteAllText(paths.UserSettingsFile, Settings));
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Unreadable" }, Ct);
        var created = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign.Id, kind = "ai_role", role = "researcher", context = new { brief = "x" } },
            Ct);
        using (var db = Open(api.Paths))
        {
            Corrupt(db, $"UPDATE work_items SET context_json = {"[]"} WHERE public_id = {created.Id}");
        }

        var (status, body) = await api.PostAsync(Operations.WorkItemGet, new { work_item_id = created.Id }, Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        var error = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(body, Jason.Contracts.Json.JasonJson.Options)!.Error;
        Assert.Equal("internal_error", error.Code);
        Assert.False(error.Retryable);
        Assert.Contains("work_items.context_json", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A hosted service that fails its start with the failure it is given.</summary>
    private sealed class FailsToStart(Exception failure) : Microsoft.Extensions.Hosting.IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.FromException(failure);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A value that starts like <c>null</c> and is not: the parser quotes it from the <c>n</c> to the end.</summary>
    private static string Unreadable(string mark) => "{\"brief\": nSENTINEL_STORED_" + mark + "}";

    /// <summary>How a line says that a column's value could not be read: the column, and the kind of failure.</summary>
    private static string Unreadable(string column, string failure) => $"the value stored in {column} cannot be read ({failure})";

    /// <summary>
    /// A runtime whose dispatcher meets, at its next claim, an item seeded with the statement's damage done to it, run
    /// until it has said that a scan failed and then stopped. The fixture is handed back, so its data directory stays
    /// until the test has read it.
    /// </summary>
    private static async Task<(RuntimeApiFixture Api, List<JsonObject> Events)> ScanUntilItFailsAsync(Func<string, string, FormattableString> corrupt)
    {
        var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths => File.WriteAllText(paths.UserSettingsFile, Settings));
        try
        {
            Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));
            SeedClaimable(api.Paths, corrupt);
            Assert.True(await LoggedAsync(api.Paths, "Dispatcher scan failed"), "The dispatcher never logged a failed scan for the item it could not read.");
            await api.Runtime.StopAsync();
            return (api, Events(api.Paths));
        }
        catch
        {
            await api.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The database as the runtime will find it: migrated by its own migrator, with a campaign route and a
    /// notification line whose JSON no longer parses.
    /// </summary>
    private static void SeedRouteAndNotificationLine(JasonPaths paths, string stored)
    {
        Migrate(paths);

        using var db = Open(paths);
        var campaign = WorkItemFactory.NewCampaign(status: CampaignStatus.Draft, now: DateTime.UtcNow);
        var route = new CampaignRoute
        {
            Campaign = campaign,
            PluginId = "fake-provider",
            Binding = new JsonObject { ["workspace"] = "w" },
            UpdatedAt = DateTime.UtcNow,
        };
        db.Campaigns.Add(campaign);
        db.CampaignRoutes.Add(route);
        var line = new JournalWriter(TimeProvider.System).Append(db, Actors.Runtime, JournalKinds.NotificationSent, campaign: null, key: "notification", updated: new NotificationLine("ntf_earlier", 1, 0, null, [], null).ToJson());
        db.SaveChanges();

        // The journal takes no update, so the line the notifier reads is a copy of the one just written, with its value
        // replaced: everything else is as the runtime itself wrote it.
        Corrupt(
            db,
            $"UPDATE campaign_routes SET binding_json = {stored} WHERE id = {route.Id}",
            $"INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, new_json) SELECT {PublicId.New("jrn")}, ts, actor_type, actor_id, kind, {stored} FROM journal WHERE id = {line.Id}");
    }

    /// <summary>
    /// An active campaign holding one claimable item, damaged by the statement, which is handed the campaign's and the
    /// item's public ids. Written in one transaction, so the running dispatcher never sees the item before the damage.
    /// </summary>
    private static void SeedClaimable(JasonPaths paths, Func<string, string, FormattableString> corrupt)
    {
        using var db = Open(paths);
        var campaign = WorkItemFactory.NewCampaign(now: DateTime.UtcNow);
        var item = WorkItemFactory.NewAiRole(campaign, now: DateTime.UtcNow);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        Corrupt(db, () => db.SaveChanges(), corrupt(campaign.PublicId, item.PublicId));
    }

    private static void Migrate(JasonPaths paths)
    {
        Directory.CreateDirectory(paths.StateDirectory);
        new DatabaseMigrator(paths).Migrate();
    }

    private static void Corrupt(JasonDbContext db, params FormattableString[] statements) => Corrupt(db, () => { }, statements);

    /// <summary>
    /// Writes what the columns' own checks refuse, the way an edit made with those checks turned off would. The pragma
    /// belongs to the connection, so it is turned on and off again on the one connection the statements use, and
    /// everything happens in one transaction.
    /// </summary>
    private static void Corrupt(JasonDbContext db, Action before, params FormattableString[] statements)
    {
        db.Database.OpenConnection();
        try
        {
            db.Database.ExecuteSqlRaw("PRAGMA ignore_check_constraints = ON;");
            using (var transaction = db.Database.BeginTransaction())
            {
                before();
                foreach (var statement in statements)
                {
                    Assert.Equal(1, db.Database.ExecuteSql(statement));
                }

                transaction.Commit();
            }

            db.Database.ExecuteSqlRaw("PRAGMA ignore_check_constraints = OFF;");
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    private static JasonDbContext Open(JasonPaths paths) => new(JasonDbContext.CreateOptions(paths.DatabaseFile));

    /// <summary>Waits for a line the runtime is about to write; read as the sink holds the file, open for writing.</summary>
    private static async Task<bool> LoggedAsync(JasonPaths paths, string fragment)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(paths.LogsDirectory)
                && Directory.GetFiles(paths.LogsDirectory, "*", SearchOption.AllDirectories).Any(file => Read(file).Contains(fragment, StringComparison.Ordinal)))
            {
                return true;
            }

            await Task.Delay(100, Ct);
        }

        return false;
    }

    private static List<JsonObject> Events(JasonPaths paths) =>
        [.. Directory.GetFiles(paths.LogsDirectory, "*", SearchOption.AllDirectories)
            .SelectMany(file => Read(file).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(line => JsonNode.Parse(line)!.AsObject())];

    private static string Read(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The template was logged, and every line of it says this failure and carries no exception.</summary>
    private static void AssertSaidBy(List<JsonObject> events, string template, string failure)
    {
        var lines = events.Where(line => (string?)line["@mt"] == template).ToList();
        Assert.True(lines.Count > 0, $"The runtime never logged \"{template}\", so the absence of a leak there proves nothing.");
        Assert.All(lines, line =>
        {
            Assert.Equal(failure, (string?)line["Failure"]);
            Assert.False(line.ContainsKey("@x"), $"\"{template}\" carries an exception.");
        });
    }

    /// <summary>
    /// The mark is nowhere in any file under <c>logs/</c>: the files are read as text, so a value inside an exception or
    /// a nested property is found as surely as one in a property of its own. Where a line carries it, the line and the
    /// property are named.
    /// </summary>
    private static void AssertNoMark(JasonPaths paths, List<JsonObject> events, string mark)
    {
        if (!Directory.GetFiles(paths.LogsDirectory, "*", SearchOption.AllDirectories).Any(file => Read(file).Contains(mark, StringComparison.Ordinal)))
        {
            return;
        }

        var carriers = events
            .SelectMany(line => line
                .Where(property => property.Value?.ToJsonString().Contains(mark, StringComparison.Ordinal) == true)
                .Select(property => $"{property.Key} on \"{(string?)line["@mt"]}\" ({(string?)line["SourceContext"]})"))
            .Distinct(StringComparer.Ordinal);
        Assert.Fail($"What the column held reached the log, in:{Environment.NewLine}{string.Join(Environment.NewLine, carriers)}");
    }
}
