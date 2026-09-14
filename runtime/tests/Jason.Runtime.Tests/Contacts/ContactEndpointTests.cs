using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Contacts;

public class ContactEndpointTests
{
    [Fact]
    public async Task A_contact_created_over_http_comes_back_normalized()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);

        var (status, body) = await fixture.PostAsync(
            Operations.ContactCreate,
            new { first_name = "Ada", channels = new[] { new { channel = "Email", value = "A@B.co" } } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"value\":\"a@b.co\"", body, StringComparison.Ordinal);
        Assert.Contains("\"primary\":false", body, StringComparison.Ordinal);
        Assert.Contains("\"custom\":{}", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_field_problem_comes_back_as_validation_details()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);

        var error = await fixture.PostErrorAsync(
            Operations.ContactCreate,
            new { time_zone = "Pacific Standard Time" },
            HttpStatusCode.BadRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("time_zone", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task An_unknown_contact_is_a_not_found_envelope()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);

        var error = await fixture.PostErrorAsync(
            Operations.ContactGet,
            new { contact_id = "cnt_missing" },
            HttpStatusCode.NotFound,
            TestContext.Current.CancellationToken);

        Assert.Equal("contact_not_found", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task The_whole_contact_surface_is_reachable_over_http()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);

        var created = await fixture.PostOkAsync<ContactDto>(
            Operations.ContactCreate,
            new { first_name = "Ada", custom = new JsonObject { ["crm_id"] = "7" } },
            TestContext.Current.CancellationToken);

        var fetched = await fixture.PostOkAsync<ContactDto>(Operations.ContactGet, new { contact_id = created.Id }, TestContext.Current.CancellationToken);
        Assert.Equal(created.Id, fetched.Id);

        var updated = await fixture.PostOkAsync<ContactDto>(
            Operations.ContactUpdate,
            new { contact_id = created.Id, company = "Analytical Engines" },
            TestContext.Current.CancellationToken);
        Assert.Equal("Analytical Engines", updated.Company);
        Assert.Equal("Ada", updated.FirstName);

        var listed = await fixture.PostOkAsync<Page<ContactDto>>(Operations.ContactList, new { }, TestContext.Current.CancellationToken);
        Assert.Equal(created.Id, Assert.Single(listed.Items).Id);

        var archived = await fixture.PostOkAsync<ContactDto>(
            Operations.ContactArchive,
            new { contact_id = created.Id, reason = "left the company" },
            TestContext.Current.CancellationToken);
        Assert.NotNull(archived.ArchivedAt);
    }
}
