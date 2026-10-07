using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Doctor;
using Jason.Contracts.Operations;

namespace Jason.Runtime.Tests.Patterns;

/// <summary>
/// No regular expression of the runtime holds an anchor that lets a line break through. An unescaped <c>$</c> outside a
/// character class, and <c>\Z</c>, also match just before a final line break, so a pattern written with either accepts a
/// value followed by one — at the end of the pattern, before a closing group or between alternatives alike — and the
/// pattern that comes next would carry the same defect unless something refuses it. The end of the value is <c>\z</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its reach is the source text of every production project: a <c>[GeneratedRegex(</c>, a <c>new Regex(</c>, a
/// <c>new(</c> assigned to something declared a <c>Regex</c>, and the static <c>Regex.IsMatch</c>, <c>Match</c>,
/// <c>Matches</c>, <c>Replace</c>, <c>Split</c>, <c>Count</c>, <c>EnumerateMatches</c> and <c>EnumerateSplits</c>,
/// whose pattern is their second argument. A pattern is read as the string literal it is written as, quoted, verbatim or
/// raw. One written any other way — a constant, pieces joined, a call — is not passed over: it fails the scan, naming
/// its file, its line and what was written there, unless it is one of the few built at run time by code that answers
/// for its own anchors (<see cref="BuiltAtRunTime"/>). A marker named in a comment is read as a marker, so a
/// commented-out pattern is held to the rule too.
/// </para>
/// <para>
/// A pattern is read for its anchors as the schema validator's rewrite reads one: a backslash takes the character after
/// it as written, inside a class <c>$</c> is a member, and a <c>]</c> that comes first in a class is a member too rather
/// than its end.
/// </para>
/// <para>
/// What the source does not always show — a <c>new(</c> whose type is not written beside it, in an expression body, a
/// <c>return</c> or a second declarator — the compiled assemblies do. Every <c>Regex</c> their code constructs, and
/// every call of its static methods that take a pattern, is found in their IL, read from their files without loading
/// them, and given a file and line by their symbols; each has to be one the scan read there, so a pattern written in a
/// form the scan does not know fails by its file and line instead of being passed over.
/// </para>
/// </remarks>
public sealed class PatternAnchorGuardTests
{
    /// <summary>
    /// How many literal patterns the scan reads in the tree today, as a floor: a scan that read fewer was not reading
    /// the tree it was pointed at, or has stopped reading a form it used to.
    /// </summary>
    private const int PatternFloor = 46;

