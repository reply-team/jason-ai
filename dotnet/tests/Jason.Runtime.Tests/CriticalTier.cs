using System.Reflection;
using System.Text.RegularExpressions;
using Xunit.Runner.Common;
using Xunit.Sdk;
using Xunit.v3;

namespace Jason.Runtime.Tests;

/// <summary>
/// The critical tier as one table — how many critical cases each family has in each test assembly — the test
/// assembly's own discovery, which the table is held to, and the reasons the sources give for each mark.
/// </summary>
/// <remarks>
/// <para>
/// A row of a theory fed by a member is in the tier only if the member still yields it when the theories are discovered,
/// so whether it is there is decided at run time, and a run filtered to the tier does not notice one that has gone. The
/// table is what notices: each test assembly discovers itself, theories by their rows, as the runner does, and compares
/// the critical cases it finds, family by family, with its own column. A change to the tier — a test marked, a row gone —
/// is a change to this table, made on purpose.
/// </para>
/// <para>
/// A class the tier carries whole (<see cref="CarriedWhole"/>) is held otherwise: every method of it is in the tier, by
/// its class's name, and the table counts each of its methods once, however many rows it runs (<see cref="Counted"/>),
/// so an edit to a page, which moves those rows, leaves the table as it is.
/// </para>
/// <para>
/// The ceiling reads rows, not counted cases: each assembly records its tier's rows in <see cref="Sizes"/> and runs at
/// most a tenth more, and the ceiling sums what each may run, the tenth included, over the suite's executed rows.
/// </para>
/// <para>
/// This file is linked into every test project, so each assembly holds its own column; the checks over the whole table
/// — every family has a critical case, the tier's share stays under its ceiling — run once, in this project.
/// </para>
/// </remarks>
internal static partial class CriticalTier
{
    /// <summary>The test assemblies, in the order of the table's columns.</summary>
    public static readonly string[] Assemblies =
        ["Jason.App.Tests", "Jason.Cli.Tests", "Jason.Contracts.Tests", "Jason.PluginHost.Tests", "Jason.Runtime.Tests"];

    /// <summary>
    /// The critical cases of each family, in each test assembly, as discovery finds them and <see cref="Counted"/> counts
    /// them: a method of a class the tier carries whole once.
    /// </summary>
    public static readonly IReadOnlyDictionary<Family, int[]> Cases = new Dictionary<Family, int[]>
    {
        //              App  Cli  Contracts  PluginHost  Runtime
        [Family.B1] =  [  9,   2,   4,   2,   8],
        [Family.B2] =  [  4,   2,   0,   0,   6],
        [Family.B3] =  [  0,   0,   4,   1,  29],
        [Family.B4] =  [  0,   0,   0,   0,   7],
        [Family.B5] =  [  0,   0,   0,   0,   2],
        [Family.B6] =  [  0,   1,   2,   0,   3],
        [Family.B7] =  [  4,   4,   4,   4,  16],
        [Family.B8] =  [  0,   0,   0,   0,   1],
        [Family.B9] =  [  3,   0,   0,   0,   5],
        [Family.W1] =  [  0,   0,   0,   0,   7],
        [Family.W2] =  [  0,   2,   0,   0,  19],
        [Family.W3] =  [  0,   0,   0,   0,  15],
        [Family.W4] =  [  0,   0,   0,   0,  17],
        [Family.W5] =  [  0,   0,   6,   2,  22],
        [Family.W6] =  [  0,   0,   0,   0,  10],
        [Family.W7] =  [  0,   0,   2,   0,  22],
        [Family.W8] =  [  0,   1,   1,   0,  12],
        [Family.W9] =  [  0,   0,   0,   0,   7],
        [Family.W10] = [  0,   0,   0,   0,  15],
        [Family.W11] = [  0,   0,   0,   0,  17],
        [Family.P1] =  [  0,   0,   0,   0,  12],
        [Family.P2] =  [  0,   2,   0,   0, 101],
        [Family.P3] =  [  0,   0,   0,   0,   3],
        [Family.S1] =  [  0,   1,   9,   0,  25],
        [Family.S2] =  [  2,   1,  10,   0,  92],
        [Family.S3] =  [  0,   3,   3,   9,  27],
        [Family.S4] =  [  0,  30,   0,   0,   3],
        [Family.S5] =  [  0,   0,   0,   0,  23],
        [Family.S6] =  [  0,   0,   0,   0, 133],
        [Family.R1] =  [  0,   0,  12,   9,  31],
        [Family.R2] =  [  0,  26,  10,   0,  22],
        [Family.R3] =  [  0,   1,  30,   0,  15],
        [Family.R4] =  [  0,   0,   0,   8,   0],
        [Family.R5] =  [  0,   0,   0,   9,   7],
        [Family.I1] =  [  0,  18,   5,   0,   4],
        [Family.I2] =  [  0,  21,  10,   0,   2],
        [Family.I3] =  [  1,  25,   0,   0,   0],
        [Family.I4] =  [  0,  12,   0,   0,   0],
        [Family.E1] =  [  0,   0,   0,   0,   1],
        [Family.E2] =  [  1,   0,   0,   0,   0],
        [Family.F1] =  [  0,   0,   9,   1,  14],
        [Family.F2] =  [  0,   0,   0,   0,   4],
        [Family.F3] =  [  1,   4,   2,   1,   6],
        [Family.F4] =  [  0,   1,   4,   4,  11],
        [Family.F5] =  [  0,   4,   2,   0,   1],
        [Family.F6] =  [  0,   0,   0,   0,   3],
        [Family.C1] =  [  0,   0,   0,   0,  15],
        [Family.M1] =  [  0,   0,   0,   0,  16],
        [Family.N1] =  [  0,   0,   0,   0,   7],
        [Family.N2] =  [  0,   0,   0,   0,  24],
        [Family.N3] =  [  0,   0,   0,   0,  46],
        [Family.D1] =  [  1,   7,   0,   0,   2],
        [Family.D2] =  [  4,   0,   0,   0,   0],
        [Family.D3] =  [  2,   0,   0,   0,   3],
        [Family.X1] =  [  0,  10,   3,   1,  14],
        [Family.X2] =  [  0,  20,   4,   3,  24],
        [Family.X3] =  [  0,   1,   0,   0,   6],
    };

