using System.Net;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// <c>workitem.review</c> at the door: the attempt a request's header names is handed to the review, which holds a request
/// from inside a run to speak as that run before it reads anything else of the world.
/// </summary>
public class ReviewDoorTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object Body = new
    {
        work_item_id = "wi_01K7QN0000000000000000NONE",
        review_hash = "sha256:00",
        result = "passed",
        actor = new { type = "human", id = "ada" },
    };

    [Fact]
    public async Task From_inside_a_run_a_review_in_a_person_s_name_is_refused_before_the_send_is_read()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));

        var (status, text) = await api.PostAsync(Operations.WorkItemReview, Body, new Dictionary<string, string> { ["Jason-Attempt"] = "att_01K7QN0000000000000000000R" }, Ct);

        Assert.True(status == HttpStatusCode.BadRequest, $"{(int)status}: {text}");
        var error = JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
        Assert.Equal("actor", Assert.Single(error.Details!).Field);
    }

    [Fact]
    public async Task From_outside_a_run_the_same_review_goes_on_to_read_the_send()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));

        var (status, text) = await api.PostAsync(Operations.WorkItemReview, Body, headers: null, Ct);

        Assert.True(status == HttpStatusCode.NotFound, $"{(int)status}: {text}");
        Assert.Equal("work_item_not_found", JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error.Code);
    }
}