    private static readonly Regex Marker = new(
        @"\[GeneratedRegex\(|new\s+(?:System\.Text\.RegularExpressions\.)?Regex\s*\(|\bRegex\??\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:\{[^{}]*\}\s*)?=\s*new\s*\(|(?<![A-Za-z0-9_.])(?:System\.Text\.RegularExpressions\.)?Regex\s*\.\s*(?<static>IsMatch|Match|Matches|Replace|Split|Count|EnumerateMatches|EnumerateSplits)\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    /// <summary>
    /// The patterns written as something other than a literal that the guard knows and lets through, each by its file
    /// and exactly what is written there, with the reason its anchors are already answered for.
    /// </summary>
    private static readonly (string File, string Argument, string Reason)[] BuiltAtRunTime =
    [
        ("SchemaValidator.cs", "form", "a published schema's pattern, every anchoring $ of which the validator rewrites as \\z and every . outside a class as a class of all but the four line terminators before it compiles it, and which it refuses outright if it holds \\A, \\Z, \\z or \\G"),
        ("IdentityRewriter.cs", "Pattern(facts, shortForm, AfterName, AfterPath, withSid: true)", "names to find anywhere in a text, escaped and joined with this file's own fragments, which anchors nowhere"),
        ("IdentityRewriter.cs", "Pattern(facts, shortForm, BeforeEscapedPair, BeforeEscapedPair, withSid: false)", "the same names, each found only before a percent escape, which anchors nowhere either"),
    ];

    [Fact]
    public void No_pattern_of_the_runtime_holds_an_anchor_that_lets_a_line_break_through()
    {
        var root = RepositoryRoot();
        var found = new List<Pattern>();

        foreach (var file in SourceFiles(root))
        {
            found.AddRange(PatternsIn(File.ReadAllText(file), Path.GetRelativePath(root, file)));
        }

        var literals = found.Where(pattern => pattern.Literal).ToList();

        // A scan that read nothing would pass for the wrong reason: it must have found as many patterns as the tree
        // holds, of each kind of literal and each form, in the files that are known to hold them.
        Assert.True(literals.Count >= PatternFloor, $"Only {literals.Count} patterns were read, fewer than the {PatternFloor} the tree holds; the source tree was not found or was not read.");
        AssertFound(literals, "ManifestRules.cs", @"^\d+\.\d+\.\d+\z");
        AssertFound(literals, "PluginIds.cs", @"^[a-z][a-z0-9-]{1,63}\z");
        AssertFound(literals, "OutcomeValidator.cs", @"^[a-z][a-z0-9_]{0,63}\z");
        AssertFound(literals, "SnakeCase.cs", "([a-z0-9])([A-Z])");
        AssertFound(literals, "ExecutableResolver.cs", "^endLocal & goto #_undefined_# 2>NUL");

        var problems = new List<string>();
        var unread = found
            .Where(pattern => !pattern.Literal && !BuiltAtRunTime.Any(known => Path.GetFileName(pattern.File) == known.File && pattern.Text == known.Argument))
            .Select(pattern => $"{pattern.File}({pattern.Line}): {pattern.Text}")
            .ToList();
        if (unread.Count > 0)
        {
            problems.Add($"{unread.Count} pattern(s) are not written as a literal, so their anchors cannot be read; write each as one, or teach the guard why its anchors are already answered for:{Environment.NewLine}{string.Join(Environment.NewLine, unread)}");
        }

        var stale = BuiltAtRunTime
            .Where(known => !found.Any(pattern => !pattern.Literal && Path.GetFileName(pattern.File) == known.File && pattern.Text == known.Argument))
            .Select(known => $"{known.File}: {known.Argument}, let through as {known.Reason}")
            .ToList();
        if (stale.Count > 0)
        {
            problems.Add($"{stale.Count} pattern(s) the guard lets through are no longer where they were, so the leave each was given is spent:{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
        }

        var compiled = CompiledCalls.ProductionAssemblies(root)
            .SelectMany(assembly => CompiledCalls.In(assembly))
            .Where(call => TakesAPattern(call.Target))
            .ToList();
        Assert.Contains(compiled, call => call.File?.EndsWith("/SchemaValidator.cs", StringComparison.Ordinal) == true);
        var unscanned = Unscanned(compiled, found);
        if (unscanned.Count > 0)
        {
            problems.Add($"{unscanned.Count} regular expression(s) the compiled runtime makes are not ones the scan read in its source, so their anchors were never read; write each where the scan reads it, as a literal after a marker it knows:{Environment.NewLine}{string.Join(Environment.NewLine, unscanned)}");
        }

        var offences = literals
            .Where(pattern => LineBreakAnchors(pattern.Text).Count > 0)
            .Select(pattern => $"{pattern.File}({pattern.Line}): {pattern.Text} (holds {string.Join(" and ", LineBreakAnchors(pattern.Text).Distinct())})")
            .ToList();
        if (offences.Count > 0)
        {
            problems.Add($"{offences.Count} pattern(s) hold an unescaped '$' or '\\Z' outside a class, which also matches before a final line break; end them with \\z:{Environment.NewLine}{string.Join(Environment.NewLine, offences)}");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, problems));
    }

    /// <summary>
    /// The rewriter's patterns are let through because they anchor nowhere, and that is held here rather than taken on
    /// trust: built for names that hold a dollar sign and the letter Z, what either of its searches compiles has no anchor
    /// of either kind.
    /// </summary>
    [Theory]
    [InlineData("_names")]
    [InlineData("_beforeEscapes")]
    public void The_patterns_the_identity_rewriter_builds_anchor_nowhere(string field)
    {
        var facts = new IdentityFacts(@"C:\Users\Z$a", "Z$a", "Z$MACHINE", "Z$DOMAIN", "S-1-5-21-1-2-3-1001") { DataDirectory = @"D:\Z$data" };
        var rewriter = new IdentityRewriter(facts, directory => directory + "$");
        var names = typeof(IdentityRewriter).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(rewriter);

        var pattern = Assert.IsType<Regex>(names).ToString();

        Assert.Contains(@"Z\$a", pattern, StringComparison.Ordinal);
        Assert.Empty(LineBreakAnchors(pattern));
    }

    /// <summary>
    /// The validator is let through because it answers for the anchors of every pattern it is handed, and that is held
    /// here rather than taken on trust. It rewrites the <c>$</c> that anchors so that it ends the value and nothing
    /// else, and the <c>.</c> outside a class so that it stops at each of the four line terminators ECMA-262 names.
    /// </summary>
    [Fact]
    public void The_schema_validator_rewrites_a_dollar_and_a_dot_as_ecma_262_reads_them()
    {
        var dollar = new JsonObject { ["type"] = "string", ["pattern"] = "^[a-z]+$" };
        Assert.Empty(SchemaValidator.Validate(JsonValue.Create("abc"), dollar));
        Assert.Equal("pattern", Assert.Single(SchemaValidator.Validate(JsonValue.Create("abc\n"), dollar)).Reason);

        var dot = new JsonObject { ["type"] = "string", ["pattern"] = "^.+$" };
        Assert.Empty(SchemaValidator.Validate(JsonValue.Create("abc"), dot));
        foreach (var terminator in new[] { "\n", "\r", "\u2028", "\u2029" })
        {
            Assert.Equal("pattern", Assert.Single(SchemaValidator.Validate(JsonValue.Create("abc" + terminator), dot)).Reason);
        }
    }

    /// <summary>
    /// The same, for the four escapes <c>\A</c>, <c>\Z</c>, <c>\z</c> and <c>\G</c>: the validator refuses them, in a
    /// class and out of one, so no pattern it runs holds an anchor of the engine's that the schema's author wrote as
    /// an escape.
    /// </summary>
    [Theory]
    [InlineData(@"\A")]
    [InlineData(@"\Z")]
    [InlineData(@"\z")]
    [InlineData(@"\G")]
    public void The_schema_validator_refuses_the_escapes_the_two_dialects_read_differently(string escape)
    {
        foreach (var pattern in new[] { $"^[a-z]+{escape}", $"^[{escape}]+$" })
        {
            var schema = new JsonObject { ["type"] = "string", ["pattern"] = pattern };

            Assert.Equal("schema_pattern_invalid", Assert.Single(SchemaValidator.CheckDialect(schema)).Reason);
            Assert.Equal("schema_pattern_invalid", Assert.Single(SchemaValidator.Validate(JsonValue.Create("abc"), schema)).Reason);
        }
    }

    /// <summary>
    /// What the validator does with a dollar, a dot and the four escapes is what the dialect's page tells a schema's
    /// author: the grammar's own rows for them stand in the page's table as the grammar renders them. The whole table is
    /// held row by row where the grammar lives; this keeps the three this guard is about in view here.
    /// </summary>
    [Fact]
    public void The_dialect_page_publishes_the_grammars_rows_for_a_dollar_a_dot_and_the_four_escapes()
    {
        var page = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "contracts", "README.md"));
        var rows = PatternGrammar.Table()
            .Where(line => line.StartsWith("| `$` |", StringComparison.Ordinal)
                || line.StartsWith("| `.` |", StringComparison.Ordinal)
                || line.StartsWith(@"| `\A`", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Contains(row, page));
    }

