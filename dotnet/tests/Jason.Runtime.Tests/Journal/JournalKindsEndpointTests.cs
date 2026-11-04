using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Journal;
using Jason.Runtime.Domain;
using Jason.Runtime.Tests.Documentation;

namespace Jason.Runtime.Tests.Journal;

/// <summary>
/// <c>journal.kinds</c>: the kinds the runtime reserves for its own journal lines, each as its published document
/// says it, a page at a time in kind order.
/// </summary>
public class JournalKindsEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// While the catalog fits one default page, the whole of it comes back on that page. A catalog that outgrows the page
    /// is no fault of the read: the walk across pages holds the rest, and this row says which of the two has changed.
    /// </summary>
    [Fact]
    public async Task Every_kind_comes_back_in_kind_order_on_one_default_page()
    {
        Assert.True(
            JournalKindCatalog.Kinds.Count <= Paging.DefaultLimit,
            $"The catalog has {JournalKindCatalog.Kinds.Count} kinds, more than one default page of {Paging.DefaultLimit}; the walk across pages holds them, so this row is the one to change.");
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var page = await api.PostOkAsync<Page<JournalKindDto>>(Operations.JournalKinds, new { }, Ct);

        Assert.Equal(JournalKindCatalog.Kinds, page.Items.Select(kind => kind.Kind));
        Assert.Null(page.NextCursor);
    }

    /// <summary>The exchange a reader of the API sees, recorded: a first page of two kinds and the cursor to the next.</summary>
    [Fact]
    public async Task A_first_page_of_two_kinds_is_recorded()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var page = await api.RecordAsync<Page<JournalKindDto>>(Operations.JournalKinds, "first-page", new { Limit = 2 }, Ct);

        Assert.Equal(JournalKindCatalog.Kinds.Take(2), page.Items.Select(kind => kind.Kind));
        Assert.Equal(Paging.EncodeCursor(JournalKindCatalog.Kinds[1]), page.NextCursor);
    }

    [Fact]
    public async Task Pages_walk_the_catalog_with_no_kind_missed_or_repeated()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var walked = new List<string>();
        string? cursor = null;

        do
        {
            var page = await api.PostOkAsync<Page<JournalKindDto>>(Operations.JournalKinds, new { Limit = 7, Cursor = cursor }, Ct);
            Assert.InRange(page.Items.Count, 1, 7);
            walked.AddRange(page.Items.Select(kind => kind.Kind));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(JournalKindCatalog.Kinds, walked);
    }

    /// <summary>
    /// The answer is the published document whole, read as the runtime holds it: a place an entity is named at is a
    /// list even where the document writes one pointer, an entity that names no list says <c>each</c> is false, and
    /// the body's schema travels as the document spells it.
    /// </summary>
    [Fact]
    public async Task Each_kind_is_its_published_document_with_every_place_a_list()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, body) = await api.PostAsync(Operations.JournalKinds, new { }, Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        var items = JsonNode.Parse(body)!["items"]!.AsArray();
        Assert.Equal(JournalKindCatalog.Kinds.Count, items.Count);
        foreach (var item in items)
        {
            var kind = (string)item!["kind"]!;
            var published = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", "contracts", "journal", kind + ".json")))!;
            Assert.True(JsonNode.DeepEquals(AsTheRuntimeHoldsIt(published), item), $"{kind}: {item.ToJsonString()}");
        }
    }

    [Fact]
    public async Task One_place_is_a_list_of_one_on_the_wire()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (_, body) = await api.PostAsync(Operations.JournalKinds, new { Limit = 1, Cursor = Paging.EncodeCursor("campaign_archived") }, Ct);

        var created = Assert.Single(JsonNode.Parse(body)!["items"]!.AsArray())!;
        Assert.Equal("campaign_created", (string)created["kind"]!);
        Assert.Equal("""["/campaign_id"]""", created["entities"]!["campaign"]!["at"]!.ToJsonString());
        Assert.Equal("always", (string)created["entities"]!["campaign"]!["presence"]!);
    }

    [Theory]
    [InlineData("limit", 0, null)]
    [InlineData("limit", 1001, null)]
    [InlineData("cursor", null, "not a cursor")]
    public async Task A_limit_or_cursor_out_of_its_range_is_refused(string field, int? limit, string? cursor)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(Operations.JournalKinds, new { Limit = limit, Cursor = cursor }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(field, detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    private static JsonNode AsTheRuntimeHoldsIt(JsonNode published)
    {
        var held = published.DeepClone().AsObject();
        foreach (var (_, entity) in held["entities"]!.AsObject())
        {
            var place = entity!.AsObject();
            if (place["at"] is JsonValue one)
            {
                place["at"] = new JsonArray(one.DeepClone());
            }

            place["each"] ??= false;
        }

        return held;
    }
}
