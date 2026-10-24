using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Plugins;

/// <summary>
/// The published block vectors under <c>docs/contracts/fixtures/block/</c>, run through the check the runtime runs when a
/// plugin reports a block. A plugin author checks a block against these files; this is what stops the files and the
/// document drifting apart.
/// </summary>
public sealed class ProviderBlockFixtureTests
{
    private static string Root => Path.Combine(ContractFiles.Fixtures, "block");

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
    public void A_block_fixture_says_what_the_check_will_say(string relative)
    {
        var fixture = Read(relative);
        var contract = ProviderBlockContract.Published;

        Assert.True((string?)fixture["contract"] == contract.Id, $"{relative} names `{fixture["contract"]}`, and the block is `{contract.Id}`.");
        Assert.True(contract.Version == (int)fixture["version"]!, $"{relative} is written for version {fixture["version"]}, and the block is at {contract.Version}.");
        Assert.False(string.IsNullOrWhiteSpace((string?)fixture["case"]), $"{relative} does not say which case it is.");

        var problems = contract.Check(fixture["value"]);
        if (fixture["expect"] is JsonValue)
        {
            Assert.Equal("valid", (string?)fixture["expect"]);
            Assert.True(problems.Count == 0, $"{relative} is valid, and the check says {string.Join("; ", problems.Select(problem => problem.Pointer + " " + problem.Reason))}.");
            return;
        }

        FixtureTests.TheStatedProblemsAndNoOthers(relative, (JsonObject)fixture["expect"]!, problems);
    }

    /// <summary>
    /// Both scopes are shown accepted, and both ways the scope and the sender's identifier can disagree are shown refused,
    /// so a plugin author sees the one rule the schema states across two fields.
    /// </summary>
    [Fact]
    public void The_fixtures_show_both_scopes_and_both_ways_a_scope_and_a_sender_disagree()
    {
        var fixtures = Files().Select(file => Read(ContractFiles.Relative(file))).ToList();
        bool Valid(JsonObject fixture) => fixture["expect"] is JsonValue;
        string? Scope(JsonObject fixture) => (string?)fixture["value"]?["scope"];
        bool NamesASender(JsonObject fixture) => fixture["value"]?["sender_external_id"] is not null;

        Assert.Contains(fixtures, fixture => Valid(fixture) && Scope(fixture) == "account");
        Assert.Contains(fixtures, fixture => Valid(fixture) && Scope(fixture) == "sender");
        Assert.Contains(fixtures, fixture => !Valid(fixture) && Scope(fixture) == "account" && NamesASender(fixture));
        Assert.Contains(fixtures, fixture => !Valid(fixture) && Scope(fixture) == "sender" && !NamesASender(fixture));
    }

    private static IEnumerable<string> Files() =>
        Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "value-*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [];

    private static JsonObject Read(string relative) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Root, relative)))!;
}
