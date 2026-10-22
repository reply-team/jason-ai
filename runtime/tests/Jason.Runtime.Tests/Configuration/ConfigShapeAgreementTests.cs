using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Cli.Doctor;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// What the command-line client says about a settings file, held to what the runtime does with the same file. The
/// client describes the file without the runtime, with its own parser set to the same tolerance, so the two agree
/// only as long as a test makes them: a file one reads and the other refuses would send a person looking for the
/// wrong problem, and a position the two number differently would send them to the wrong line.
/// </summary>
public sealed partial class ConfigShapeAgreementTests
{
    private static readonly IReadOnlySet<string> NoPlugins = new HashSet<string>();

    public enum Outcome
    {
        /// <summary>The runtime reads it.</summary>
        Read,

        /// <summary>The runtime's JSON reader stops at a position.</summary>
        NotJson,

        /// <summary>It is JSON, and the runtime's provider refuses it anyway.</summary>
        Refused,
    }

    public static TheoryData<string, Outcome> Files() => new()
    {
        { "{}", Outcome.Read },
        { """{"Dispatcher":{"MaxParallel":2}}""", Outcome.Read },
        { "{\n  // a note\n  \"Dispatcher\": { \"MaxParallel\": 2, },\n  /* another */\n}", Outcome.Read },
        { """{"Roles":{"DefaultEntryCommand":["a","b",],},}""", Outcome.Read },
        { """{"Update":{"FeedUrl":null,"IntervalHours":[]}}""", Outcome.Read },
        { "\uFEFF{\"Dispatcher\":{}}", Outcome.Read },

        { "", Outcome.NotJson },
        { "  \n ", Outcome.NotJson },
        { "{", Outcome.NotJson },
        { """{"a" 1}""", Outcome.NotJson },
        { "{\n  \"Update\": {\n    \"FeedUrl\": \"x\",\n    \"IntervalHours\": ]\n  }\n}", Outcome.NotJson },
        { "{\r\n  \"a\": 1,\r\n  \"b\": ]\r\n}", Outcome.NotJson },
        { "{\t\"a\":\t01}", Outcome.NotJson },
        { """{"é": ]""", Outcome.NotJson },
        { "{\"日本語\": 1, \"b\": ]}", Outcome.NotJson },
        { "{'a': 1}", Outcome.NotJson },
        { "{Dispatcher: 1}", Outcome.NotJson },
        { """{"a": 1} trailing""", Outcome.NotJson },
        { """{"a": "unterminated""", Outcome.NotJson },
        { "[1,2", Outcome.NotJson },
        { "\uFEFF{\"a\": ]", Outcome.NotJson },

        // Valid JSON, and not a settings file: the shape has to say what it is instead.
        { "[]", Outcome.Refused },
        { "\"x\"", Outcome.Refused },
        { "42", Outcome.Refused },
        { "null", Outcome.Refused },
        { """{"Update":{"IntervalHours":1,"IntervalHours":2}}""", Outcome.Refused },
        { """{"Dispatcher":{"MaxParallel":2},"dispatcher":{"MaxParallel":3}}""", Outcome.Refused },
    };

    // Critical: the doctor parser and the runtime agree.
    public static TheoryData<string, Outcome> CriticalFiles() =>
        CriticalRows.Mark(Files(), Family.S4, data => data is ["{}", Outcome.Read]);

    [Theory]
    [MemberData(nameof(CriticalFiles))]
    public void The_client_and_the_runtime_agree_on_whether_the_file_is_json_and_on_where_it_stops(string text, Outcome expected)
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, text);

        var (outcome, line, column) = RuntimeReads(dir);
        var described = ConfigShape.Describe(text, SettingsAllowlist.Default, NoPlugins);

        Assert.Equal(expected, outcome);
        Assert.Equal(outcome != Outcome.NotJson, described["parses"]!.GetValue<bool>());
        if (outcome == Outcome.NotJson)
        {
            Assert.Equal(line, described["error"]!["line"]!.GetValue<long>());
            Assert.Equal(column, described["error"]!["column"]!.GetValue<long>());
        }
    }

    /// <summary>The runtime refuses these two; the description of each says what is wrong with it, without being asked.</summary>
    [Fact]
    public void A_file_the_runtime_refuses_though_it_is_json_is_described_as_what_it_is()
    {
        var list = ConfigShape.Describe("[]", SettingsAllowlist.Default, NoPlugins);
        var twice = ConfigShape.Describe("""{"Update":{"IntervalHours":1,"IntervalHours":2}}""", SettingsAllowlist.Default, NoPlugins);

        Assert.Equal("array", list["shape"]!["type"]!.GetValue<string>());
        Assert.Equal(1, twice["shape"]!["members"]!["Update"]!["duplicate_keys"]!.GetValue<int>());
    }

    /// <summary>
    /// What the runtime does with the file, from the sentence it throws when it cannot read it — which is the one place
    /// it says where its reader stopped, and the one the client's numbers have to be the same as.
    /// </summary>
    private static (Outcome Outcome, long? Line, long? Column) RuntimeReads(TempDataDir dir)
    {
        try
        {
            (JasonConfiguration.Build(dir.Paths, shippedSettingsDirectory: null) as IDisposable)?.Dispose();
            return (Outcome.Read, null, null);
        }
        catch (InvalidOperationException refused)
        {
            var said = Failure().Match(refused.Message);
            Assert.True(said.Success, refused.Message);
            return said.Groups["line"].Success
                ? (Outcome.NotJson, long.Parse(said.Groups["line"].Value, CultureInfo.InvariantCulture), long.Parse(said.Groups["byte"].Value, CultureInfo.InvariantCulture))
                : (Outcome.Refused, null, null);
        }
    }

    [GeneratedRegex(@"could not be read \((?<type>\w+)(?: at line (?<line>[0-9]+), byte (?<byte>[0-9]+))?\)\.\z")]
    private static partial Regex Failure();
}
