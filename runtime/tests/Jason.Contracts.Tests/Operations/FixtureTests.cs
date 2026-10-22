using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The published fixtures, run through the same code the runtime runs. A plugin author checks their work against
/// these files; this is what stops the files and the code drifting apart.
/// </summary>
public class FixtureTests
{
    public static TheoryData<string> Inputs() => Files("input");

    public static TheoryData<string> Outcomes() => Files("outcome");

    // Critical: fixtures executed, a sample across the catalog.
    public static TheoryData<string> CriticalInputs() =>
        CriticalRows.Mark(Inputs(), Family.R3, data => data is
            ["docs/contracts/fixtures/campaign.activate/input-a-fraction-of-a-person.json"]
            or ["docs/contracts/fixtures/engagement.summarize/input-a-span-of-dates-for-a-campaign-linked-now.json"]
            or ["docs/contracts/fixtures/social_invitation.send/input-without-a-note.json"]);

    // Critical: outcome classification from fixtures, a sample.
    public static TheoryData<string> CriticalOutcomes() =>
        CriticalRows.Mark(Outcomes(), Family.R1, data => data is
            ["docs/contracts/fixtures/campaign.activate/outcome-a-negative-count.json"]
            or ["docs/contracts/fixtures/conversation.reply/outcome-pins-the-contact.json"]
            or ["docs/contracts/fixtures/social_invitation.send/outcome-suppressed.json"]);

    public static TheoryData<string> Previews() => Files("preview");

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
    [Critical(Family.R3, "no operation without fixtures")]
    public void Every_published_operation_has_fixtures_of_both_kinds()
    {
        var inputs = ContractFiles.FixtureFiles("input").Select(Read).ToList();
        var outcomes = ContractFiles.FixtureFiles("outcome").Select(Read).ToList();

        Assert.NotEmpty(OperationCatalog.All);
        Assert.NotEmpty(DeliveryCatalog.All);
        Assert.NotEmpty(EventsCatalog.All);
        foreach (var id in OperationCatalog.All.Select(contract => contract.Id)
            .Concat(DeliveryCatalog.All.Select(contract => contract.Id))
            .Concat(EventsCatalog.All.Select(contract => contract.Id)))
        {
            Assert.Contains(inputs, fixture => (string?)fixture["operation"] == id);
            Assert.Contains(outcomes, fixture => (string?)fixture["operation"] == id);
        }
    }

    /// <summary>
    /// Every operation that finds the provider's contact for its person — <c>find_or_create</c> or <c>find_only</c> —
    /// publishes the answer that pins it: <c>outcome-pins-the-contact.json</c>, a success whose <c>expect.pinned</c>
    /// names the contact, so a plugin author reads what the runtime writes down from the identifier their lookup found.
    /// </summary>
    [Fact]
    public void Every_operation_that_finds_the_person_publishes_the_answer_that_pins_them()
    {
        var finding = OperationCatalog.All
            .Where(contract => contract.Preflight.ProviderContact is ProviderContact.FindOrCreate or ProviderContact.FindOnly)
            .ToList();

        Assert.NotEmpty(finding);
        foreach (var contract in finding)
        {
            var path = Path.Combine(ContractFiles.Fixtures, contract.Id, "outcome-pins-the-contact.json");
            Assert.True(File.Exists(path), $"{contract.Id} finds the provider's contact and publishes no {ContractFiles.Relative(path)}.");

            var expected = (JsonObject)Read(ContractFiles.Relative(path))["expect"]!;
            Assert.Equal("succeeded", (string?)expected["status"]);
            Assert.True(
                expected["pinned"]?["contact"] is JsonValue pinned && pinned.GetValueKind() == JsonValueKind.String,
                $"{ContractFiles.Relative(path)} pins no contact: {expected.ToJsonString()}");
        }
    }

    /// <summary>
    /// Every operation that may create the person — <c>find_or_create</c> — publishes the refusal that keeps it from
    /// creating a second one: <c>outcome-contact-not-unique.json</c>, two of the provider's contacts under the consumed
    /// value, failed and permanent, so a plugin author reads that the lookup is never guessed past into an import.
    /// </summary>
    [Fact]
    public void Every_operation_that_may_create_the_person_publishes_the_refusal_of_two_held()
    {
        var creating = OperationCatalog.All.Where(contract => contract.Preflight.ProviderContact == ProviderContact.FindOrCreate).ToList();

        Assert.NotEmpty(creating);
        foreach (var contract in creating)
        {
            var path = Path.Combine(ContractFiles.Fixtures, contract.Id, "outcome-contact-not-unique.json");
            Assert.True(File.Exists(path), $"{contract.Id} may create the person and publishes no {ContractFiles.Relative(path)}.");

            var fixture = Read(ContractFiles.Relative(path));
            var answered = (string?)fixture["outcome"]?["error"]?["code"];
            var expected = (JsonObject)fixture["expect"]!;
            Assert.True(
                answered == "contact_not_unique" && (string?)expected["status"] == "failed" && (string?)expected["class"] == "permanent",
                $"{ContractFiles.Relative(path)} answers {answered ?? "no code"} and expects {expected.ToJsonString()}.");
        }
    }

