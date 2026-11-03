using System.Text.RegularExpressions;
using Jason.Contracts.Ids;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests;

public partial class PublicIdTests
{
    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]{26}$")]
    private static partial Regex CrockfordUlid();

    [Fact]
    public void Ulid_is_26_crockford_base32_characters()
    {
        var ulid = Ulid.NewUlid();
        Assert.Matches(CrockfordUlid(), ulid);
    }

    [Fact]
    public void Ulids_sort_by_time()
    {
        var earlier = Ulid.NewUlid(DateTimeOffset.UnixEpoch.AddMilliseconds(1));
        var later = Ulid.NewUlid(DateTimeOffset.UnixEpoch.AddMilliseconds(2));
        Assert.True(string.CompareOrdinal(earlier, later) < 0);
        Assert.Equal(earlier[..10], Ulid.NewUlid(DateTimeOffset.UnixEpoch.AddMilliseconds(1))[..10]);
    }

    [Fact]
    public void Ulids_are_unique()
    {
        var set = new HashSet<string>(Enumerable.Range(0, 1000).Select(_ => Ulid.NewUlid()));
        Assert.Equal(1000, set.Count);
    }

    [Fact]
    [Critical(Family.X1, "ordering inside one millisecond is fragile")]
    public void Ulids_generated_in_one_process_are_strictly_increasing_even_within_a_millisecond()
    {
        var ids = Enumerable.Range(0, 5000).Select(_ => Ulid.NewUlid()).ToList();
        for (var i = 1; i < ids.Count; i++)
        {
            Assert.True(string.CompareOrdinal(ids[i - 1], ids[i]) < 0, $"{ids[i - 1]} is not before {ids[i]}");
        }
    }

    [Fact]
    [Critical(Family.R2, "cmp_/wi_/att_ ids")]
    public void Public_ids_are_prefixed_ulids()
    {
        var id = PublicId.New("cmp");
        Assert.StartsWith("cmp_", id, StringComparison.Ordinal);
        Assert.Matches(CrockfordUlid(), id[4..]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("cmp1")]
    [InlineData("toolongprefix")]
    public void Prefixes_must_be_two_to_eight_lowercase_letters(string prefix) =>
        Assert.Throws<ArgumentException>(() => PublicId.New(prefix));

    /// <summary>A fresh id of a prefix is well formed for that prefix: one rule beside the one that makes them.</summary>
    [Theory]
    [InlineData("apr")]
    [InlineData("dec")]
    [InlineData("cmp")]
    public void A_new_id_is_well_formed_for_its_own_prefix(string prefix) =>
        Assert.True(PublicId.IsWellFormed(prefix, PublicId.New(prefix)));

    [Fact]
    public void An_id_of_another_prefix_is_not_well_formed() =>
        Assert.False(PublicId.IsWellFormed("apr", PublicId.New("dec")));

    /// <summary>Everything that is not the prefix, an underscore and 26 uppercase Crockford characters is refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("apr_")]
    [InlineData("apr")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6W")]
    [InlineData("apr_01jbz3x7q9k2m4n6p8r0s2t4v6")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4VI")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4VL")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4VO")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4VU")]
    [InlineData("APR_01JBZ3X7Q9K2M4N6P8R0S2T4V6")]
    [InlineData("apr-01JBZ3X7Q9K2M4N6P8R0S2T4V6")]
    [InlineData(" apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6 ")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V٣")]
    public void Anything_else_is_not_well_formed(string? value) =>
        Assert.False(PublicId.IsWellFormed("apr", value));

    [Fact]
    public void A_made_up_id_of_the_right_shape_is_well_formed() =>
        Assert.True(PublicId.IsWellFormed("apr", "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6"));
}