    /// <summary>
    /// The scan is only worth what its reader is worth. This is the reader held to the forms a pattern is written in,
    /// and to the escapes and classes that make a dollar sign or a Z a character rather than an anchor.
    /// </summary>
    [Theory]
    [InlineData("[GeneratedRegex(\"^a$\")]", true)]
    [InlineData("[GeneratedRegex(@\"^a$\")]", true)]
    [InlineData("[GeneratedRegex(\"\"\"^a$\"\"\")]", true)]
    [InlineData("[GeneratedRegex(\"\"\"\n    ^a \"quoted\" $\n    \"\"\")]", true)]
    [InlineData("new Regex(\"^a$\", RegexOptions.CultureInvariant)", true)]
    [InlineData("new Regex(@\"^a$\")", true)]
    [InlineData("new System.Text.RegularExpressions.Regex(\"^a$\")", true)]
    [InlineData("private static readonly Regex Name = new(\"^a$\");", true)]
    [InlineData("Regex? name = new(@\"^a$\", RegexOptions.None);", true)]
    [InlineData("public static Regex Name { get; } = new(\"^a$\");", true)]
    [InlineData("Regex.IsMatch(value, \"^a$\")", true)]
    [InlineData("Regex.Match(value, @\"^a$\", RegexOptions.None)", true)]
    [InlineData("Regex.Matches(value, \"^a$\")", true)]
    [InlineData("Regex.Replace(value, \"^a$\", \"$1\")", true)]
    [InlineData("Regex.Split(value, \"^a$\")", true)]
    [InlineData("Regex.Count(value, \"^a$\")", true)]
    [InlineData("Regex.EnumerateMatches(value, \"^a$\")", true)]
    [InlineData("Regex.EnumerateSplits(value, \"^a$\")", true)]
    [InlineData("System.Text.RegularExpressions.Regex.IsMatch(value, \"^a$\")", true)]
    [InlineData("[GeneratedRegex(\n    @\"^a$\")]", true)]
    [InlineData("[GeneratedRegex(\"^a\\\\\\\\$\")]", true)]
    [InlineData("[GeneratedRegex(@\"^a\\\\$\")]", true)]
    [InlineData("[GeneratedRegex(@\"^(a|b$)\")]", true)]
    [InlineData("[GeneratedRegex(@\"^(a$)\")]", true)]
    [InlineData("[GeneratedRegex(@\"^a$|^b\\z\")]", true)]
    [InlineData("[GeneratedRegex(@\"^$a\")]", true)]
    [InlineData("[GeneratedRegex(@\"^a\\Z\")]", true)]
    [InlineData("[GeneratedRegex(@\"^(a\\Z|b)\\z\")]", true)]
    [InlineData("[GeneratedRegex(@\"^[]$]$\")]", true)]
    [InlineData("[GeneratedRegex(@\"^a\\z\")]", false)]
    [InlineData("[GeneratedRegex(\"\"\"^a\\z\"\"\")]", false)]
    [InlineData("[GeneratedRegex(@\"^a\\$\")]", false)]
    [InlineData("[GeneratedRegex(\"^a\\\\$\")]", false)]
    [InlineData("[GeneratedRegex(@\"^[A-Za-z_$][A-Za-z0-9_$]*\\z\")]", false)]
    [InlineData("[GeneratedRegex(@\"^[^$]+\\z\")]", false)]
    [InlineData("[GeneratedRegex(@\"^[]$]+\\z\")]", false)]
    [InlineData("[GeneratedRegex(@\"^[\\]$]+\\z\")]", false)]
    [InlineData("[GeneratedRegex(@\"^a\\\\Z\")]", false)]
    [InlineData("[GeneratedRegex(@\"^[A-Z]+\\z\")]", false)]
    [InlineData("[GeneratedRegex(@\"\\s+\")]", false)]
    [InlineData("Regex.Replace(value, @\"\\s+\", \"$1 $\")", false)]
    public void The_reader_finds_an_anchor_that_lets_a_line_break_through_and_nothing_that_does_not(string source, bool offends)
    {
        var pattern = Assert.Single(PatternsIn(source, "Anywhere.cs"));

        Assert.True(pattern.Literal, $"The pattern was not read as a literal: {pattern.Text}");
        Assert.Equal(offends, LineBreakAnchors(pattern.Text).Count > 0);
    }