    /// <summary>
    /// Per test assembly, in the order of the columns: the tier's methods, exactly, the suite's methods and executed
    /// cases, and the tier's rows, when this table was last written. Discovery must find at least as many methods, and at
    /// least <see cref="CasesAtLeast"/> of the cases, so the tier's share measured on them is never smaller than its share
    /// of the suite as it is; and at most <see cref="TierRowsAtMost"/> of the tier's rows, every row of a class the tier
    /// carries whole among them, so the ceiling, which reads these rows, never reads fewer than the tier runs by more than
    /// a tenth. A commit that removes test methods, drops more than a tenth of an assembly's cases, or grows its tier's
    /// rows by more than a tenth re-measures that assembly's entry in the same commit, from the sizes the guard prints.
    /// </summary>
    public static readonly (int TierMethods, int Methods, int Cases, int TierRows)[] Sizes =
    [
        (29, 415, 676, 32),      // Jason.App.Tests
        (165, 2780, 5270, 382),  // Jason.Cli.Tests
        (105, 864, 4589, 136),   // Jason.Contracts.Tests
        (48, 302, 479, 54),      // Jason.PluginHost.Tests
        (733, 6291, 12954, 937), // Jason.Runtime.Tests
    ];

    /// <summary>The ceiling of the tier's share of the suite, in per cent, of its methods and of its executed rows.</summary>
    public const int Ceiling = 15;

    /// <summary>The families a table gives no critical case in any assembly, a family it has no row for among them.</summary>
    public static List<Family> FamiliesWithoutACase(IReadOnlyDictionary<Family, int[]> cases) =>
        [.. Enum.GetValues<Family>().Where(family => !cases.TryGetValue(family, out var row) || row.Sum() < 1)];

    /// <summary>
    /// The executed cases an assembly must have, of the ones the table measured: nine tenths, since a platform enumerates
    /// some theories' rows differently, and the methods, which no platform changes, are held exactly.
    /// </summary>
    public static int CasesAtLeast(int measured) => measured - (measured / 10);

