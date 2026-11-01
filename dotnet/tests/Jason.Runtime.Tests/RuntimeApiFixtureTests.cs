using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests;

/// <summary>
/// The fixture's own helpers fail with what the runtime answered, not only with the status it answered with. An
/// integration test that compared statuses alone once failed with "Expected: OK, Actual: Conflict" and nothing
/// else, while the code that would have named the race — <c>concurrent_update</c> — was in the body it dropped.
/// </summary>
public class RuntimeApiFixtureTests
{
    private const string Nobody = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_call_expected_to_succeed_fails_with_the_error_the_runtime_gave()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = Nobody }, Ct));

        Assert.Contains("404", failure.Message, StringComparison.Ordinal);
        Assert.Contains("work_item_not_found", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_call_expected_to_be_refused_one_way_fails_with_the_refusal_it_got_instead()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => api.PostErrorAsync(Operations.WorkItemGet, new { work_item_id = Nobody }, HttpStatusCode.Conflict, Ct));

        Assert.Contains("404", failure.Message, StringComparison.Ordinal);
        Assert.Contains("work_item_not_found", failure.Message, StringComparison.Ordinal);
    }
}
