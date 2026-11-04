using System.Text.Json.Nodes;
using Jason.Contracts.Journal;
using Jason.Contracts.Operations;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Journal;

/// <summary>
/// The published body vectors under <c>docs/contracts/fixtures/journal/</c>, run through the check every line of a
/// reserved kind is held to when the runtime writes it. A plugin author reads a kind's lines from these files; this is
/// what stops the files and the documents drifting apart.
/// </summary>
public sealed class JournalKindFixtureTests
{
    private static string Root => Path.Combine(ContractFiles.Fixtures, "journal");

    private static readonly string[] Fields = ["kind", "version", "case", "body", "expect"];

    public static TheoryData<string> Bodies()
    {
        var data = new TheoryData<string>();
        foreach (var file in Files())
        {
            data.Add(ContractFiles.Relative(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void A_body_fixture_says_what_the_check_will_say(string relative)
    {
        var fixture = Read(relative);
        var unknown = fixture.Select(field => field.Key).Where(field => !Fields.Contains(field, StringComparer.Ordinal)).ToList();
        Assert.True(unknown.Count == 0, $"{relative} carries {string.Join(", ", unknown)}, which a body fixture does not.");

        var kind = (string?)fixture["kind"];
        var contract = kind is null ? null : JournalKindCatalog.Find(kind);

        Assert.True(contract is not null, $"{relative} names the kind `{kind}`, which is not published.");
        Assert.True(contract.Version == (int)fixture["version"]!, $"{relative} is written for version {fixture["version"]} of {kind}, which is at {contract.Version}.");
        Assert.False(string.IsNullOrWhiteSpace((string?)fixture["case"]), $"{relative} does not say which case it is.");
        Assert.True(
            string.Equals(Path.GetFileName(Path.GetDirectoryName(relative)), kind, StringComparison.Ordinal),
            $"{relative} names `{kind}` and lives in another kind's directory.");
        Assert.True(fixture["body"] is JsonObject, $"{relative} carries no body: a line's key, old and new as one object.");

        var problems = SchemaValidator.Validate(fixture["body"], contract.Body);
        if (fixture["expect"] is JsonValue)
        {
            Assert.Equal("valid", (string?)fixture["expect"]);
            Assert.True(problems.Count == 0, $"{relative} is valid, and the check says {string.Join("; ", problems.Select(problem => problem.Pointer + " " + problem.Reason))}.");

            // What the runtime checks is the line's three members, so a valid body reads the same through that door.
            var body = (JsonObject)fixture["body"]!;
            Assert.Empty(contract.CheckBody((string?)body["key"], body["old"], body["new"]));
            return;
        }

        FixtureTests.TheStatedProblemsAndNoOthers(relative, (JsonObject)fixture["expect"]!, problems);
    }

    /// <summary>Every kind shows a body it accepts and a body it refuses, so a reader sees both sides of its schema.</summary>
    [Fact]
    public void Every_published_kind_has_a_valid_and_an_invalid_body_fixture()
    {
        var fixtures = Files().Select(file => Read(ContractFiles.Relative(file))).ToList();

        Assert.NotEmpty(JournalKindCatalog.All);
        var missing = new List<string>();
        foreach (var kind in JournalKindCatalog.Kinds)
        {
            var mine = fixtures.Where(fixture => (string?)fixture["kind"] == kind).ToList();
            if (!mine.Exists(fixture => fixture["expect"] is JsonValue))
            {
                missing.Add($"{kind} publishes no body it accepts");
            }

            if (!mine.Exists(fixture => fixture["expect"] is JsonObject))
            {
                missing.Add($"{kind} publishes no body it refuses");
            }
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    private static IEnumerable<string> Files() =>
        Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [];

    private static JsonObject Read(string relative) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Root, relative)))!;
}