    /// <summary>
    /// The tier's rows an assembly may run, of the ones it recorded: a tenth more, so a line added to a page moves nothing,
    /// and the ceiling reads this bound for each assembly, so it holds for whatever the tier may run, the tenth included.
    /// </summary>
    public static int TierRowsAtMost(int recorded) => recorded + (recorded / 10);

    /// <summary>
    /// What is wrong when an assembly's tier runs more than a tenth over the rows it recorded, which the ceiling reads; null
    /// when it does not.
    /// </summary>
    public static string? TierRowsProblem(int discovered, int recorded) =>
        discovered <= TierRowsAtMost(recorded)
            ? null
            : $"the tier runs {discovered} row(s), more than a tenth over the {recorded} it recorded, which the ceiling reads: "
                + "the pages print more commands, or cases were added to a class the tier carries whole; "
                + "re-record the tier's rows, the last of the sizes printed below, in its entry of Sizes, and the ceiling reads the new figure";

    /// <summary>How the guard's failure says an assembly's discovery differs from its column of the table.</summary>
    public const string Differs = "differs from its column of the critical tier's table (a commit that marks, unmarks or removes tests rewrites it; a method of a class the tier carries whole counts once, however many rows it runs)";

    /// <summary>What is wrong when a suite is smaller than the table measured its ceiling on.</summary>
    public static List<string> FloorProblems(int methods, int cases, (int TierMethods, int Methods, int Cases, int TierRows) sizes)
    {
        var problems = new List<string>();
        if (methods < sizes.Methods)
        {
            problems.Add($"the suite has {methods} method(s), fewer than the {sizes.Methods} the table measured the ceiling on");
        }

        if (cases < CasesAtLeast(sizes.Cases))
        {
            problems.Add($"the suite has {cases} case(s), fewer than nine tenths of the {sizes.Cases} the table measured the ceiling on");
        }

        return problems;
    }

    /// <summary>
    /// The tier's rows and the suite's executed rows the ceiling divides, over the sizes of the assemblies: the most each
    /// assembly's tier may run, a tenth over what it recorded, and the least of the suite.
    /// </summary>
    public static (int Tier, int Suite) RowsShare(IEnumerable<(int TierMethods, int Methods, int Cases, int TierRows)> sizes)
    {
        var all = sizes.ToList();
        return (all.Sum(size => TierRowsAtMost(size.TierRows)), all.Sum(size => CasesAtLeast(size.Cases)));
    }

    /// <summary>Whether a tier of this many is at most <see cref="Ceiling"/> per cent of a suite of that many.</summary>
    public static bool WithinCeiling(int tier, int suite) => 100L * tier <= (long)Ceiling * suite;

    private static readonly Lazy<Task<IReadOnlyList<ITestCase>>> Discovered = new(() => Task.Run(Discover));

    /// <summary>Every case this test assembly holds, a theory's rows each a case of its own, discovered once.</summary>
    public static Task<IReadOnlyList<ITestCase>> ThisAssembly() => Discovered.Value;

    public static bool IsCritical(ITestCase testCase) =>
        testCase.Traits.TryGetValue(CriticalAttribute.Trait, out var values) && values.Contains(CriticalAttribute.Value);

    /// <summary>The method a case runs, by its class and name.</summary>
    public static string Method(ITestCase testCase) => $"{testCase.TestClassName}.{testCase.TestMethodName}";

    /// <summary>
    /// Whether a method belongs to a class the tier carries whole, a class a unit can turn red without touching it: in
    /// <c>Jason.Runtime.Tests.Persistence</c>, every class whose name ends in <c>MigrationTests</c>,
    /// <c>MigrationHistoryTests</c>, <c>DatabaseMigratorTests</c>, and the three classes whose only subject is one
    /// migration's <c>Up</c> over a seeded database; in <c>Jason.Cli.Tests.Documentation</c>,
    /// <c>DocumentedPageCommandsTests</c>, except its <c>doctor --output</c> rows, whose lines are the test's own and not a
    /// page's.
    /// </summary>
    /// <remarks>
    /// A name cannot hold the upgrade methods inside the schema classes — a test that migrates to the previous migration,
    /// seeds, and migrates to the head, in a class whose other methods hold the schema at the head — so a unit that adds
    /// one to a schema class marks it itself.
    /// </remarks>
    public static bool CarriedWhole(string @class, string method)
    {
        var dot = @class.LastIndexOf('.');
        var space = dot < 0 ? string.Empty : @class[..dot];
        var name = @class[(dot + 1)..];
        return space switch
        {
            "Jason.Runtime.Tests.Persistence" => name.EndsWith("MigrationTests", StringComparison.Ordinal)
                || name is "MigrationHistoryTests" or "DatabaseMigratorTests" or "LineageBackfillTests" or "BounceStepSchemaTests" or "ResponderWatermarkSchemaTests",
            "Jason.Cli.Tests.Documentation" => name == "DocumentedPageCommandsTests"
                && method != "A_file_a_printed_command_writes_lands_in_this_tests_own_directory",
            _ => false,
        };
    }

