namespace Jason.Runtime.Tests;

/// <summary>
/// The checks over the critical tier's whole table, which every test assembly holds to what it discovers
/// (<see cref="CriticalTierTests"/>): every family has a critical case, and the tier is at most 15 % of the suite, of its
/// methods and of its executed cases. There is no floor on the share: most changes add regression tests only, and the
/// share may fall as the suite grows.
/// </summary>
public class CriticalTierTableTests
{
    [Fact]
    [Critical(Family.B7, "a family without a critical case is a behaviour the tier no longer covers")]
    public void Every_family_has_one_count_per_assembly_and_at_least_one_critical_case()
    {
        Assert.True(
            CriticalTier.Sizes.Length == CriticalTier.Assemblies.Length,
            $"The table has sizes for {CriticalTier.Sizes.Length} assemblies, and {CriticalTier.Assemblies.Length} columns.");
        var malformed = CriticalTier.Cases
            .Where(pair => pair.Value.Length != CriticalTier.Assemblies.Length || pair.Value.Any(count => count < 0))
            .Select(pair => $"{pair.Key} [{string.Join(", ", pair.Value)}]")
            .ToList();
        Assert.True(malformed.Count == 0, $"Rows that are not one count per assembly: {string.Join("; ", malformed)}.");

        var without = CriticalTier.FamiliesWithoutACase(CriticalTier.Cases);

        Assert.True(without.Count == 0, $"Families with no critical case in any assembly: {string.Join(", ", without)}.");
    }

    [Fact]
    public void A_family_with_no_row_or_a_row_of_zeros_is_named()
    {
        var cases = new Dictionary<Family, int[]>
        {
            [Family.B1] = [0, 1, 0, 0, 0],
            [Family.B2] = [0, 0, 0, 0, 0],
        };

        var without = CriticalTier.FamiliesWithoutACase(cases);

        Assert.DoesNotContain(Family.B1, without);
        Assert.Contains(Family.B2, without);
        Assert.Contains(Family.X3, without);
        Assert.Equal(Enum.GetValues<Family>().Length - 1, without.Count);
    }

    [Fact]
    [Critical(Family.B7, "the tier stays a fast loop: at most 15 % of the suite's methods")]
    public void The_tier_is_at_most_15_percent_of_the_suite_s_methods()
    {
        var tier = CriticalTier.Sizes.Sum(size => size.TierMethods);
        var suite = CriticalTier.Sizes.Sum(size => size.Methods);

        Assert.True(
            CriticalTier.WithinCeiling(tier, suite),
            $"The tier has {tier} of the suite's {suite} methods, over the ceiling of {CriticalTier.Ceiling} % of the methods.");
    }

    [Fact]
    [Critical(Family.B7, "the tier stays a fast loop: at most 15 % of the suite's executed cases")]
    public void The_tier_is_at_most_15_percent_of_the_suite_s_executed_cases()
    {
        var tier = CriticalTier.Cases.Values.Sum(row => row.Sum());
        var suite = CriticalTier.Sizes.Sum(size => CriticalTier.CasesAtLeast(size.Cases));

        Assert.True(
            CriticalTier.WithinCeiling(tier, suite),
            $"The tier has {tier} of the suite's {suite} executed cases, over the ceiling of {CriticalTier.Ceiling} % of the cases.");
    }

    /// <summary>
    /// A platform enumerates some theories' rows differently — a row only Windows has a program for — so the suite's
    /// executed cases are not one number everywhere, and an assembly is held to nine tenths of what the table measured.
    /// </summary>
    [Theory]
    [InlineData(5_004, 4_504)]
    [InlineData(10, 9)]
    [InlineData(9, 9)]
    public void An_assembly_s_cases_are_held_to_nine_tenths_of_what_the_table_measured(int measured, int atLeast)
    {
        Assert.Equal(atLeast, CriticalTier.CasesAtLeast(measured));
    }

    /// <summary>The ceiling lets exactly 15 % through, and not one more.</summary>
    [Theory]
    [InlineData(15, 100, true)]
    [InlineData(16, 100, false)]
    [InlineData(1_500, 10_000, true)]
    [InlineData(1_501, 10_000, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    public void The_ceiling_lets_exactly_15_percent_through(int tier, int suite, bool within)
    {
        Assert.True(
            CriticalTier.WithinCeiling(tier, suite) == within,
            $"{tier} of {suite} was {(within ? "refused" : "let through")}.");
    }
}