    /// <summary>A pattern not written as a literal is reported, with what is written there for a failure to name.</summary>
    [Theory]
    [InlineData("new Regex(EcmaForm(pattern), RegexOptions.NonBacktracking)", "EcmaForm(pattern)")]
    [InlineData("[GeneratedRegex(Pattern)]", "Pattern")]
    [InlineData("Regex.IsMatch(value, Held)", "Held")]
    [InlineData("Regex.IsMatch(value, \"^a\" + \"$\")", "\"^a\" + \"$\"")]
    [InlineData("Regex.IsMatch(value, $\"^{a}$\")", "$\"^{a}$\"")]
    [InlineData("private static readonly Regex Name = new(Held);", "Held")]
    [InlineData("Regex.IsMatch(value, pattern: \"^a$\")", "pattern: \"^a$\"")]
    public void A_pattern_the_reader_cannot_read_is_reported_with_what_is_written_there(string source, string written)
    {
        var pattern = Assert.Single(PatternsIn(source, "Anywhere.cs"));

        Assert.False(pattern.Literal);
        Assert.Equal(written, pattern.Text);
    }

    /// <summary>Calls that only share a name — an instance's method, a member called Regex, another type — are not patterns.</summary>
    [Theory]
    [InlineData("regex.IsMatch(value)")]
    [InlineData("options.Regex.IsMatch(value)")]
    [InlineData("MyRegex.Replace(value, \"$1\")")]
    [InlineData("Regex.Escape(value)")]
    [InlineData("private static readonly Matcher Name = new(\"^a$\");")]
    public void A_call_that_only_shares_a_name_is_not_read_as_a_pattern(string source) =>
        Assert.Empty(PatternsIn(source, "Anywhere.cs"));

