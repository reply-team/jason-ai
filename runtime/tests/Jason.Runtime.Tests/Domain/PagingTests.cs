using Jason.Runtime.Domain;

namespace Jason.Runtime.Tests.Domain;

public class PagingTests
{
    [Fact]
    public void Limit_defaults_to_a_hundred_and_stays_within_its_bounds()
    {
        Assert.Equal(Paging.DefaultLimit, Paging.ResolveLimit(null));
        Assert.Equal(5, Paging.ResolveLimit(5));
        Assert.Equal(Paging.MaxLimit, Paging.ResolveLimit(Paging.MaxLimit));

        foreach (var rejected in new int?[] { 0, -1, Paging.MaxLimit + 1 })
        {
            var ex = Assert.Throws<ValidationException>(() => Paging.ResolveLimit(rejected));
            Assert.Equal("limit", Assert.Single(ex.Details!).Field);
        }
    }

    [Fact]
    public void Cursors_round_trip_and_nonsense_is_refused()
    {
        Assert.Null(Paging.DecodeCursor(null));
        Assert.Null(Paging.DecodeCursor("   "));

        var cursor = Paging.EncodeCursor("cmp_01ABCDEF");
        Assert.DoesNotContain("=", cursor, StringComparison.Ordinal);
        Assert.Equal("cmp_01ABCDEF", Paging.DecodeCursor(cursor));

        var ex = Assert.Throws<ValidationException>(() => Paging.DecodeCursor("***"));
        Assert.Equal("cursor", Assert.Single(ex.Details!).Field);
    }

    [Fact]
    public void A_full_page_points_at_its_last_row_and_a_short_page_ends_the_listing()
    {
        var page = Paging.ToPage(new[] { "a", "b", "c" }, 2, s => "cmp_" + s, s => s.ToUpperInvariant());

        Assert.Equal(["A", "B"], page.Items);
        Assert.Equal(Paging.EncodeCursor("cmp_b"), page.NextCursor);

        var last = Paging.ToPage(new[] { "a", "b" }, 2, s => "cmp_" + s, s => s);

        Assert.Equal(["a", "b"], last.Items);
        Assert.Null(last.NextCursor);
    }
}
