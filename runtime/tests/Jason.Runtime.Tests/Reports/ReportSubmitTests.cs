using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Reports;

/// <summary>
/// Admission: somebody says an effect happened outside Jason, and Jason writes down that they said it. What
/// these tests are about is the line between the two halves of the row — the reporter's words, which are kept
/// exactly, and the runtime's receipt, which is the little it knows by itself.
/// </summary>
public class ReportSubmitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reported_effect_is_admitted_as_the_reporters_word_with_the_runtimes_receipt_beside_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, item) = await ReportedWorld.WriteAsync(db, Ct);

        var submitted = ReportedWorld.Submission();
        submitted["provider"] = "reply";
        submitted["account"] = "team@example.test";
        submitted["occurred_at"] = "2026-09-17T11:04:00Z";
        submitted["observed_at"] = "2026-09-17T11:05:12Z";
        submitted["campaign_id"] = campaign.PublicId;
        submitted["contact_id"] = contact.PublicId;
        submitted["work_item_id"] = item.PublicId;
        submitted["operation"] = "campaign.enroll";
        submitted["external_ids"] = new JsonArray(new JsonObject { ["kind"] = "message", ["value"] = "m-1" });
        submitted["evidence"] = new JsonObject { ["subject"] = "Following up" };
        submitted["unknown_fields"] = new JsonArray("observed_at");
        submitted["uncertainty"] = "The send was queued; nobody watched it leave.";
        submitted["reason"] = "Reporting it the moment I noticed.";

        var expected = submitted.DeepClone().AsObject();
        expected.Remove("actor");
        expected.Remove("reason");

        var admitted = await ReportedWorld.Service(db).SubmitAsync(submitted, Ct);

        Assert.StartsWith("rpt_", admitted.Id, StringComparison.Ordinal);
        Assert.Equal(ReportedWorld.Noon, admitted.ReceivedAt.UtcDateTime);
        Assert.Equal(new ActorRef(ActorType.Human, "person-1"), admitted.Reporter);
        Assert.Equal("Reporting it the moment I noticed.", admitted.Reason);
        Assert.Equal(ReportDedupOutcome.Admitted, admitted.Dedup.Outcome);
        Assert.Null(admitted.Dedup.Matched);

        // The assertion is what was sent, not a rewrite of it: same keys, same values, same order of a list.
        Assert.True(JsonNode.DeepEquals(expected, admitted.Assertion));
        Assert.Equal(CanonicalJson.Hash(expected), admitted.AssertionHash);

        Assert.Equal(campaign.PublicId, admitted.Correlation.CampaignId);
        Assert.Equal(contact.PublicId, admitted.Correlation.ContactId);
        Assert.Equal(item.PublicId, admitted.Correlation.WorkItemId);
        Assert.Equal("campaign.enroll", admitted.Correlation.Operation);
        Assert.True(admitted.Correlation.OperationKnown);
        Assert.True(admitted.Correlation.ContactInCampaign);

        // Admission establishes that a report is well-formed and attributable. It establishes nothing else, and
        // the answer says so rather than leaving it to be assumed.
        Assert.False(admitted.Correlation.Verified);

        var stored = await db.Reports.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(admitted.Id, stored.PublicId);
        Assert.Equal("email_sent", stored.Effect);
        Assert.Equal(ReportedWorld.Noon, stored.ReceivedAt);
        Assert.True(JsonNode.DeepEquals(expected, stored.Assertion));
    }

    /// <summary>
    /// The catalog decides a boolean, never the admission. An effect nobody modelled is still an effect that
    /// reached somebody, and a runtime that refused to hear about it would be choosing not to know.
    /// </summary>
    [Fact]
    public async Task A_report_naming_an_operation_Jason_does_not_know_is_admitted_as_the_reporters_word()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var submitted = ReportedWorld.Submission();
        submitted["operation"] = "carrier_pigeon.dispatch";

        var admitted = await ReportedWorld.Service(db).SubmitAsync(submitted, Ct);

        Assert.Equal(ReportDedupOutcome.Admitted, admitted.Dedup.Outcome);
        Assert.Equal("carrier_pigeon.dispatch", admitted.Correlation.Operation);
        Assert.False(admitted.Correlation.OperationKnown);
        Assert.Equal("carrier_pigeon.dispatch", admitted.Assertion["operation"]!.GetValue<string>());
        Assert.Equal("carrier_pigeon.dispatch", (await db.Reports.AsNoTracking().SingleAsync(Ct)).Operation);
    }

    /// <summary>
    /// A report that names nothing local is still a report: "as available" is the rule, and refusing one would
    /// push the reporter to invent a correlation rather than admit they have none.
    /// </summary>
    [Fact]
    public async Task A_report_that_correlates_to_nothing_is_still_admitted()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var admitted = await ReportedWorld.Service(db).SubmitAsync(ReportedWorld.Submission(), Ct);

        Assert.Null(admitted.Correlation.CampaignId);
        Assert.Null(admitted.Correlation.ContactId);
        Assert.Null(admitted.Correlation.WorkItemId);
        Assert.Null(admitted.Correlation.Operation);
        Assert.False(admitted.Correlation.OperationKnown);
        Assert.Null(admitted.Correlation.ContactInCampaign);
    }

    /// <summary>
    /// An effect that reached somebody the local campaign has never heard of is exactly the kind of fact a
    /// report exists to carry. Refusing it would make the reporter drop either the campaign or the person.
    /// </summary>
    [Fact]
    public async Task A_contact_who_is_not_a_member_yet_is_admitted_and_said_to_be_one()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, _, _) = await ReportedWorld.WriteAsync(db, Ct);
        var stranger = ReportedWorld.NewContact(db);
        await db.SaveChangesAsync(Ct);

        var submitted = ReportedWorld.Submission();
        submitted["campaign_id"] = campaign.PublicId;
        submitted["contact_id"] = stranger.PublicId;

        var admitted = await ReportedWorld.Service(db).SubmitAsync(submitted, Ct);

        Assert.Equal(stranger.PublicId, admitted.Correlation.ContactId);
        Assert.False(admitted.Correlation.ContactInCampaign);
        Assert.False((await db.Reports.AsNoTracking().SingleAsync(Ct)).ContactInCampaign);
    }

    /// <summary>
    /// The case the marker exists for, and the one it is easiest to get wrong. A membership is never deleted —
    /// removing somebody sets the row to excluded — so asking whether a row exists answers "yes" for a person
    /// who was taken out, which is exactly who an out-of-band effect would be worth reporting about.
    /// </summary>
    [Fact]
    public async Task A_contact_who_was_removed_from_the_campaign_is_not_said_to_be_in_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, _) = await ReportedWorld.WriteAsync(db, Ct);

        var clock = new FixedClock(ReportedWorld.Noon);
        var memberships = new MembershipService(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
        await memberships.RemoveAsync(new RemoveContactsRequest(campaign.PublicId, [contact.PublicId], null, "taken out"), Ct);

        var submitted = ReportedWorld.Submission();
        submitted["campaign_id"] = campaign.PublicId;
        submitted["contact_id"] = contact.PublicId;

        var admitted = await ReportedWorld.Service(db).SubmitAsync(submitted, Ct);

        Assert.False(admitted.Correlation.ContactInCampaign);
        Assert.False((await db.Reports.AsNoTracking().SingleAsync(Ct)).ContactInCampaign);

        // The row is still there, which is why the state and not the row is what the question is about.
        Assert.Equal(
            MembershipState.Excluded,
            (await db.CampaignContacts.AsNoTracking().SingleAsync(m => m.ContactId == contact.Id, Ct)).State);
    }

    /// <summary>
    /// The chronicle carries that a report landed and what it was about, by identifier. The assertion, the
    /// summary and whatever evidence came with it stay in the report: a journal line is a note, not a payload.
    /// </summary>
    [Fact]
    public async Task An_admitted_report_leaves_one_line_naming_it_and_nothing_else()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, item) = await ReportedWorld.WriteAsync(db, Ct);

        var submitted = ReportedWorld.Submission();
        submitted["campaign_id"] = campaign.PublicId;
        submitted["contact_id"] = contact.PublicId;
        submitted["work_item_id"] = item.PublicId;
        submitted["operation"] = "campaign.enroll";
        submitted["evidence"] = new JsonObject { ["subject"] = "Following up" };

        var admitted = await ReportedWorld.Service(db).SubmitAsync(submitted, Ct);

        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == "external_effect_reported", Ct);
        Assert.Equal(ActorType.Human, line.ActorType);
        Assert.Equal("person-1", line.ActorId);
        Assert.Equal(campaign.Id, line.CampaignId);

        var written = line.New!.AsObject();
        Assert.Equal(admitted.Id, written["id"]!.GetValue<string>());
        Assert.Equal("email_sent", written["effect"]!.GetValue<string>());
        Assert.Equal("campaign.enroll", written["operation"]!.GetValue<string>());
        Assert.Equal(contact.PublicId, written["contact_id"]!.GetValue<string>());
        Assert.Equal(item.PublicId, written["work_item_id"]!.GetValue<string>());
        Assert.DoesNotContain("evidence", written.Select(pair => pair.Key), StringComparer.Ordinal);
        Assert.DoesNotContain("summary", written.Select(pair => pair.Key), StringComparer.Ordinal);
        Assert.DoesNotContain("assertion", written.Select(pair => pair.Key), StringComparer.Ordinal);
    }
}
