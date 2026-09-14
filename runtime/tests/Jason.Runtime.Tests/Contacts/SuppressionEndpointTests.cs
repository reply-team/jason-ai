using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Contacts;

public class SuppressionEndpointTests
{
    [Fact]
    public async Task The_whole_suppression_surface_is_reachable_over_http()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);

        var added = await fixture.PostOkAsync<SuppressionDto>(
            Operations.SuppressionAdd,
            new { channel = "Email", value = " Foo@Bar.com ", reason = "unsubscribed" },
            TestContext.Current.CancellationToken);
        Assert.Equal("foo@bar.com", added.Value);

        var listed = await fixture.PostOkAsync<Page<SuppressionDto>>(
            Operations.SuppressionList,
            new { channel = "email" },
            TestContext.Current.CancellationToken);
        Assert.Equal(added.Id, Assert.Single(listed.Items).Id);

        var removed = await fixture.PostOkAsync<SuppressionRemovedDto>(
            Operations.SuppressionRemove,
            new { channel = "email", value = "foo@bar.com", reason = "they asked to be reinstated" },
            TestContext.Current.CancellationToken);
        Assert.True(removed.Removed);

        var again = await fixture.PostOkAsync<SuppressionRemovedDto>(
            Operations.SuppressionRemove,
            new { channel = "email", value = "foo@bar.com", reason = "housekeeping" },
            TestContext.Current.CancellationToken);
        Assert.False(again.Removed);
    }

    [Fact]
    public async Task Lifting_a_suppression_without_a_reason_is_a_validation_envelope()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);

        var error = await fixture.PostErrorAsync(
            Operations.SuppressionRemove,
            new { channel = "email", value = "foo@bar.com" },
            HttpStatusCode.BadRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("reason", Assert.Single(error.Details!).Field);
    }
}
