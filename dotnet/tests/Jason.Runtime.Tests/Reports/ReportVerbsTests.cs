using System.Net;
using System.Reflection;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Reports;

/// <summary>
/// The third thing that keeps an admitted report immutable, and the one the database cannot do: there is no verb
/// that changes or removes one. The triggers and the interceptor are proven where the rows are; this is proven
/// at the door, which is the only place a caller can reach.
/// </summary>
public class ReportVerbsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_verb_writes_an_admitted_report()
    {
        var published = typeof(Operations)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(operation => operation.StartsWith("report.", StringComparison.Ordinal))
            .OrderBy(operation => operation, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["report.get", "report.list", "report.submit"], published);

        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        foreach (var absent in (string[])["report.update", "report.delete", "report.verify", "report.reconcile"])
        {
            var (status, _) = await api.PostRawAsync(absent, "{}", Ct);
            Assert.Equal(HttpStatusCode.NotFound, status);
        }
    }

    /// <summary>
    /// Submission is bound as the document it arrived as, so the shapes a typed request would have refused for
    /// free have to be refused here. A body that is not an object is the caller's mistake, never the runtime's.
    /// </summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"effect\":\"email_sent\"}]")]
    [InlineData("null")]
    [InlineData("\"a report\"")]
    [InlineData("7")]
    public async Task A_submission_that_is_not_an_object_is_the_callers_mistake(string body)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, text) = await api.PostRawAsync(Operations.ReportSubmit, body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("invalid_request", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A property written twice says two things and the contract picks neither. It is refused at the door for
    /// every operation; a verb that keeps the document it was sent has the most to lose if it were not.
    /// </summary>
    [Fact]
    public async Task A_property_written_twice_is_refused_before_anything_reads_it()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, text) = await api.PostRawAsync(
            Operations.ReportSubmit,
            """{"effect":"email_sent","effect":"call_made","tool":"t","summary":"s","actor":{"type":"human","id":"person-1"}}""",
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("invalid_request", text, StringComparison.Ordinal);
    }

    /// <summary>The whole door, once: a real submission over HTTP answers with the report it admitted.</summary>
    [Fact]
    public async Task A_report_submitted_over_http_comes_back_with_its_receipt()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var admitted = await api.PostOkAsync<ReportDto>(
            Operations.ReportSubmit,
            new
            {
                effect = "email_sent",
                tool = "some-other-cli",
                summary = "Sent by hand, outside the runtime.",
                actor = new { type = "human", id = "person-1" },
            },
            Ct);

        Assert.StartsWith("rpt_", admitted.Id, StringComparison.Ordinal);
        Assert.Equal(ReportDedupOutcome.Admitted, admitted.Dedup.Outcome);
        Assert.False(admitted.Correlation.Verified);
        Assert.Equal("email_sent", admitted.Assertion["effect"]!.GetValue<string>());

        // The envelope is not part of what was asserted about the world.
        Assert.False(admitted.Assertion.ContainsKey("actor"));
    }
}
