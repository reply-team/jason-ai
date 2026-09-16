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

        if (fixture["expect"] is JsonValue)
        {
            Assert.Equal("valid", (string?)fixture["expect"]);
            Assert.Empty(problems);
            return;
        }

        TheStatedProblemsAndNoOthers(relative, (JsonObject)fixture["expect"]!, problems);
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
                TheFailureTheOperationDeclares(relative, contract, outcome.Error);
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
    /// A failure a fixture shows is one its operation declares, filed under the class the document files it under.
    /// The class is what the runtime reads, so a code the document never listed teaches a plugin author to report
    /// something no operation accepts, and a code shown under the wrong class teaches the opposite of the document:
    /// repeat where the contract says stop, or stop where it says repeat.
    /// </summary>
    private static void TheFailureTheOperationDeclares(string relative, OperationContract contract, OutcomeError error)
    {
        var declared = contract.FailureCodes.SingleOrDefault(code => string.Equals(code.Code, error.Code, StringComparison.Ordinal));

        Assert.True(
            declared is not null,
            $"{relative} reports `{error.Code}`, which {contract.Id} does not declare. It declares "
            + string.Join(", ", contract.FailureCodes.Select(code => "`" + code.Code + "`")) + ".");
        Assert.True(
            declared.Class == error.Class,
            $"{relative} reports `{error.Code}` as `{Name(error.Class)}`; {contract.Id} declares it as `{Name(declared.Class)}`.");
    }

    /// <summary>
    /// What a fixture says is wrong, held against what the runtime reports. One routine serves both kinds, because
    /// both are answering the same question about the same validator. A fixture that claimed only "something is
    /// wrong" would go on passing while the document was broken somewhere nobody meant, so the pointers are
    /// compared as a sorted set; and where a fixture names the reason as well — one of the reason codes the
    /// package publishes, which travels outwards as the code of an API error rather than staying inside the
    /// runtime — that is held against the report too, so that a constraint quietly changing kind at that pointer
    /// fails here rather than passing for a reason nobody wrote down.
    /// </summary>
    private static void TheStatedProblemsAndNoOthers(string relative, JsonObject expected, IReadOnlyList<SchemaProblem> problems)
    {
        var stated = expected["invalid"] as JsonArray;

        Assert.True(stated is not null, $"{relative} says the document is invalid without saying what is invalid about it.");
        Assert.NotEmpty(stated);
        Assert.Equal(
            stated.Select(Pointer).Order(StringComparer.Ordinal).ToList(),
            problems.Select(problem => problem.Pointer).Order(StringComparer.Ordinal).ToList());

        // Where a fixture names the reason at every entry for a pointer, the set at that pointer is compared for
        // equality rather than for membership: "one of the problems here is the one I named" goes on passing
        // after that pointer grows a second problem nobody meant, which is exactly the drift this exists to catch.
        foreach (var group in stated.OfType<JsonObject>().GroupBy(entry => (string)entry["pointer"]!, StringComparer.Ordinal))
        {
            var reasons = group.Select(entry => (string?)entry["reason"]).ToList();
            if (reasons.Exists(reason => reason is null))
            {
                continue;
            }

            var named = reasons.OfType<string>().Order(StringComparer.Ordinal).ToList();
            var reported = problems.Where(problem => problem.Pointer == group.Key).Select(problem => problem.Reason).Order(StringComparer.Ordinal).ToList();

            Assert.True(
                named.SequenceEqual(reported, StringComparer.Ordinal),
                $"{relative} says `{group.Key}` fails with {Listed(named)}; the validator reports {Listed(reported)} there.");
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

    /// <summary>A class as the documents spell it, so a failure message reads in the vocabulary of the contract.</summary>
    private static string Name(FailureClass failure) =>
        JsonSerializer.Serialize(failure, JasonJson.Options).Trim('"');

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