    /// <summary>
    /// The critical cases of each family as the table counts them: every case once, except that a method of a class the
    /// tier carries whole counts once for its family however many rows it runs.
    /// </summary>
    /// <remarks>
    /// The rows of such a method are, today, the lines the pages print, which move with every edit of a page; counted
    /// case by case, the table would move with them, and two units that each add a printed command from one base would
    /// merge cleanly into a count neither of them wrote. <see cref="OutsideTheTier"/> holds those classes to the tier
    /// completely, so the exact count adds nothing for them but that churn. The ceiling does not read these counted cases:
    /// it bounds what the tier costs to run, so it reads the tier's rows each assembly records in <see cref="Sizes"/>.
    /// </remarks>
    public static Dictionary<Family, int> Counted(IEnumerable<(string Class, string Method, Family Family)> cases)
    {
        var counted = new Dictionary<Family, int>();
        var whole = new HashSet<(string, string, Family)>();
        foreach (var testCase in cases)
        {
            if (CarriedWhole(testCase.Class, testCase.Method) && !whole.Add(testCase))
            {
                continue;
            }

            counted[testCase.Family] = counted.GetValueOrDefault(testCase.Family) + 1;
        }

        return counted;
    }

    /// <summary>
    /// The methods, by class and name, that a class the tier carries whole holds outside the tier: each method once, in the
    /// order the cases come, however many of its cases are outside.
    /// </summary>
    public static List<string> OutsideTheTier(IEnumerable<(string Class, string Method, bool Critical)> cases) =>
        [.. cases
            .Where(testCase => !testCase.Critical && CarriedWhole(testCase.Class, testCase.Method))
            .Select(testCase => $"{testCase.Class[(testCase.Class.LastIndexOf('.') + 1)..]}.{testCase.Method}")
            .Distinct(StringComparer.Ordinal)];

    /// <summary>Whether a data row's traits, names and values in turn, put it in the tier.</summary>
    public static bool MarksTheTier(string[]? traits) =>
        traits is not null
        && Enumerable.Range(0, traits.Length / 2).Any(pair => traits[2 * pair] == CriticalAttribute.Trait && traits[(2 * pair) + 1] == CriticalAttribute.Value);

    /// <summary>
    /// A mark the sources show: the file and line, the theory a marked row belongs to or the member that marks rows, and
    /// the reason its <c>// Critical:</c> comment gives, empty where it gives none, and the class it sits in: the nearest
    /// <c>class</c> declaration above it.
    /// </summary>
    public sealed record Mark(string File, int Line, string Name, string Reason, string Class);

