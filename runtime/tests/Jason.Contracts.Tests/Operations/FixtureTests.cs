using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The published fixtures, run through the same code the runtime runs. A plugin author checks their work against
/// these files; this is what stops the files and the code drifting apart.
/// </summary>
public class FixtureTests
{
    public static TheoryData<string> Inputs() => Files("input");

    public static TheoryData<string> Outcomes() => Files("outcome");

    private static TheoryData<string> Files(string prefix)
    {
        var data = new TheoryData<string>();
        foreach (var file in ContractFiles.FixtureFiles(prefix))
        {
            data.Add(ContractFiles.Relative(file));
        }

        return data;
    }

    private static JsonObject Read(string relative) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Root, relative)))!;

    [Fact]
    public void Every_published_operation_has_fixtures_of_both_kinds()
    {
        var inputs = ContractFiles.FixtureFiles("input").Select(Read).ToList();
        var outcomes = ContractFiles.FixtureFiles("outcome").Select(Read).ToList();

        Assert.NotEmpty(OperationCatalog.All);
        Assert.All(OperationCatalog.All, contract =>
        {
            Assert.Contains(inputs, fixture => (string?)fixture["operation"] == contract.Id);
            Assert.Contains(outcomes, fixture => (string?)fixture["operation"] == contract.Id);
        });
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void An_input_fixture_says_what_the_validator_will_say(string relative)
    {
        var fixture = Read(relative);
        var contract = Operation(fixture, relative);

        var problems = SchemaValidator.Validate(fixture["input"], contract.InputSchema);
        var pointers = problems.Select(problem => problem.Pointer).Order(StringComparer.Ordinal).ToList();

        if (fixture["expect"] is JsonValue)
        {
            Assert.Equal("valid", (string?)fixture["expect"]);
            Assert.Empty(problems);
            return;
        }

        var expected = ((JsonArray)fixture["expect"]!["invalid"]!).Select(pointer => (string)pointer!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected, pointers);
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void An_outcome_fixture_says_what_the_runtime_will_make_of_that_answer(string relative)
    {
        var fixture = Read(relative);
        var contract = Operation(fixture, relative);
        var outcome = JsonSerializer.Deserialize<PluginOutcome>(fixture["outcome"]!.ToJsonString(), JasonJson.Options)!;
        var expected = (JsonObject)fixture["expect"]!;
        var status = (string)expected["status"]!;

        var resultProblems = outcome.Status == OutcomeStatus.Succeeded
            ? [.. OutcomeContract.CheckResult(contract, outcome.Result), .. OutcomeContract.CheckExternalIds(contract, outcome.ExternalIds)]
            : new List<SchemaProblem>();

        switch (status)
        {
            case "succeeded":
                Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
                Assert.Empty(resultProblems);
                Assert.False((bool)expected["retriable"]!, "A succeeded outcome is never repeated.");
                break;

            case "failed":
                Assert.Equal(OutcomeStatus.Failed, outcome.Status);
                Assert.NotNull(outcome.Error);
                Assert.Equal(Class(expected), outcome.Error.Class);
                Assert.Equal(Retriable(outcome.Error.Class, contract), (bool)expected["retriable"]!);
                break;

            case "result_invalid":
                Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
                TheStatedProblemsAndNoOthers(relative, expected, resultProblems);
                Assert.Equal(FailureClass.Ambiguous, Class(expected));
                Assert.False((bool)expected["retriable"]!, "A shape error is deterministic, so repeating it can only spend budget.");
                break;

            default:
                Assert.Fail($"{relative} expects the unknown status `{status}`.");
                break;
        }
    }

    /// <summary>
    /// What a <c>result_invalid</c> fixture says is wrong with the answer, held against what the runtime reports.
    /// A fixture that claimed only "something is wrong" would go on passing while the result was broken somewhere
    /// nobody meant, so the pointers are compared as a sorted set exactly as an input fixture's are, and where a
    /// fixture names the reason as well — one of the reason codes the package publishes, which travels outwards as
    /// the code of an API error rather than staying inside the runtime — that is held against the report too.
    /// </summary>
    private static void TheStatedProblemsAndNoOthers(string relative, JsonObject expected, IReadOnlyList<SchemaProblem> problems)
    {
        var stated = expected["invalid"] as JsonArray;

        Assert.True(stated is not null, $"{relative} says the result is invalid without saying what is invalid about it.");
        Assert.NotEmpty(stated);
        Assert.Equal(
            stated.Select(Pointer).Order(StringComparer.Ordinal).ToList(),
            problems.Select(problem => problem.Pointer).Order(StringComparer.Ordinal).ToList());

        foreach (var entry in stated.OfType<JsonObject>())
        {
            var reason = (string?)entry["reason"];
            if (reason is null)
            {
                continue;
            }

            var pointer = (string)entry["pointer"]!;
            var reported = problems.Where(problem => problem.Pointer == pointer).Select(problem => problem.Reason).ToList();

            Assert.True(
                reported.Contains(reason, StringComparer.Ordinal),
                $"{relative} says `{pointer}` fails with `{reason}`; the validator reports {Listed(reported)} there.");
        }
    }

    /// <summary>An entry of <c>invalid</c> is a pointer, or a pointer with the reason the fixture means by it.</summary>
    private static string Pointer(JsonNode? entry) =>
        entry is JsonObject stated ? (string)stated["pointer"]! : (string)entry!;

    private static string Listed(IReadOnlyList<string> reasons) =>
        reasons.Count == 0 ? "nothing" : string.Join(", ", reasons.Select(reason => "`" + reason + "`"));

    private static OperationContract Operation(JsonObject fixture, string relative)
    {
        var id = (string?)fixture["operation"];
        var contract = id is null ? null : OperationCatalog.Find(id);

        Assert.True(contract is not null, $"{relative} names the operation `{id}`, which is not published.");
        Assert.Equal(contract.Version, (int)fixture["version"]!);
        Assert.False(string.IsNullOrWhiteSpace((string?)fixture["case"]), $"{relative} does not say which case it is.");
        return contract;
    }

    private static FailureClass Class(JsonObject expected) =>
        JsonSerializer.Deserialize<FailureClass>("\"" + (string)expected["class"]! + "\"", JasonJson.Options);

    /// <summary>
    /// The retry rule as this version states it, kept here rather than in the runtime because 5a has nothing that
    /// hands a work item to a plugin yet. A shape error is final whatever the operation allows: the next attempt
    /// would run the same code over the same answer, so it can only spend budget while a manager waits.
    /// </summary>
    private static bool Retriable(FailureClass failure, OperationContract contract) => failure switch
    {
        FailureClass.Transient => true,
        FailureClass.Ambiguous => contract.RepeatAfterAmbiguous is RepeatAfterAmbiguous.Safe or RepeatAfterAmbiguous.AfterRecoveryRead,
        _ => false,
    };
}