    [Fact]
    public void The_reader_gives_the_pattern_as_the_regular_expression_engine_sees_it_and_the_line_it_starts_on()
    {
        var source = "class A\n{\n    [GeneratedRegex(\"^a\\\\.b\\\"c$\")]\n    static partial Regex One();\n\n    [GeneratedRegex(@\"^a\"\"b\\.$\")]\n    static partial Regex Two();\n\n    bool Three(string v) => Regex.IsMatch(\n        v, \"^c\\\\z\");\n}";

        var patterns = PatternsIn(source, "A.cs").ToList();

        Assert.Equal([(3, "^a\\.b\"c$"), (6, "^a\"b\\.$"), (9, "^c\\z")], patterns.Select(pattern => (pattern.Line, pattern.Text)));
    }

    /// <summary>
    /// The scan's reach is held by the compiled code: a target-typed <c>new(</c> in an expression body, which the source
    /// scan does not read, is found in this file's own assembly by its file and line.
    /// </summary>
    [Fact]
    public void A_pattern_the_source_scan_does_not_read_is_found_in_the_compiled_assembly_by_its_file_and_line()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "runtime", "tests", "Jason.Runtime.Tests", "Patterns", "PatternAnchorGuardTests.cs");
        var source = File.ReadAllText(path);
        var file = Path.GetRelativePath(root, path).Replace('\\', '/');
        var compiled = CompiledCalls.In(typeof(PatternAnchorGuardTests).Assembly.Location, type => type == typeof(PatternAnchorGuardTests).FullName)
            .Where(call => TakesAPattern(call.Target))
            .ToList();

        var unscanned = Unscanned(compiled, [.. PatternsIn(source, file)]);

        // The declaration is looked for in pieces, so the text looked for is not also the text of this lookup.
        Assert.Equal([$"{file}({CSharpSource.LineOf(source, source.IndexOf("Regex " + nameof(Unwritten) + "(", StringComparison.Ordinal))}): Regex..ctor"], unscanned);
    }

    /// <summary>A pattern made where the source scan does not look: a target-typed <c>new(</c> in an expression body.</summary>
    private static Regex Unwritten() => new("^unread$");

    /// <summary>Whether a call makes a regular expression from a pattern: <c>Regex</c>'s constructor, or one of its static methods that take one.</summary>
    private static bool TakesAPattern(CallTarget target) =>
        target is { Namespace: "System.Text.RegularExpressions", Type: "Regex" }
        && (target.Member == ".ctor"
            || (!target.IsInstance && target.Member is "IsMatch" or "Match" or "Matches" or "Replace" or "Split" or "Count" or "EnumerateMatches" or "EnumerateSplits"));

    /// <summary>
    /// The compiled calls the scan did not read: each call's statement, by the lines the symbols give it, has to hold as
    /// many patterns the scan found as it holds calls. A call the symbols give no line is never read.
    /// </summary>
    private static List<string> Unscanned(IEnumerable<CompiledCall> calls, List<Pattern> found) =>
        [.. calls
            .GroupBy(call => (call.File, call.StartLine, call.EndLine))
            .Where(statement => statement.Key.File is null
                || found.Count(pattern => string.Equals(pattern.File.Replace('\\', '/'), statement.Key.File, StringComparison.OrdinalIgnoreCase)
                    && pattern.Line >= statement.Key.StartLine
                    && pattern.Line <= statement.Key.EndLine) < statement.Count())
            .SelectMany(statement => statement.Select(call => $"{call.File ?? call.Method}({call.StartLine}): {call.Target.Type}.{call.Target.Member}"))];

    private static void AssertFound(List<Pattern> found, string file, string start) =>
        Assert.True(
            found.Any(pattern => Path.GetFileName(pattern.File) == file && pattern.Text.StartsWith(start, StringComparison.Ordinal)),
            $"The scan did not find the pattern {start} in {file}.");

    /// <summary>
    /// The anchors in a pattern that also match before a final line break: each <c>$</c> and each <c>\Z</c> that no
    /// backslash makes a character and no class makes a member. One pass, read as the schema validator's rewrite reads
    /// a pattern: a backslash takes the character after it as written, so <c>\$</c> is a dollar and <c>\\$</c> is a
    /// backslash and then an anchor; inside a class <c>$</c> is a member, and a <c>]</c> that comes first in a class
    /// (<c>[]a]</c>, <c>[^]a]</c>) is a member too rather than its end.
    /// </summary>
    private static List<string> LineBreakAnchors(string pattern)
    {
        var anchors = new List<string>();
        var inClass = false;
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '\\' && index + 1 < pattern.Length)
            {
                if (!inClass && pattern[index + 1] == 'Z')
                {
                    anchors.Add(@"\Z");
                }

                index++;
                continue;
            }

            if (!inClass && character == '[')
            {
                inClass = true;
                if (index + 1 < pattern.Length && pattern[index + 1] == '^')
                {
                    index++;
                }

                if (index + 1 < pattern.Length && pattern[index + 1] == ']')
                {
                    index++;
                }

                continue;
            }

            if (inClass && character == ']')
            {
                inClass = false;
            }
            else if (!inClass && character == '$')
            {
                anchors.Add("$");
            }
        }

        return anchors;
    }

    /// <summary>Every pattern a source writes after one of the markers, with the line its marker is on.</summary>
    private static IEnumerable<Pattern> PatternsIn(string source, string file)
    {
        foreach (var marker in Marker.Matches(source).Cast<Match>())
        {
            yield return PatternOf(source, file, marker);
        }
    }

    /// <summary>
    /// The pattern a marker introduces: the literal its pattern argument is — the first, or the second for a static
    /// method, whose first is the input — as the regular expression engine receives it, or, where that argument is not a
    /// literal, exactly what is written there, so a failure can name the file, the line and the text.
    /// </summary>
    private static Pattern PatternOf(string source, string file, Match marker)
    {
        var line = CSharpSource.LineOf(source, marker.Index);
        var position = 0;
        var at = marker.Index + marker.Length;
        if (marker.Groups["static"].Success)
        {
            at = NextArgument(source, at);
            position = 1;
        }

        while (at < source.Length && char.IsWhiteSpace(source[at]))
        {
            at++;
        }

        if (at < source.Length && CSharpSource.Literal(source, at, out var end) is { } text && ArgumentEndsAt(source, end))
        {
            return new Pattern(file, line, text, Literal: true);
        }

        var argument = ArgumentText(source, at);
        return new Pattern(file, line, argument.Length > 0 ? argument : $"(argument {position + 1} not found)", Literal: false);
    }

    /// <summary>The index just past the comma that ends the argument starting at the index, or the end of the text.</summary>
    private static int NextArgument(string source, int at)
    {
        var end = CSharpSource.ArgumentEnd(source, at);
        return end < source.Length && source[end] == ',' ? end + 1 : source.Length;
    }

    /// <summary>The text of the argument starting at the index, trimmed, up to the comma or bracket that ends it.</summary>
    private static string ArgumentText(string source, int at) =>
        at >= source.Length ? string.Empty : source[at..CSharpSource.ArgumentEnd(source, at)].Trim();

    /// <summary>Whether nothing but whitespace stands between the index and the comma or bracket that ends an argument.</summary>
    private static bool ArgumentEndsAt(string source, int at)
    {
        while (at < source.Length && char.IsWhiteSpace(source[at]))
        {
            at++;
        }

        return at < source.Length && source[at] is ',' or ')' or ']';
    }

    /// <summary>Every C# file of every production project, found from the repository's own marker.</summary>
    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "runtime", "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>A pattern the scan found: where, and its text — the literal as the engine sees it, or what is written there instead.</summary>
    private sealed record Pattern(string File, int Line, string Text, bool Literal);
}