    /// <summary>The marks of every test source under <c>dotnet/tests</c>, as <see cref="ReadMarks(string, string)"/> reads one.</summary>
    public static (List<Mark> Rows, List<Mark> Members) ReadMarks()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.True(root is not null, $"No directory above {AppContext.BaseDirectory} holds global.json.");
        var rows = new List<Mark>();
        var members = new List<Mark>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root.FullName, "dotnet", "tests"), "*.cs", SearchOption.AllDirectories))
        {
            var file = Path.GetRelativePath(root.FullName, path).Replace('\\', '/');
            if (file.Split('/') is var parts && (parts.Contains("bin") || parts.Contains("obj")))
            {
                continue;
            }

            var (fileRows, fileMembers) = ReadMarks(file, File.ReadAllText(path));
            rows.AddRange(fileRows);
            members.AddRange(fileMembers);
        }

        return (rows, members);
    }

    /// <summary>
    /// The marks one source shows: each <c>InlineData</c> row that carries the tier's trait, by the theory declared below
    /// it, and each member whose body calls <see cref="CriticalRows.Mark"/>, by its name. A row's reason is the
    /// <c>// Critical:</c> comment on the line directly above it, and a member's the one directly above the member; a
    /// call to <c>Mark</c> that is not a member's body of its own, as inside a test, is not a mark.
    /// </summary>
    public static (List<Mark> Rows, List<Mark> Members) ReadMarks(string file, string source)
    {
        var lines = source.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var rows = new List<Mark>();
        var members = new List<Mark>();
        for (var at = 0; at < lines.Length; at++)
        {
            if (lines[at].TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (MarkedRow().IsMatch(lines[at]))
            {
                var theory = lines.Skip(at + 1).Select(line => Theory().Match(line)).FirstOrDefault(match => match.Success);
                rows.Add(new Mark(file, at + 1, theory?.Groups[1].Value ?? string.Empty, ReasonAbove(lines, at), ClassAbove(lines, at)));
                continue;
            }

            if (!lines[at].Contains("CriticalRows.Mark(", StringComparison.Ordinal))
            {
                continue;
            }

            // Up through the member's own lines: never past a blank line or the end of a statement or a block.
            for (var up = at; up >= 0 && lines[up].Trim().Length > 0 && (up == at || !lines[up].TrimEnd().EndsWith(';') && !lines[up].TrimEnd().EndsWith('}')); up--)
            {
                if (Member().Match(lines[up]) is { Success: true } member)
                {
                    members.Add(new Mark(file, at + 1, member.Groups[1].Value, ReasonAbove(lines, up), ClassAbove(lines, up)));
                    break;
                }
            }
        }

        return (rows, members);
    }

    /// <summary>The marked rows of one theory: those under its name in its class, never another class's of the same name.</summary>
    public static List<Mark> RowsOf(IEnumerable<Mark> rows, string @class, string theory) =>
        [.. rows.Where(row => row.Name == theory && row.Class == @class)];

    /// <summary>The nearest <c>class</c> declaration at or above a line; empty where there is none.</summary>
    private static string ClassAbove(string[] lines, int at)
    {
        for (var up = at; up >= 0; up--)
        {
            if (ClassDeclaration().Match(lines[up]) is { Success: true } declared)
            {
                return declared.Groups[1].Value;
            }
        }

        return string.Empty;
    }

    private static string ReasonAbove(string[] lines, int at)
    {
        const string Said = "// Critical:";
        var above = at > 0 ? lines[at - 1].Trim() : string.Empty;
        return above.StartsWith(Said, StringComparison.Ordinal) ? above[Said.Length..].Trim() : string.Empty;
    }

    /// <summary>An <c>InlineData</c> row whose traits put it in the tier.</summary>
    [GeneratedRegex(@"InlineData\(.*\bTraits\s*=.*""Category""\s*,\s*""Critical""")]
    private static partial Regex MarkedRow();

    /// <summary>A class declaration, by its name (a generic class without its parameters).</summary>
    [GeneratedRegex(@"^\s*(?:\w+\s+)*?class\s+(\w+)")]
    private static partial Regex ClassDeclaration();

    /// <summary>A test method's declaration, by its name.</summary>
    [GeneratedRegex(@"^\s*public\s+(?:static\s+)?(?:async\s+)?(?:void|Task|ValueTask)\s+(\w+)\s*\(")]
    private static partial Regex Theory();

    /// <summary>A static member's declaration, a method or a property, by its name.</summary>
    [GeneratedRegex(@"^\s*(?:public|internal|private|protected)\s+(?:\w+\s+)*?static\s+[^=(]*?\b(\w+)\s*(?:\(\s*\)|=>)")]
    private static partial Regex Member();

    /// <summary>
    /// The test framework's own discovery of this assembly, in this process, with the theories enumerated as the runner
    /// enumerates them. It runs on a task of its own, so the context it sets up never reaches the test that asked.
    /// </summary>
    private static async Task<IReadOnlyList<ITestCase>> Discover()
    {
        var options = TestFrameworkOptions.ForDiscovery(new TestAssemblyConfiguration());
        options.SetPreEnumerateTheories(true);

        await using var framework = new XunitTestFramework();
        var discoverer = framework.GetDiscoverer(typeof(CriticalTier).Assembly);
        var found = new List<ITestCase>();
        await discoverer.Find(
            testCase =>
            {
                found.Add(testCase);
                return new ValueTask<bool>(true);
            },
            options);
        return found;
    }
}

