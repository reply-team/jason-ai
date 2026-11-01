using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The pattern grammar the dialect publishes, held through what a schema author meets: a construct ECMA-262 and the
/// engine read differently is rewritten to ECMA-262's meaning where one can be written, and refused by name where none
/// can; a construct only the engine has is refused by name; and what both read alike runs as written.
/// </summary>
public class PatternGrammarTests
{
    /// <summary>
    /// Every construct the grammar refuses, each in a pattern that is otherwise ordinary. Before the grammar was published,
    /// most of these compiled and meant what only the engine means — a Unicode property, an inline option, a class
    /// subtraction — or something ECMA-262 does not: a class that begins with `]`, an unknown escape read as a letter.
    /// </summary>
    [Theory]
    // Critical: the pattern grammar refuses what both engines disagree on.
    [InlineData(@"^a\b$", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.R3)])]
    [InlineData(@"^\Bx$")]
    [InlineData(@"^\p{L}$")]
    [InlineData(@"^\P{L}$")]
    [InlineData(@"^(a)\k<1>$")]
    [InlineData(@"^\1$")]
    [InlineData(@"^\e$")]
    [InlineData(@"^\a$")]
    [InlineData(@"^\cA$")]
    [InlineData(@"^(?i)a$")]
    [InlineData(@"^(?-i:a)$")]
    [InlineData(@"(?x)^a$ # $")]
    [InlineData(@"^(?#note)a$")]
    [InlineData(@"^(?<name>a)$")]
    [InlineData(@"^(?'name'a)$")]
    [InlineData(@"^(?>a)$")]
    [InlineData(@"^(?(a)a|b)$")]
    [InlineData(@"^(?=a)a$")]
    [InlineData(@"^(?<=a)b$")]
    [InlineData(@"^(?<!a)b$")]
    [InlineData(@"^[a-z-[aeiou]]$")]
    // Critical: the pattern grammar refuses what both engines disagree on.
    [InlineData(@"^[\d-z]$", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.R3)])]
    [InlineData(@"^[a-\w]$")]
    [InlineData(@"^[\D]$")]
    [InlineData(@"^[\W]$")]
    [InlineData(@"^[\S]$")]
    [InlineData(@"^[\b]$")]
    [InlineData(@"^[]a]$")]
    [InlineData(@"^[^]a]$")]
    [InlineData(@"^a{$")]
    [InlineData(@"^a}$")]
    [InlineData(@"^a{,5}$")]
    [InlineData(@"^\x4$")]
    [InlineData(@"^\u004$")]
    [InlineData(@"^\u{41}$")]
    [InlineData(@"^\01$")]
    [InlineData(@"^\q$")]
    [InlineData(@"^\@$")]
    [InlineData(@"^a\")]
    [InlineData(@"^[abc$")]
    [InlineData(@"^\A$")]
    [InlineData(@"^a\Z")]
    public void A_construct_the_grammar_refuses_is_refused(string pattern)
    {
        var problem = Assert.Single(SchemaValidator.CheckDialect(Patterned(pattern)));

        Assert.Equal("schema_pattern_invalid", problem.Reason);
        Assert.Equal("/pattern", problem.Pointer);
    }

    /// <summary>
    /// `\d` is ECMA-262's ten ASCII digits, where the engine's is every script's: a digit of another script is refused,
    /// in a class too, and the complement takes it.
    /// </summary>
    [Fact]
    public void A_digit_is_one_of_the_ten_ascii_digits()
    {
        var arabicIndicThree = Text(0x0663);

        Assert.Empty(CheckPattern(@"^\d$", "3"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^\d$", arabicIndicThree)).Reason);
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^[\d]$", arabicIndicThree)).Reason);
        Assert.Empty(CheckPattern(@"^\D$", arabicIndicThree));
    }

    /// <summary>`\w` is ECMA-262's ASCII letters, digits and underscore, where the engine's takes every script's letters.</summary>
    [Fact]
    public void A_word_character_is_an_ascii_letter_digit_or_underscore()
    {
        Assert.Empty(CheckPattern(@"^\w+$", "Az09_"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^\w$", Text(0x00E9))).Reason);
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^[\w]$", Text(0x00E9))).Reason);
        Assert.Empty(CheckPattern(@"^\W$", Text(0x00E9)));
    }

    /// <summary>
    /// `\s` is ECMA-262's white space and line terminators: the byte order mark is one and the next-line control is not,
    /// the other way round from the engine's class.
    /// </summary>
    [Fact]
    public void White_space_is_ecma_262s_with_the_byte_order_mark_in_and_next_line_out()
    {
        var byteOrderMark = Text(0xFEFF);
        var nextLine = Text(0x0085);

        Assert.Empty(CheckPattern(@"^\s$", " "));
        Assert.Empty(CheckPattern(@"^\s$", Text(0x3000)));
        Assert.Empty(CheckPattern(@"^\s$", Text(0x2028)));
        Assert.Empty(CheckPattern(@"^\s$", byteOrderMark));
        Assert.Empty(CheckPattern(@"^[\s]$", byteOrderMark));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^\s$", nextLine)).Reason);
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^\S$", byteOrderMark)).Reason);
        Assert.Empty(CheckPattern(@"^\S$", nextLine));
    }

    /// <summary>What both readings share runs as written: groups, counted and lazy quantifiers, codes, escaped syntax characters.</summary>
    [Theory]
    [InlineData(@"^(?:ab)+$", "abab")]
    [InlineData(@"^a{2,3}?$", "aaa")]
    [InlineData(@"^a{2,}$", "aaaa")]
    [InlineData(@"^\x41\u0042$", "AB")]
    [InlineData(@"^[\-\]\[]+$", "-][")]
    [InlineData(@"^\.\/\{\}\|$", "./{}|")]
    [InlineData(@"^[a-z0-9]+$", "abc123")]
    [InlineData(@"^[$.^(|*]+$", "$.^(|*")]
    [InlineData(@"^[a-]$", "-")]
    [InlineData(@"^\t$", "\t")]
    [InlineData(@"^a]$", "a]")]
    public void What_both_readings_share_runs_as_written(string pattern, string value)
    {
        Assert.Empty(SchemaValidator.CheckDialect(Patterned(pattern)));
        Assert.Empty(CheckPattern(pattern, value));
    }

    /// <summary>
    /// A refusal of a pattern names the construct and where it stands, and never the pattern: a pattern comes from
    /// whoever wrote the schema — a plugin's manifest, a work item's result format — and the refusal travels to answers
    /// and records that are not theirs to fill. The construct is the few characters the table names, nothing around them.
    /// </summary>
    [Theory]
    // Critical: a refusal never quotes the pattern.
    [InlineData(@"^SENTINEL_PAT(?i)x$", "(?i", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.B3)])]
    [InlineData(@"^SENTINEL_PAT\p{L}$", @"\p")]
    [InlineData(@"^SENTINEL_PAT[]x]$", "[]")]
    [InlineData(@"^SENTINEL_PAT\Z", @"\Z")]
    public void A_refusal_names_the_construct_and_where_it_stands_and_never_the_pattern(string pattern, string construct)
    {
        var problem = Assert.Single(SchemaValidator.CheckDialect(Patterned(pattern)));

        Assert.DoesNotContain("SENTINEL_PAT", problem.Message, StringComparison.Ordinal);
        Assert.Contains($"`{construct}` at character 14", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>A pattern the engine cannot compile — a group never closed — is refused without being quoted either.</summary>
    [Fact]
    public void A_pattern_that_does_not_compile_is_refused_without_being_quoted()
    {
        var problem = Assert.Single(SchemaValidator.CheckDialect(Patterned("^SENTINEL_PAT(x$")));

        Assert.Equal("schema_pattern_invalid", problem.Reason);
        Assert.DoesNotContain("SENTINEL_PAT", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>A pattern the load-time probe refuses for its time is refused without being quoted.</summary>
    [Fact]
    public void A_pattern_the_probe_refuses_is_refused_without_being_quoted()
    {
        var problem = Assert.Single(SchemaValidator.ProbePatterns(Patterned("^SENTINEL_PATx$"), new NeverFinishes()));

        Assert.Equal("schema_pattern_invalid", problem.Reason);
        Assert.Contains("pattern_timed_out", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL_PAT", problem.Message, StringComparison.Ordinal);
    }

    private static string Text(int code) => ((char)code).ToString();

    private static JsonObject Patterned(string pattern) => new() { ["type"] = "string", ["pattern"] = pattern };

    /// <summary>A timing under which no match ever finishes.</summary>
    private sealed class NeverFinishes : PatternTiming
    {
        public override bool IsMatch(System.Text.RegularExpressions.Regex pattern, string value) =>
            throw new System.Text.RegularExpressions.RegexMatchTimeoutException(value, "probe", TimeSpan.FromMilliseconds(SchemaValidator.PatternTimeoutMilliseconds));
    }

    private static IReadOnlyList<SchemaProblem> CheckPattern(string pattern, string value) =>
        SchemaValidator.Validate(JsonValue.Create(value), Patterned(pattern));
}