    /// <summary>
    /// A preview is proven by rendering it, so an operation that declares one has fixtures that render it, and one
    /// that declares none has nothing to render. An operation nobody approves declares none (the document tests hold
    /// that), so it has no preview fixtures either.
    /// </summary>
    [Fact]
    public void An_operation_has_preview_fixtures_exactly_when_it_declares_a_preview()
    {
        var previews = ContractFiles.FixtureFiles("preview").Select(Read).ToList();

        Assert.All(OperationCatalog.All, contract =>
        {
            var rendered = previews.Any(fixture => (string?)fixture["operation"] == contract.Id);
            Assert.True(
                rendered == contract.Preview is not null,
                contract.Preview is null
                    ? $"{contract.Id} declares no preview, yet a preview fixture renders one for it."
                    : $"{contract.Id} declares a preview and no fixture renders it.");
        });
    }

    /// <summary>
    /// What a person deciding will read, rendered from an input the schema accepts. The summary is compared whole,
    /// because the words around the values are the half of the preview a pointer check cannot see.
    /// </summary>
    [Theory]
    [MemberData(nameof(Previews))]
    public void A_preview_fixture_says_what_a_person_deciding_will_read(string relative)
    {
        var fixture = Read(relative);
        var contract = Operation(fixture, relative);
        var input = fixture["input"];

        Assert.True(contract.Preview is not null, $"{relative} renders a preview {contract.Id} does not declare.");
        var problems = SchemaValidator.Validate(input, contract.InputSchema);
        Assert.True(problems.Count == 0, $"{relative} renders an input the schema refuses at {Listed([.. problems.Select(problem => problem.Pointer)])}.");

        var expected = Assert.IsType<JsonObject>(fixture["expect"]);
        Assert.True(
            expected.All(member => member.Key is "summary" or "highlights"),
            $"{relative} expects something other than a summary and its highlights.");
        Assert.Equal((string?)expected["summary"], contract.Preview.Summary.Render(input));

        var highlights = Assert.IsType<JsonObject>(expected["highlights"]);
        var rendered = contract.Preview.Highlight(input);
        Assert.True(
            JsonNode.DeepEquals(highlights, rendered),
            $"{relative} expects the highlights {highlights.ToJsonString()}; the preview carries {rendered.ToJsonString()}.");
    }