/// <summary>
/// The tier each test assembly runs is the one the table pins, and every mark in it gives its reason. Linked into every
/// test project: each assembly holds itself.
/// </summary>
public sealed class CriticalTierTests
{
    /// <summary>
    /// The runner makes each row of a theory a case of its own, which a row's traits need: a theory run as one case
    /// carries its method's traits alone, and every marked row would leave the tier without a sound. The discovery the
    /// table is held to enumerates the rows the same way.
    /// </summary>
    [Theory]
    [InlineData("the one row")]
    [Critical(Family.B7, "a marked row is a case of the tier only where the runner makes each row a case")]
    public void The_runner_makes_each_row_of_a_theory_a_case_of_its_own(string row)
    {
        var running = TestContext.Current.TestCase;

        Assert.True(
            running is not XunitDelayEnumeratedTheoryTestCase,
            $"'{row}' runs inside {running?.GetType().Name}, one case for the whole theory, so no marked row is a case the tier can select.");
    }

    /// <summary>
    /// The critical cases this assembly discovers are the ones its column of the table says, family by family; each names
    /// one family; the tier's methods are the table's; and the suite is at least as large as the table measured it.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a row that leaves the tier, which a run of the tier cannot see, turns this red")]
    public async Task The_tier_this_assembly_runs_is_its_column_of_the_table_family_by_family()
    {
        var name = typeof(CriticalTierTests).Assembly.GetName().Name!;
        var column = Array.IndexOf(CriticalTier.Assemblies, name);
        Assert.True(column >= 0, $"{name} is not a column of the critical tier's table.");

        var cases = await CriticalTier.ThisAssembly();
        var critical = cases.Where(CriticalTier.IsCritical).ToList();

        var problems = new List<string>();
        var named = new List<(string Class, string Method, Family Family)>();
        foreach (var testCase in critical)
        {
            var families = testCase.Traits.TryGetValue(CriticalAttribute.FamilyTrait, out var values) ? values.ToList() : [];
            if (families is [var one] && Enum.GetNames<Family>().Contains(one, StringComparer.Ordinal))
            {
                named.Add((testCase.TestClassName ?? string.Empty, testCase.TestMethodName ?? string.Empty, Enum.Parse<Family>(one)));
            }
            else
            {
                problems.Add($"{testCase.TestCaseDisplayName} names the families [{string.Join(", ", families)}], not one of Family's members");
            }
        }

        var byFamily = CriticalTier.Counted(named);

        foreach (var family in Enum.GetValues<Family>())
        {
            var pinned = CriticalTier.Cases.TryGetValue(family, out var row) && row.Length > column ? row[column] : 0;
            var found = byFamily.GetValueOrDefault(family);
            if (pinned != found)
            {
                problems.Add($"{family}: the table says {pinned} critical case(s), and {found} are discovered");
            }
        }

        var sizes = CriticalTier.Sizes.Length > column ? CriticalTier.Sizes[column] : default;
        var tierMethods = critical.Select(CriticalTier.Method).Distinct(StringComparer.Ordinal).Count();
        var methods = cases.Select(CriticalTier.Method).Distinct(StringComparer.Ordinal).Count();
        if (tierMethods != sizes.TierMethods)
        {
            problems.Add($"the table says the tier has {sizes.TierMethods} method(s), and {tierMethods} are discovered");
        }

        problems.AddRange(CriticalTier.FloorProblems(methods, cases.Count, sizes));

        // Where the rows alone moved, the table is as it was, so the failure does not send its reader to look for marked tests.
        var said = problems.Count == 0 ? $"{name}: its entry of Sizes records fewer of the tier's rows than it runs" : $"{name} {CriticalTier.Differs}";
        if (CriticalTier.TierRowsProblem(critical.Count, sizes.TierRows) is { } rows)
        {
            problems.Add(rows);
        }

        Assert.True(
            problems.Count == 0,
            $"{said}:\n{string.Join('\n', problems)}\n"
            + $"Discovered, as the table writes it: {string.Join(" · ", byFamily.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value}"))}; "
            + $"sizes ({tierMethods}, {methods}, {cases.Count}, {critical.Count}).");
    }

    /// <summary>
    /// Every method of a class the tier carries whole is in the tier, so a migration's or a page's test added without its
    /// mark is named here rather than left for the whole suite to find.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a test of a class the tier carries whole, added without its mark, which a run of the tier cannot see, turns this red")]
    public async Task Every_method_of_a_class_the_tier_carries_whole_is_in_it()
    {
        var cases = await CriticalTier.ThisAssembly();

        var outside = CriticalTier.OutsideTheTier(cases.Select(testCase => (testCase.TestClassName ?? string.Empty, testCase.TestMethodName ?? string.Empty, CriticalTier.IsCritical(testCase))));

        Assert.True(
            outside.Count == 0,
            $"{typeof(CriticalTierTests).Assembly.GetName().Name} holds methods of classes the tier carries whole outside the tier; mark each with [Critical]:\n{string.Join('\n', outside)}");
    }

    /// <summary>
    /// Every mark this assembly runs gives its reason: a method's <see cref="CriticalAttribute"/> its <c>why</c>, a marked
    /// <c>InlineData</c> row the <c>// Critical:</c> comment on the line above it, and a member that marks rows through
    /// <see cref="CriticalRows.Mark"/> the comment above the member. A theory's rows are found in the sources by its name,
    /// and the sources must show as many marked rows under that name as the theory carries, so a row marked in a form this
    /// does not read is named rather than passed over.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a mark without its reason cannot be weighed when the tier is cut")]
    public async Task Every_mark_this_assembly_runs_gives_its_reason()
    {
        var cases = await CriticalTier.ThisAssembly();
        var (rows, members) = CriticalTier.ReadMarks();

        var problems = new List<string>();
        foreach (var method in cases.Where(CriticalTier.IsCritical).Select(testCase => (testCase as IXunitTestCase)?.TestMethod.Method).Distinct())
        {
            if (method is null)
            {
                problems.Add("a critical case is not one of xUnit's own, so its method cannot be read");
                continue;
            }

            var name = $"{method.DeclaringType?.Name}.{method.Name}";
            if (method.GetCustomAttribute<CriticalAttribute>() is { } critical)
            {
                if (string.IsNullOrWhiteSpace(critical.Why))
                {
                    problems.Add($"{name}: its [Critical] gives no reason");
                }

                continue;
            }

            // Marks join on class and name. The examples in CriticalReasonsTests are read too, as class ExampleTests, which no assembly declares, so they never join.
            var declaring = method.DeclaringType?.Name.Split('`')[0] ?? string.Empty;
            var marked = method.GetCustomAttributes<InlineDataAttribute>().Count(data => CriticalTier.MarksTheTier(data.Traits));
            var marks = CriticalTier.RowsOf(rows, declaring, method.Name);
            if (marks.Count != marked)
            {
                problems.Add($"{name}: {marked} of its InlineData rows carry the tier's trait, and the sources show {marks.Count} marked row(s) under its name in its class");
            }

            problems.AddRange(marks.Where(row => row.Reason.Length == 0).Select(row => $"{row.File}:{row.Line}: a marked row of {name} has no `// Critical:` reason on the line above it"));

            var marking = method.GetCustomAttributes<MemberDataAttribute>()
                .SelectMany(data => members.Where(member => member.Name == data.MemberName && member.Class == (data.MemberType ?? method.DeclaringType)?.Name.Split('`')[0]))
                .ToList();
            problems.AddRange(marking.Where(member => member.Reason.Length == 0).Select(member => $"{member.File}:{member.Line}: {member.Name}, which marks rows of {name}, has no `// Critical:` reason above it"));

            if (marked == 0 && marking.Count == 0)
            {
                problems.Add($"{name}: it runs critical cases and carries no [Critical], and the sources show no marked row of it and no member it reads that marks rows");
            }
        }

        Assert.True(
            problems.Count == 0,
            $"{typeof(CriticalTierTests).Assembly.GetName().Name} runs critical cases whose mark gives no reason:\n{string.Join('\n', problems)}");
    }
}
