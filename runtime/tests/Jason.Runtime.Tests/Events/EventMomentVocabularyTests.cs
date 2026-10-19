using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Runtime.Events;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// One vocabulary of moments on both sides of a read: what the events contract's output schema accepts as when an event
/// happened is exactly what the store's reader can read. A page the schema accepted and the reader could not read would
/// be refused after it had passed the contract, and read again from the same cursor for ever.
/// </summary>
public class EventMomentVocabularyTests
{
    [Theory]
    [InlineData("2026-10-01T09:00:00Z", true)]
    [InlineData("2026-10-01T09:00:00.1Z", true)]
    [InlineData("2026-10-01T09:00:00.123Z", true)]
    [InlineData("2026-10-01T09:00:00.1234567Z", true)]
    [InlineData("2024-02-29T23:59:59.999Z", true)]
    [InlineData("0001-01-01T00:00:00Z", true)]
    [InlineData("9999-12-31T23:59:59.9999999Z", true)]
    [InlineData("2026-10-01T09:00:00.12345678Z", false)]
    [InlineData("2026-10-01T09:00:00.Z", false)]
    [InlineData("2026-10-01t09:00:00Z", false)]
    [InlineData("2026-10-01T09:00:00z", false)]
    [InlineData("2026-10-01T09:00:00+00:00", false)]
    [InlineData("2026-10-01T09:00:00+01:00", false)]
    [InlineData("2026-10-01T09:00:00+15:00", false)]
    [InlineData("2026-12-31T23:59:60Z", false)]
    [InlineData("2026-10-01T24:00:00Z", false)]
    [InlineData("2026-02-30T09:00:00Z", false)]
    [InlineData("2026-10-01 09:00:00Z", false)]
    [InlineData("2026-10-01T09:00Z", false)]
    [InlineData("2026-10-01", false)]
    [InlineData("0000-01-01T00:00:00Z", false)]
    public void The_schema_and_the_reader_refuse_the_same_moments(string moment, bool accepted)
    {
        var bounced = new JsonObject
        {
            ["kind"] = "bounce",
            ["id"] = null,
            ["occurred_at"] = moment,
            ["contact"] = "c_1001",
            ["campaign"] = "p_1",
            ["message"] = null,
            ["bounce"] = "hard",
        };
        var page = new JsonObject { ["events"] = new JsonArray(bounced), ["cursor"] = "c-1", ["has_more"] = false };

        var problems = EventsCatalog.Find("events.read")!.CheckResult(page);
        var read = EventPage.Read(page);

        Assert.True(problems.Count == 0 == accepted, $"The schema {(problems.Count == 0 ? "accepted" : "refused")} {moment}; it should {(accepted ? "accept" : "refuse")} it.");
        Assert.True(read is not null == accepted, $"The reader {(read is null ? "refused" : "read")} {moment}; it should {(accepted ? "read" : "refuse")} it.");
    }
}
