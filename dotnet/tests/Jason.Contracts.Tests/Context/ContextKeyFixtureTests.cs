using System.Text.Json.Nodes;
using Jason.Contracts.Context;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Context;

/// <summary>
/// The published value vectors under <c>docs/contracts/fixtures/context/</c>, run through the check the runtime runs
/// when a campaign's context is created or a patch names a reserved key. A skill author checks a value against these
/// files; this is what stops the files and the documents drifting apart.
/// </summary>
public sealed class ContextKeyFixtureTests
{
    private static string Root => Path.Combine(ContractFiles.Fixtures, "context");

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
        var contract = key is null ? null : ContextKeyCatalog.Find(key);

        Assert.True(contract is not null, $"{relative} names the context key `{key}`, which is not published.");
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

        Assert.NotEmpty(ContextKeyCatalog.All);
        foreach (var key in ContextKeyCatalog.Keys)
        {
            var mine = fixtures.Where(fixture => (string?)fixture["key"] == key).ToList();
            Assert.True(mine.Exists(fixture => fixture["expect"] is JsonValue), $"{key} publishes no value it accepts.");
            Assert.True(mine.Exists(fixture => fixture["expect"] is JsonObject), $"{key} publishes no value it refuses.");
        }
    }

    /// <summary>
    /// The goals' refusals a role is most likely to write, each published as a vector: a goal without its kind, a kind
    /// outside the list, goals that are no list, an order that is no whole number, and two goals under one id.
    /// </summary>
    [Fact]
    public void The_goals_publish_the_refusals_a_role_is_most_likely_to_meet()
    {
        var reasons = Files()
            .Select(file => Read(ContractFiles.Relative(file)))
            .Where(fixture => (string?)fixture["key"] == "goals" && fixture["expect"] is JsonObject)
            .SelectMany(fixture => ((JsonArray)fixture["expect"]!["invalid"]!).Select(problem => (string)problem!["reason"]!))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Superset(new HashSet<string>(["required", "enum", "type", ContextKeyContract.UniqueItems], StringComparer.Ordinal), reasons);
    }

    private static IEnumerable<string> Files() =>
        Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "value-*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [];

    private static JsonObject Read(string relative) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Root, relative)))!;
}
