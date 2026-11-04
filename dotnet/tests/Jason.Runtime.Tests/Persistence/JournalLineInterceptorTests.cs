using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Hosting;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A journal line of a kind the runtime reserves is held to its document under <c>docs/contracts/journal/</c> as it is
/// written: refused under the strict mode every test runs in, written and logged by its kind in a running installation.
/// </summary>
public sealed class JournalLineInterceptorTests
{
    private const string Sentinel = "SENTINEL_BODY_VALUE";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_line_that_matches_its_document_is_written()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        db.Journal.Add(Line("suppression_added", "email", null, new JsonObject { ["channel"] = "email", ["value"] = "pat@example.test" }));
        db.SaveChanges();

        Assert.Equal(1, db.Journal.Count());
    }

    [Fact]
    public void Strict_refuses_a_body_off_its_document_naming_the_kind_and_the_place_and_never_the_value()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        db.Journal.Add(Line("suppression_added", "email", null, new JsonObject { ["channel"] = "email", ["typed"] = Sentinel }));
        var refused = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        Assert.Contains("`suppression_added`", refused.Message, StringComparison.Ordinal);
        Assert.Contains("/new", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, refused.ToString(), StringComparison.Ordinal);

        db.ChangeTracker.Clear();
        Assert.Equal(0, db.Journal.Count());
    }

    [Fact]
    public void Strict_refuses_a_line_without_a_column_its_document_says_every_line_names()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        db.Journal.Add(Line("campaign_paused", "status", JsonValue.Create("active"), JsonValue.Create("paused")));
        var refused = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        Assert.Contains("`campaign_paused`", refused.Message, StringComparison.Ordinal);
        Assert.Contains("/campaign_id", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_named_through_its_navigation_counts_as_named()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var now = DateTime.UtcNow;
        var campaign = new Campaign { PublicId = "cmp_01JC0000000000000000000001", Name = "spring", ProjectId = DefaultProject.Id, CreatedAt = now, UpdatedAt = now };

        db.Journal.Add(Line("campaign_paused", "status", JsonValue.Create("active"), JsonValue.Create("paused"), campaign));
        db.SaveChanges();

        Assert.Equal(1, db.Journal.Count());
    }

    [Fact]
    public void A_kind_of_somebody_elses_own_is_not_held_to_any_document()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        db.Journal.Add(Line("agent_note", Sentinel, new JsonArray(1, 2), JsonValue.Create(true)));
        db.SaveChanges();

        Assert.Equal(1, db.Journal.Count());
    }

    [Fact]
    public void Logged_writes_the_line_and_one_warning_that_names_its_kind_and_nothing_else()
    {
        using var database = new TestDatabase();
        var logs = new RecordingLogs();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var options = new DbContextOptionsBuilder<JasonDbContext>();
        JasonDbContext.Configure(options, database.File, JournalLines.Logged(factory.CreateLogger<JournalLineInterceptor>()));
        using var db = new JasonDbContext(options.Options);

        db.Journal.Add(Line("suppression_added", Sentinel, null, new JsonObject { ["typed"] = Sentinel }));
        db.SaveChanges();

        Assert.Equal(1, db.Journal.Count());
        var line = Assert.Single(logs.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("A journal line of kind suppression_added was written off its document", line.Message);
        Assert.False(logs.Mentions(Sentinel), "a value of the line reached the log");
    }

    /// <summary>
    /// What an installation runs with: options nobody set the switch on log a line off its document and commit it, and
    /// the options every test's runtime starts with refuse it. The switch is internal to the runtime and its tests, so no
    /// mode that starts an installation's runtime can set it: what the installation runs with is its default.
    /// </summary>
    [Fact]
    public async Task A_runtime_with_default_options_logs_and_commits_and_the_tests_runtime_refuses()
    {
        var installation = TestRuntimeOptions.Quiet with { JournalLinesStrict = default };
        Assert.True(TestRuntimeOptions.Quiet.JournalLinesStrict);

        using (var dir = new TempDataDir())
        {
            var runtime = await StartAsync(dir, installation);
            await using (runtime)
            {
                using (var scope = runtime.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
                    db.Journal.Add(Line("suppression_added", Sentinel, null, new JsonObject { ["typed"] = Sentinel }));
                    await db.SaveChangesAsync(Ct);
                    Assert.Equal(1, await db.Journal.CountAsync(entry => entry.Kind == "suppression_added", Ct));
                }

                await runtime.StopAsync();
            }

            var log = string.Concat(Directory.GetFiles(dir.Paths.LogsDirectory).Select(File.ReadAllText));
            var warned = log
                .Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@l"] == "Warning" && line["Kind"] is not null)
                .Select(line => (string?)line["Kind"])
                .ToList();
            Assert.Equal(["suppression_added"], warned);
            Assert.DoesNotContain(Sentinel, log, StringComparison.Ordinal);
        }

        using var strictDir = new TempDataDir();
        await using var strict = await StartAsync(strictDir, TestRuntimeOptions.Quiet);
        using var strictScope = strict.Services.CreateScope();
        var strictDb = strictScope.ServiceProvider.GetRequiredService<JasonDbContext>();
        strictDb.Journal.Add(Line("suppression_added", Sentinel, null, new JsonObject { ["typed"] = Sentinel }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => strictDb.SaveChangesAsync(Ct));
    }

    private static async Task<RunningRuntime> StartAsync(TempDataDir dir, RuntimeHostOptions options)
    {
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        return await RuntimeHost.StartAsync(dir.Paths, options, Ct);
    }

    private static JournalEntry Line(string kind, string? key, JsonNode? old, JsonNode? updated, Campaign? campaign = null) => new()
    {
        Campaign = campaign,
        PublicId = "jrn_" + Guid.NewGuid().ToString("N"),
        Ts = DateTime.UtcNow,
        ActorType = ActorType.System,
        ActorId = "runtime",
        Kind = kind,
        Key = key,
        Old = old,
        New = updated,
    };
}
