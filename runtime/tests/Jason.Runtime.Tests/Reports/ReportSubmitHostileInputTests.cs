using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Reports;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Reports;

/// <summary>
/// What a submission may not be. Admission is the one door an outside actor writes through, so every refusal
/// here is decided before a row exists, names the field it is about, and never repeats the value it was given.
/// </summary>
public class ReportSubmitHostileInputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Provenance that names nobody is not provenance.</summary>
    [Fact]
    public async Task A_report_from_nobody_in_particular_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var anonymous = ReportedWorld.Submission();
        anonymous.Remove("actor");
        var unnamed = ReportedWorld.Submission();
        unnamed["actor"] = new JsonObject { ["type"] = "human" };
        var blank = ReportedWorld.Submission();
        blank["actor"] = new JsonObject { ["type"] = "human", ["id"] = "   " };

        foreach (var submission in (JsonObject[])[anonymous, unnamed, blank])
        {
            var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));
            Assert.Equal("actor_required", refused.Code);
        }

        Assert.Equal(0, await db.Reports.CountAsync(Ct));
    }

    /// <summary>
    /// The same rule, refused a step earlier and in the words every verb uses. A role or an attempt that carries
    /// no id never reaches the report path: <c>Actors.Resolve</c> refuses it for the whole API, and a report
    /// inventing a third spelling of "name yourself" would be a second place to keep in step. Only the case that
    /// resolver allows — an anonymous human, which is the right default everywhere else — is this path's own.
    /// </summary>
    [Theory]
    [InlineData("role")]
    [InlineData("attempt")]
    public async Task A_role_or_an_attempt_that_names_nobody_is_refused_where_every_verb_refuses_it(string type)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission["actor"] = new JsonObject { ["type"] = type };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal("actor.id", Assert.Single(refused.Details!).Field);
        Assert.Equal("required", refused.Details![0].Code);
        Assert.Equal(0, await db.Reports.CountAsync(Ct));
    }

    /// <summary>The runtime performs effects; it does not report them.</summary>
    [Fact]
    public async Task A_reporter_may_not_claim_to_be_the_runtime()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission["actor"] = new JsonObject { ["type"] = "system", ["id"] = "runtime" };

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal("reporter_reserved", refused.Code);
        Assert.Equal(0, await db.Reports.CountAsync(Ct));
    }

    /// <summary>
    /// A human and a role are taken at their word — the runtime holds one token and no caller identity — but an
    /// attempt is a row in this database, so a claim to be one is either true or a mistake worth naming.
    /// </summary>
    [Fact]
    public async Task An_attempt_that_does_not_exist_cannot_be_a_reporter()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission["actor"] = new JsonObject { ["type"] = "attempt", ["id"] = "att_01K52JR0000000000000000001" };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal("actor.id", Assert.Single(refused.Details!).Field);
        Assert.Equal("unknown", refused.Details![0].Code);
        Assert.Equal(0, await db.Reports.CountAsync(Ct));
    }

    /// <summary>
    /// The receipt is the runtime's half of the row. A submission that tries to write it is told so: stripping
    /// it quietly would answer with a document nobody sent.
    /// </summary>
    [Theory]
    [InlineData("received_at")]
    [InlineData("id")]
    [InlineData("assertion_hash")]
    [InlineData("correlation")]
    public async Task A_submission_may_not_write_the_runtimes_half_of_the_row(string field)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission[field] = "2000-01-01T00:00:00Z";

        var refused = await Assert.ThrowsAsync<ValidationException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal(field, Assert.Single(refused.Details!).Field);
        Assert.Equal("field_reserved", refused.Details![0].Code);
        Assert.Equal(0, await db.Reports.CountAsync(Ct));
    }

    [Fact]
    public async Task A_field_this_api_does_not_know_is_refused_rather_than_ignored()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission["recipient_email"] = "someone@example.test";

        var refused = await Assert.ThrowsAsync<ValidationException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal("recipient_email", Assert.Single(refused.Details!).Field);
        Assert.Equal("field_unknown", refused.Details![0].Code);
    }

    /// <summary>A marker nobody can interpret is worse than no marker, and a typo is exactly that.</summary>
    [Fact]
    public async Task Unknown_fields_must_name_fields_that_exist()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission["unknown_fields"] = new JsonArray("occured_at");

        var refused = await Assert.ThrowsAsync<ValidationException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal("unknown_fields[0]", Assert.Single(refused.Details!).Field);
        Assert.Equal("unknown_field_unknown", refused.Details![0].Code);
    }

    [Fact]
    public async Task Everything_wrong_with_one_request_is_said_at_once()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = new JsonObject { ["actor"] = ReportedWorld.Actor(new ActorRef(ActorType.Human, "person-1")) };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal(
            ["effect", "summary", "tool"],
            refused.Details!.Select(detail => detail.Field).OrderBy(field => field, StringComparer.Ordinal));
        Assert.All(refused.Details!, detail => Assert.Equal("required", detail.Code));
    }

    [Theory]
    [InlineData("summary", ReportService.MaxProseLength, "too_long")]
    [InlineData("uncertainty", ReportService.MaxProseLength, "too_long")]
    [InlineData("effect", ReportService.MaxTextLength, "too_long")]
    [InlineData("idempotency_key", ReportService.MaxTextLength, "too_long")]
    public async Task A_field_larger_than_its_cap_is_refused_by_name(string field, int cap, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission[field] = new string('x', cap + 1);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Contains(refused.Details!, detail => detail.Field == field && detail.Code == code);
        Assert.DoesNotContain(new string('x', cap + 1), refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.Reports.CountAsync(Ct));
    }

    /// <summary>Evidence is a note about what happened, not the thing itself.</summary>
    [Fact]
    public async Task Evidence_larger_than_a_note_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission["evidence"] = new JsonObject { ["body"] = new string('x', ReportService.MaxEvidenceBytes + 1) };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal("evidence", Assert.Single(refused.Details!).Field);
        Assert.Equal("too_large", refused.Details![0].Code);
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-13-45T99:00:00Z")]
    [InlineData("")]
    public async Task A_time_that_is_not_a_time_is_refused(string text)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission["occurred_at"] = text;

        var refused = await Assert.ThrowsAsync<ValidationException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Contains(refused.Details!, detail => detail.Field == "occurred_at");
    }

    /// <summary>
    /// A time with no zone is read as UTC, not as the zone of whatever machine the runtime happens to run on.
    /// The reporter said an instant; a host-local reading would store a different one on every installation, and
    /// nobody reading the column afterwards could tell which had been assumed.
    /// </summary>
    [Fact]
    public async Task A_time_with_no_zone_is_read_as_utc()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission["occurred_at"] = "2026-09-17T11:04:00";

        await ReportedWorld.Service(db).SubmitAsync(submission, Ct);

        var stored = await db.Reports.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(new DateTime(2026, 9, 17, 11, 4, 0, DateTimeKind.Utc), stored.OccurredAt);

        // And what the reporter wrote is still what the assertion holds, zone or no zone.
        Assert.Equal("2026-09-17T11:04:00", stored.Assertion["occurred_at"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("campaign_id", "cmp_01K52JR0000000000000000001", "campaign_not_found")]
    [InlineData("contact_id", "cnt_01K52JR0000000000000000001", "contact_not_found")]
    [InlineData("work_item_id", "wi_01K52JR0000000000000000001", "work_item_not_found")]
    public async Task An_id_that_names_nothing_is_refused_because_it_is_a_typo(string field, string id, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submission = ReportedWorld.Submission();
        submission[field] = id;

        var refused = await Assert.ThrowsAsync<NotFoundException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal(code, refused.Code);
        Assert.Equal(0, await db.Reports.CountAsync(Ct));
    }

    /// <summary>Ids that disagree with each other describe nothing that happened.</summary>
    [Fact]
    public async Task A_work_item_from_another_campaign_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (_, _, item) = await ReportedWorld.WriteAsync(db, Ct);
        var elsewhere = WorkItemFactory.NewCampaign("Elsewhere", now: ReportedWorld.Noon);
        db.Campaigns.Add(elsewhere);
        await db.SaveChangesAsync(Ct);

        var submission = ReportedWorld.Submission();
        submission["campaign_id"] = elsewhere.PublicId;
        submission["work_item_id"] = item.PublicId;

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal("correlation_inconsistent", refused.Code);
        Assert.Equal(0, await db.Reports.CountAsync(Ct));
    }

    [Fact]
    public async Task A_work_item_about_another_person_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, _, item) = await ReportedWorld.WriteAsync(db, Ct);
        var somebodyElse = ReportedWorld.NewContact(db);
        await db.SaveChangesAsync(Ct);

        var submission = ReportedWorld.Submission();
        submission["campaign_id"] = campaign.PublicId;
        submission["contact_id"] = somebodyElse.PublicId;
        submission["work_item_id"] = item.PublicId;

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => ReportedWorld.Service(db).SubmitAsync(submission, Ct));

        Assert.Equal("correlation_inconsistent", refused.Code);
    }

    /// <summary>
    /// The kind a report writes is the runtime's own, so a caller cannot append one by hand and make an effect
    /// look reported when no report exists to read.
    /// </summary>
    [Fact]
    public void The_kind_a_report_writes_cannot_be_appended_by_a_caller()
    {
        Assert.Contains("external_effect_reported", Jason.Runtime.Journal.JournalKinds.Reserved);
    }
}
