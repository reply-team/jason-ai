using System.Text.Json.Nodes;
using Jason.Contracts.Preferences;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Preferences;

/// <summary>
/// The published value vectors under <c>docs/contracts/fixtures/preferences/</c>, run through the check the runtime runs
/// when a person sets a preference. A skill author checks a value against these files; this is what stops the files and
/// the documents drifting apart.
/// </summary>
public sealed class PreferenceFixtureTests
{
    private static string Root => Path.Combine(ContractFiles.Fixtures, "preferences");

    public static TheoryData<string> Values()
    {
        var data = new TheoryData<string>();
        foreach (var file in Files())
        {
            data.Add(ContractFiles.Relative(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void A_value_fixture_says_what_the_check_will_say(string relative)
    {
        var fixture = Read(relative);
        var key = (string?)fixture["key"];
        var contract = key is null ? null : PreferenceCatalog.Find(key);

        Assert.True(contract is not null, $"{relative} names the preference `{key}`, which is not published.");
        Assert.True(contract.Version == (int)fixture["version"]!, $"{relative} is written for version {fixture["version"]} of {key}, which is at {contract.Version}.");
        Assert.False(string.IsNullOrWhiteSpace((string?)fixture["case"]), $"{relative} does not say which case it is.");
        Assert.True(
            string.Equals(Path.GetFileName(Path.GetDirectoryName(relative)), key, StringComparison.Ordinal),
            $"{relative} names `{key}` and lives in another key's directory.");

        var problems = contract.CheckValue(fixture["value"]);
        if (fixture["expect"] is JsonValue)
        {
            Assert.Equal("valid", (string?)fixture["expect"]);
            Assert.True(problems.Count == 0, $"{relative} is valid, and the check says {string.Join("; ", problems.Select(problem => problem.Pointer + " " + problem.Reason))}.");
            return;
        }

        FixtureTests.TheStatedProblemsAndNoOthers(relative, (JsonObject)fixture["expect"]!, problems);
    }

    /// <summary>Every key shows a value it accepts and a value it refuses, so a skill author sees both sides of its schema.</summary>
    [Fact]
    public void Every_published_key_has_a_valid_and_an_invalid_value_fixture()
    {
        var fixtures = Files().Select(file => Read(ContractFiles.Relative(file))).ToList();

        Assert.NotEmpty(PreferenceCatalog.All);
        foreach (var key in PreferenceCatalog.Keys)
        {
            var mine = fixtures.Where(fixture => (string?)fixture["key"] == key).ToList();
            Assert.True(mine.Exists(fixture => fixture["expect"] is JsonValue), $"{key} publishes no value it accepts.");
            Assert.True(mine.Exists(fixture => fixture["expect"] is JsonObject), $"{key} publishes no value it refuses.");
        }
    }

    private static IEnumerable<string> Files() =>
        Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "value-*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [];

    private static JsonObject Read(string relative) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Root, relative)))!;
}
