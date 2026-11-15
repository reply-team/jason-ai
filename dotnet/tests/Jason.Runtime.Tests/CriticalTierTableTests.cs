namespace Jason.Runtime.Tests;

/// <summary>
/// The checks over the critical tier's whole table, which every test assembly holds to what it discovers
/// (<see cref="CriticalTierTests"/>): every family has a critical case, and the tier is at most 15 % of the suite, of its
/// methods and of its executed rows. There is no floor on the share: most changes add regression tests only, and the
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

    /// <summary>
    /// The ceiling bounds what the tier costs to run, so it reads rows: the tier's rows each assembly recorded, every row of
    /// a class the tier carries whole among them, over the suite's executed rows. The table's counted cases, which count a
    /// method of such a class once, answer another question and are not read here.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "the tier stays a fast loop: at most 15 % of the suite's executed rows")]
    public void The_tier_is_at_most_15_percent_of_the_suite_s_executed_rows()
    {
        var tier = CriticalTier.Sizes.Sum(size => size.TierRows);
        var suite = CriticalTier.Sizes.Sum(size => CriticalTier.CasesAtLeast(size.Cases));

        Assert.True(
            CriticalTier.WithinCeiling(tier, suite),
            $"The tier runs {tier} of the suite's {suite} executed rows, over the ceiling of {CriticalTier.Ceiling} % of the rows.");
    }

    /// <summary>
    /// An assembly's tier may run up to a tenth more rows than it recorded, so one more line on a page moves nothing; past
    /// that the check names what moved it and what to re-record.
    /// </summary>
    [Theory]
    [InlineData(200, 200, false)]
    [InlineData(220, 200, false)]
    [InlineData(221, 200, true)]
    [InlineData(9, 9, false)]
    [InlineData(10, 9, true)]
    [InlineData(0, 0, false)]
    public void An_assembly_s_tier_rows_are_held_to_a_tenth_over_what_it_recorded(int discovered, int recorded, bool refused)
    {
        var problem = CriticalTier.TierRowsProblem(discovered, recorded);

        Assert.True((problem is not null) == refused, $"{discovered} rows against {recorded} recorded: '{problem}'.");
        if (problem is not null)
        {
            Assert.Contains($"{discovered} row(s)", problem, StringComparison.Ordinal);
            Assert.Contains($"the {recorded} it recorded", problem, StringComparison.Ordinal);
            Assert.Contains("the pages print more commands", problem, StringComparison.Ordinal);
            Assert.Contains("a class the tier carries whole", problem, StringComparison.Ordinal);
            Assert.Contains("re-record", problem, StringComparison.Ordinal);
        }
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

    /// <summary>
    /// The table counts a method of a class the tier carries whole once for its family, however many rows it runs, so a
    /// page that prints one more command, or one fewer, leaves the table as it is; every other case counts once, a
    /// theory's rows each, and a method of the same name in a class not carried whole is counted row by row.
    /// </summary>
    [Fact]
    public void A_method_of_a_class_the_tier_carries_whole_counts_once_however_many_rows_it_runs()
    {
        const string Pages = "Jason.Cli.Tests.Documentation.DocumentedPageCommandsTests";
        const string Migrations = "Jason.Runtime.Tests.Persistence.FooMigrationTests";
        var cases = new (string Class, string Method, Family Family)[]
        {
            (Pages, "Every_documented_command_is_understood", Family.D1),
            (Pages, "Every_documented_command_is_understood", Family.D1),
            (Pages, "Every_documented_command_is_understood", Family.D1),
            (Pages, "Every_guarded_noun_is_printed_by_a_page", Family.D1),
            (Pages, "Every_guarded_noun_is_printed_by_a_page", Family.D1),
            (Migrations, "Down_restores_the_table", Family.P2),
            (Migrations, "Down_restores_the_table", Family.P2),
            (Migrations, "Up_keeps_the_rows", Family.F1),
            ("Jason.Cli.Tests.Documentation.OtherPageTests", "Every_documented_command_is_understood", Family.D1),
            ("Jason.Cli.Tests.Documentation.OtherPageTests", "Every_documented_command_is_understood", Family.D1),
            ("Jason.Runtime.Tests.Hosting.FooMigrationTests", "Down_restores_the_table", Family.P2),
        };

        var counted = CriticalTier.Counted(cases);

        Assert.Equal(
            [(Family.P2, 2), (Family.F1, 1), (Family.D1, 4)],
            counted.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));
    }

    /// <summary>
    /// The classes the tier carries whole are held by their names: a method of one outside the tier is named by its class
    /// and its name, once however many of its rows are outside, and a class of another name or another namespace is not.
    /// </summary>
    [Fact]
    public void A_method_of_a_class_the_tier_carries_whole_that_is_outside_it_is_named()
    {
        var cases = new (string Class, string Method, bool Critical)[]
        {
            ("Jason.Runtime.Tests.Persistence.FooMigrationTests", "Down_restores_the_table", false),
            ("Jason.Runtime.Tests.Persistence.FooMigrationTests", "Up_keeps_the_rows", true),
            ("Jason.Runtime.Tests.Persistence.MigrationHistoryTests", "The_history_is_append_only", false),
            ("Jason.Runtime.Tests.Persistence.DatabaseMigratorTests", "A_failed_migration_restores_the_backup", false),
            ("Jason.Runtime.Tests.Persistence.DatabaseMigratorTests", "A_failed_migration_restores_the_backup", true),
            ("Jason.Runtime.Tests.Persistence.FooSchemaTests", "The_index_is_unique", false),
            ("Jason.Runtime.Tests.Hosting.FooMigrationTests", "Elsewhere", false),
            ("Jason.Cli.Tests.Documentation.DocumentedPageCommandsTests", "Every_documented_command_is_understood", false),
            ("Jason.Cli.Tests.Documentation.DocumentedPageCommandsTests", "Every_documented_command_is_understood", false),
            ("Jason.Cli.Tests.Documentation.DocumentedPageCommandsTests", "A_file_a_printed_command_writes_lands_in_this_tests_own_directory", false),
            ("Jason.Cli.Tests.Documentation.OtherPageTests", "Every_line_is_typed", false),
        };

        var outside = CriticalTier.OutsideTheTier(cases);

        Assert.Equal(
            [
                "FooMigrationTests.Down_restores_the_table",
                "MigrationHistoryTests.The_history_is_append_only",
                "DatabaseMigratorTests.A_failed_migration_restores_the_backup",
                "DocumentedPageCommandsTests.Every_documented_command_is_understood",
            ],
            outside);
    }
}