    /// <summary>
    /// The load check walks plain properties only, so a pointer behind a reference — a contact's name, the
    /// campaign's — is proven here instead: each placeholder and each highlight has to name a real value in at least
    /// one fixture, or a misspelling would render a dash for every person anybody is ever asked about.
    /// </summary>
    [Fact]
    public void Every_pointer_a_preview_uses_names_a_value_in_at_least_one_of_its_fixtures()
    {
        var previews = ContractFiles.FixtureFiles("preview").Select(Read).ToList();

        foreach (var contract in OperationCatalog.All.Where(contract => contract.Preview is not null))
        {
            var inputs = previews.Where(fixture => (string?)fixture["operation"] == contract.Id).Select(fixture => fixture["input"]).ToList();
            foreach (var pointer in contract.Preview!.Summary.Pointers.Concat(contract.Preview.Highlights).Distinct(StringComparer.Ordinal))
            {
                Assert.True(
                    inputs.Exists(input => PreviewPointer.Parse(pointer).Resolve(input) is not null),
                    $"No preview fixture of {contract.Id} holds a value at `{pointer}`, so nothing shows that the pointer names one.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(CriticalInputs))]
    public void An_input_fixture_says_what_the_validator_will_say(string relative)
    {
        var fixture = Read(relative);
        var problems = SchemaValidator.Validate(fixture["input"], InputSchema(fixture, relative));

        if (fixture["expect"] is JsonValue)
        {
            Assert.Equal("valid", (string?)fixture["expect"]);
            Assert.Empty(problems);
            return;
        }

        TheStatedProblemsAndNoOthers(relative, (JsonObject)fixture["expect"]!, problems);
    }

    [Theory]
    [MemberData(nameof(CriticalOutcomes))]
    public void An_outcome_fixture_says_what_the_runtime_will_make_of_that_answer(string relative)
    {
        var fixture = Read(relative);
        var outcome = JsonSerializer.Deserialize<PluginOutcome>(fixture["outcome"]!.ToJsonString(), JasonJson.Options)!;
        var expected = (JsonObject)fixture["expect"]!;
        var status = (string)expected["status"]!;

        if (Delivery(fixture, relative) is { } delivery)
        {
            ADeliveryOutcome(relative, delivery, outcome, expected, status);
            return;
        }

        if (Events(fixture, relative) is { } events)
        {
            AnEventsOutcome(relative, events, outcome, expected, status);
            return;
        }

        var contract = Operation(fixture, relative);

        var resultProblems = outcome.Status == OutcomeStatus.Succeeded
            ? [.. OutcomeContract.CheckResult(contract, outcome.Result), .. OutcomeContract.CheckExternalIds(contract, outcome.ExternalIds)]
            : new List<SchemaProblem>();

        switch (status)
        {
            case "succeeded":
                Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
                Assert.Empty(resultProblems);
                Assert.False((bool)expected["retriable"]!, "A succeeded outcome is never repeated.");
                if (expected["pinned"] is JsonObject pins)
                {
                    // A success's identifiers are beside its result, every one of a declared kind, so all of them are written down.
                    Assert.Equal(pins.ToJsonString(), (outcome.ExternalIds ?? []).ToJsonString());
                }

                break;

            case "failed":
                Assert.Equal(OutcomeStatus.Failed, outcome.Status);
                Assert.NotNull(outcome.Error);
                Assert.Equal(Class(expected), outcome.Error.Class);
                TheFailureTheOperationDeclares(relative, contract, outcome.Error);
                Assert.Equal(OutcomeContract.Retriable(outcome.Error.Class, contract), (bool)expected["retriable"]!);
                TheIdentifiersTheFailureLeaves(relative, contract, expected, outcome);
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
    /// What a failed answer leaves behind. A failed outcome may still carry identifiers, and the answer-lost case
    /// is where they matter most — the effect happened, the answer did not come back, and the identifier is the
    /// only trace — so a declared kind is written down by the same rule as on a success. An undeclared kind does
    /// <b>not</b> turn the outcome into a shape error the way it would on a success: replacing the failure the
    /// plugin reported with "your identifier was wrong" would hide the reason the operation failed. That
    /// asymmetry is deliberate, and this is what executes it.
    /// </summary>
    /// <remarks>
    /// Identifiers travel on the error rather than beside the result: that is where <c>host.fail</c> puts them
    /// and the only place the runtime reads them, so a fixture showing them anywhere else would teach a plugin
    /// author to return a pin that is silently dropped.
    /// </remarks>
    private static void TheIdentifiersTheFailureLeaves(string relative, OperationContract contract, JsonObject expected, PluginOutcome outcome)
    {
        Assert.True(
            outcome.ExternalIds is null,
            $"{relative} puts identifiers beside the result of a failed outcome; a failure carries them on its error.");

        var refused = OutcomeContract.CheckExternalIds(contract, outcome.Error!.ExternalIds, OutcomeContract.OnTheError);
        if (expected["invalid"] is JsonArray)
        {
            TheStatedProblemsAndNoOthers(relative, expected, refused);
        }
        else
        {
            Assert.True(refused.Count == 0, $"{relative} returns an identifier {contract.Id} does not declare without saying so.");
        }

        var pinned = new JsonObject();
        foreach (var (kind, value) in outcome.Error.ExternalIds ?? [])
        {
            if (contract.ExternalIds.ContainsKey(kind) && !refused.Any(problem => problem.Pointer.EndsWith("/" + kind, StringComparison.Ordinal)))
            {
                pinned[kind] = value?.DeepClone();
            }
        }

        Assert.Equal(
            (expected["pinned"] as JsonObject ?? []).ToJsonString(),
            pinned.ToJsonString());
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
    /// fails here rather than passing for a reason nobody wrote down. The named preferences' value fixtures are held by
    /// the same routine, for the same reason.
    /// </summary>
    internal static void TheStatedProblemsAndNoOthers(string relative, JsonObject expected, IReadOnlyList<SchemaProblem> problems)
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

    /// <summary>
    /// What a delivery outcome means: a success is a result the output schema accepts and no identifiers, because a
    /// delivery pins nothing; a failure is one the contract declares under its class; and a delivery is repeated only
    /// where it failed transiently, once.
    /// </summary>
    private static void ADeliveryOutcome(string relative, DeliveryContract contract, PluginOutcome outcome, JsonObject expected, string status)
    {
        var resultProblems = outcome.Status == OutcomeStatus.Succeeded
            ? [.. contract.CheckResult(outcome.Result), .. DeliveryContract.CheckExternalIds(outcome.ExternalIds)]
            : new List<SchemaProblem>();

        switch (status)
        {
            case "succeeded":
                Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
                Assert.Empty(resultProblems);
                Assert.False((bool)expected["retriable"]!, "A delivered pointer is never sent again.");
                break;

            case "failed":
                Assert.Equal(OutcomeStatus.Failed, outcome.Status);
                Assert.NotNull(outcome.Error);
                Assert.Equal(Class(expected), outcome.Error.Class);
                var declared = contract.FailureCodes.SingleOrDefault(code => string.Equals(code.Code, outcome.Error.Code, StringComparison.Ordinal));
                Assert.True(declared is not null, $"{relative} reports `{outcome.Error.Code}`, which {contract.Id} does not declare.");
                Assert.Equal(declared.Class, outcome.Error.Class);
                Assert.Equal(DeliveryContract.Retriable(outcome.Error.Class), (bool)expected["retriable"]!);
                break;

            case "result_invalid":
                Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
                TheStatedProblemsAndNoOthers(relative, expected, resultProblems);
                Assert.Equal(FailureClass.Ambiguous, Class(expected));
                Assert.False((bool)expected["retriable"]!, "A pointer that may have arrived is not sent twice.");
                break;

            default:
                Assert.Fail($"{relative} expects the unknown status `{status}`.");
                break;
        }
    }

    /// <summary>
    /// What a read's outcome means: a success is a page the output schema accepts and no identifiers, because a read
    /// finds pins and writes none; a failure is one the contract declares under its class; and a read that failed is
    /// read again at the doubled cadence where the failure may pass by itself — transient, or ambiguous, which an
    /// answer outside the contract is — and after a day where it will not.
    /// </summary>
    private static void AnEventsOutcome(string relative, EventsContract contract, PluginOutcome outcome, JsonObject expected, string status)
    {
        var resultProblems = outcome.Status == OutcomeStatus.Succeeded
            ? [.. contract.CheckResult(outcome.Result), .. EventsContract.CheckExternalIds(outcome.ExternalIds)]
            : new List<SchemaProblem>();

        switch (status)
        {
            case "succeeded":
                Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
                Assert.True(resultProblems.Count == 0, $"{relative} succeeds with an answer refused at {Listed([.. resultProblems.Select(problem => problem.Pointer + " " + problem.Reason)])}.");
                Assert.False((bool)expected["retriable"]!, "A page that was read is not read again.");
                break;

            case "failed":
                Assert.Equal(OutcomeStatus.Failed, outcome.Status);
                Assert.NotNull(outcome.Error);
                Assert.Equal(Class(expected), outcome.Error.Class);
                var declared = contract.FailureCodes.SingleOrDefault(code => string.Equals(code.Code, outcome.Error.Code, StringComparison.Ordinal));
                Assert.True(declared is not null, $"{relative} reports `{outcome.Error.Code}`, which {contract.Id} does not declare.");
                Assert.Equal(declared.Class, outcome.Error.Class);
                Assert.Equal(EventsContract.Retriable(outcome.Error.Class), (bool)expected["retriable"]!);
                break;

            case "result_invalid":
                Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
                TheStatedProblemsAndNoOthers(relative, expected, resultProblems);
                Assert.Equal(FailureClass.Ambiguous, Class(expected));
                Assert.Equal(EventsContract.Retriable(FailureClass.Ambiguous), (bool)expected["retriable"]!);
                break;

            default:
                Assert.Fail($"{relative} expects the unknown status `{status}`.");
                break;
        }
    }

    /// <summary>The schema an input fixture is checked against, in whichever family publishes its contract.</summary>
    private static JsonObject InputSchema(JsonObject fixture, string relative) =>
        Delivery(fixture, relative)?.InputSchema ?? Events(fixture, relative)?.InputSchema ?? Operation(fixture, relative).InputSchema;

    /// <summary>The events contract a fixture names, or null where it names another family's.</summary>
    private static EventsContract? Events(JsonObject fixture, string relative)
    {
        var id = (string?)fixture["operation"];
        if (id is null || EventsCatalog.Find(id) is not { } contract)
        {
            return null;
        }

        Assert.Equal(contract.Version, (int)fixture["version"]!);
        Assert.False(string.IsNullOrWhiteSpace((string?)fixture["case"]), $"{relative} does not say which case it is.");
        return contract;
    }

    /// <summary>The delivery contract a fixture names, or null where it names a canonical operation.</summary>
    private static DeliveryContract? Delivery(JsonObject fixture, string relative)
    {
        var id = (string?)fixture["operation"];
        if (id is null || DeliveryCatalog.Find(id) is not { } contract)
        {
            return null;
        }

        Assert.Equal(contract.Version, (int)fixture["version"]!);
        Assert.False(string.IsNullOrWhiteSpace((string?)fixture["case"]), $"{relative} does not say which case it is.");
        return contract;
    }

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
}
