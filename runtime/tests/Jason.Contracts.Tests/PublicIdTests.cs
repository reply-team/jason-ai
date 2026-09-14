using System.Text.RegularExpressions;
using Jason.Contracts.Ids;

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
}
