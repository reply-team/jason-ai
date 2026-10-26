using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Documentation;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// What the published operation contracts make of a <c>provider_op</c> work item. A planner learns here, while
/// the item is being written, what it would otherwise learn hours later from a failed attempt.
/// </summary>
public class WorkItemContractTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// An operation nobody publishes, and the delivery contract: a notification is a family of its own, never a
    /// provider operation a work item could ask a plugin to perform.
    /// </summary>
    [Theory]
    [InlineData("contacts.enroll")]
    [InlineData("notification.send")]
    public async Task An_operation_this_build_publishes_no_contract_for_is_refused(string operation)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(ProviderOp(campaign.PublicId, operation), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("operation", detail.Field);
        Assert.Equal("unknown", detail.Code);
        Assert.Contains("campaign.get", detail.Message, StringComparison.Ordinal);
        Assert.Contains("list_membership.add", detail.Message, StringComparison.Ordinal);
        Assert.Contains("campaign.enroll", detail.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(operation, detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_the_vocabulary_refuses_is_reported_once_and_not_measured_against_the_catalog()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(ProviderOp(campaign.PublicId, "Campaign.Get"), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("operation", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task An_operation_whose_arguments_are_all_optional_needs_no_input()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var item = await service.CreateAsync(ProviderOp(campaign.PublicId, "campaign.get"), Ct);

        Assert.Equal("campaign.get", item.Operation);
        Assert.Empty(item.Context);
    }

    [Fact]
    [Critical(Family.R3, "context.input at create")]
    public async Task An_operation_that_declares_required_arguments_is_refused_without_an_input()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(ProviderOp(campaign.PublicId, "list_membership.add"), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
        Assert.Contains("list", detail.Message, StringComparison.Ordinal);
        Assert.Contains("channel", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_input_that_is_explicitly_nothing_reads_as_no_arguments_at_all()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "list_membership.add") with { Context = new JsonObject { ["input"] = null } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    [Critical(Family.R3, "the reserved key")]
    public async Task Arguments_the_contract_accepts_are_kept_under_the_reserved_key()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var item = await service.CreateAsync(
            ProviderOp(campaign.PublicId, "list_membership.add") with
            {
                Context = new JsonObject { ["brief"] = "the weekly import", ["input"] = Membership() },
            },
            Ct);

        Assert.Equal("list_membership.add", item.Operation);
        Assert.Equal("lst_7", (string?)item.Context["input"]!["list"]!["external_id"]);
        Assert.Equal("the weekly import", (string?)item.Context["brief"]);
    }

    [Fact]
    public async Task An_argument_of_the_wrong_type_is_reported_at_the_pointer_that_holds_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);
        var input = Membership();
        input["channel"] = 7;

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "list_membership.add") with { Context = new JsonObject { ["input"] = input } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input/channel", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    /// <summary>
    /// The channel's pattern ends in `$`, and an argument that is a name with a newline after it is not that name:
    /// creation refuses it at the pointer that holds it rather than storing it for a plugin to trip over.
    /// </summary>
    [Fact]
    public async Task An_argument_that_ends_in_a_newline_does_not_satisfy_a_pattern_that_ends_the_value()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);
        var input = Membership();
        input["channel"] = "email\n";

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "list_membership.add") with { Context = new JsonObject { ["input"] = input } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input/channel", detail.Field);
        Assert.Equal("invalid", detail.Code);
        Assert.Contains("must match", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_problem_in_the_arguments_is_reported_at_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "list_membership.add") with
                {
                    Context = new JsonObject { ["input"] = new JsonObject { ["channel"] = 7, ["hurry"] = true } },
                },
                Ct));

        Assert.Equal(3, error.Details!.Count);
        Assert.All(error.Details, detail => Assert.Equal("invalid", detail.Code));
        Assert.Contains(error.Details, detail => detail.Field == "context.input/list");
        Assert.Contains(error.Details, detail => detail.Field == "context.input/channel");
        Assert.Contains(error.Details, detail => detail.Field == "context.input/hurry");
    }

    [Fact]
    public async Task An_input_that_is_not_an_object_is_reported_against_the_input_itself()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "campaign.get") with { Context = new JsonObject { ["input"] = "cmp_provider_7" } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    [Critical(Family.S2, "enrolment choices are explicit")]
    public async Task The_enrollment_contract_insists_on_the_three_choices_nobody_may_default()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "campaign.enroll") with
                {
                    Context = new JsonObject { ["input"] = new JsonObject { ["channel"] = "email" } },
                },
                Ct));

        Assert.Equal(3, error.Details!.Count);
        Assert.Contains(error.Details, detail => detail.Field == "context.input/collision");
        Assert.Contains(error.Details, detail => detail.Field == "context.input/start");
        Assert.Contains(error.Details, detail => detail.Field == "context.input/first_touch");
    }

    /// <summary>
    /// The account a message goes from is described once, as a shared definition, and the arguments name it. The
    /// definition is read when the item is written, not skipped: a sender it refuses is refused at the place inside
    /// the sender that is wrong.
    /// </summary>
    [Fact]
    [Critical(Family.R3, "a shared definition at create, refusing inside")]
    public async Task A_sender_its_shared_definition_refuses_is_reported_inside_the_sender()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);
        var input = Message();
        input["sender"]!.AsObject().Remove("address");

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "message.send") with { Context = new JsonObject { ["input"] = input } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.True(detail.Field == "context.input/sender/address", $"The sender was refused at {detail.Field}: {detail.Message}");
        Assert.Equal("invalid", detail.Code);
    }

    /// <summary>
    /// A template the standard refuses is refused when the item is written, at the message, in words that name what
    /// the template writes and where — never the template — rather than hours later by the provider.
    /// </summary>
    [Fact]
    [Critical(Family.R3, "a template refused at create")]
    public async Task A_template_that_writes_a_helper_is_refused_when_the_item_is_written()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);
        var input = Message();
        input["message"]!["compose"] = "template";
        input["message"]!["body"] = "Hi {{toLower contact.first_name}}, thanks for the chat.";

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "message.send") with { Context = new JsonObject { ["input"] = input } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.True(detail.Field == "context.input/message", $"The template was refused at {detail.Field}: {detail.Message}");
        Assert.Contains("`format` at `/message/body`", detail.Message, StringComparison.Ordinal);
        Assert.Contains("a helper or a macro at character 4", detail.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("toLower", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A template refused inside the message's alternatives is refused by the alternatives, at the message, and their
    /// words are what a person reads: a LinkedIn message written as a template is told the format it would need.
    /// </summary>
    [Fact]
    public async Task A_template_on_a_linkedin_message_is_told_the_format_a_template_needs()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);
        var input = new JsonObject
        {
            ["channel"] = "linkedin",
            ["sender"] = new JsonObject { ["external_id"] = "s_34", ["address"] = "https://www.linkedin.com/in/ann-owner" },
            ["message"] = new JsonObject
            {
                ["format"] = "linkedin_message",
                ["compose"] = "template",
                ["body"] = "Thanks for connecting, {{contact.first_name}}.",
            },
        };

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "message.send") with { Context = new JsonObject { ["input"] = input } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input/message", detail.Field);
        Assert.Equal(
            "No alternative this schema allows accepts the value here; the first reported `const` at `/message/format`: "
            + "The only value accepted here is \"email\".",
            detail.Message);
    }

    /// <summary>
    /// A subject that writes a construct the standard refuses is refused at the message, in words that name the
    /// subject's pointer, the construct and the character it starts on — never the subject.
    /// </summary>
    [Fact]
    public async Task A_subject_that_writes_a_helper_is_refused_naming_the_subject_and_where_it_starts()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);
        var input = Message();
        input["message"]!["compose"] = "template";
        input["message"]!["subject"] = "Lisbon {{toLower contact.company}}";
        input["message"]!["body"] = "Hi {{contact.first_name}}, thanks for the chat at the meetup.";

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                ProviderOp(campaign.PublicId, "message.send") with { Context = new JsonObject { ["input"] = input } },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input/message", detail.Field);
        Assert.Equal(
            "No alternative this schema allows accepts the value here; the first reported `format` at `/message/subject`: "
            + "The template here writes a helper or a macro at character 8, which the template standard refuses, because "
            + "it is a provider's own computation, which another provider does not have.",
            detail.Message);
    }

    /// <summary>
    /// A message that does not say it is a template is the final text, as it always was: braces in it are accepted when
    /// the item is written, and stay the plugin's to refuse.
    /// </summary>
    [Fact]
    public async Task A_text_with_braces_is_still_accepted_when_the_item_is_written()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);
        var input = Message();
        input["message"]!["body"] = "Hi {{toLower contact.first_name}}, thanks for the chat.";

        var item = await service.CreateAsync(
            ProviderOp(campaign.PublicId, "message.send") with { Context = new JsonObject { ["input"] = input } },
            Ct);

        Assert.Equal("Hi {{toLower contact.first_name}}, thanks for the chat.", (string?)item.Context["input"]!["message"]!["body"]);
    }

    /// <summary>
    /// What the create and a patch of the arguments make of every published input fixture agrees with what the
    /// fixture says about the arguments, whatever shape a document gives them. A fixture that expects <c>valid</c> is
    /// accepted, and every problem it states under <c>/args</c> is refused at <c>context.input</c>, at the same place
    /// less <c>/args</c>, for the same reason. A problem elsewhere in the composed input — the contact, the campaign,
    /// a rule across the whole of it — is the claim's to find, so a fixture refused only there is accepted when the
    /// item is written. A preview fixture's input is one the schema accepts by definition, and is read as
    /// <c>valid</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublishedInputs))]
    public void The_create_and_a_patch_read_the_arguments_of_every_published_fixture_as_the_fixture_does(string relative)
    {
        var fixture = ReadFixture(relative);
        var operation = (string?)fixture["operation"];
        var contract = OperationCatalog.Find(operation ?? string.Empty);
        Assert.True(contract is not null, $"{relative} is a fixture of `{operation}`, which the catalog does not publish.");

        var arguments = (fixture["input"] as JsonObject)?["args"];
        var stated = StatedAboutTheArguments(fixture);
        var expected = stated.Select(entry => InputField + entry.Pointer).Order(StringComparer.Ordinal).ToList();
        var doors = new (string Name, Action<ValidationErrors> Validate)[]
        {
            ("the create", errors => WorkItemValidation.ValidateProviderOperation(contract.Id, arguments, PatternTiming.Default, errors)),
            ("a patch of the arguments", errors => WorkItemValidation.ValidateProviderInput(contract.Id, arguments, PatternTiming.Default, errors)),
        };

        foreach (var (name, validate) in doors)
        {
            var refused = RefusedAt(validate);
            Assert.True(
                expected.SequenceEqual(refused.Select(detail => detail.Field), StringComparer.Ordinal),
                $"{relative} says the arguments are refused at {Listed(expected)}; {name} refuses them at "
                    + Listed([.. refused.Select(detail => $"{detail.Field} ({detail.Message})")]) + ".");
            Assert.All(refused, detail => Assert.True(detail.Code == "invalid", $"{relative}: {name} reports {detail.Field} as `{detail.Code}`."));
        }

        // Both report every problem as `invalid`, with the reason as the message; the reasons come from the schema they
        // measure the arguments by. Each door must say exactly what that reading says, and where the fixture names the
        // reason at every entry for a place, the reasons there are compared whole.
        var problems = SchemaValidator.Validate(arguments, contract.ArgumentSchema);
        var verdicts = SchemaValidator.Verdicts(problems);
        var said = verdicts.Select(problem => (Field: InputField + problem.Pointer, problem.Message)).Order().ToList();
        foreach (var (name, validate) in doors)
        {
            var reported = RefusedAt(validate).Select(detail => (detail.Field, detail.Message)).Order().ToList();
            Assert.True(
                said.SequenceEqual(reported),
                $"{relative}: {name} says {ListedWithMessages(reported)}; the arguments' schema reads {ListedWithMessages(said)}.");
        }

        foreach (var group in stated.GroupBy(entry => entry.Pointer, StringComparer.Ordinal))
        {
            if (group.Any(entry => entry.Reason is null))
            {
                continue;
            }

            var named = group.Select(entry => entry.Reason!).Order(StringComparer.Ordinal).ToList();
            var reported = problems.Where(problem => problem.Pointer == group.Key).Select(problem => problem.Reason).Order(StringComparer.Ordinal).ToList();
            Assert.True(
                named.SequenceEqual(reported, StringComparer.Ordinal),
                $"{relative} says `{InputField}{group.Key}` fails with {Listed(named)}; the arguments are refused there with {Listed(reported)}.");
        }
    }

    /// <summary>
    /// Every operation shows at least one set of arguments its schema accepts, so the row above proves, for each,
    /// that arguments the claim would accept are accepted when the item is written.
    /// </summary>
    [Fact]
    public void Every_published_operation_shows_arguments_its_schema_accepts()
    {
        var shown = PublishedInputFiles()
            .Select(ReadFixture)
            .Where(fixture => fixture["expect"] is JsonObject { } expect ? expect["invalid"] is null : (string?)fixture["expect"] == "valid")
            .Select(fixture => (string?)fixture["operation"])
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(OperationCatalog.All, contract => Assert.True(
            shown.Contains(contract.Id),
            $"{contract.Id} publishes no input fixture that expects `valid` and no preview fixture, so nothing shows its arguments accepted at create."));
    }

    /// <summary>Every input fixture and every preview fixture of every operation the catalog publishes.</summary>
    public static TheoryData<string> PublishedInputs()
    {
        var data = new TheoryData<string>();
        foreach (var relative in PublishedInputFiles())
        {
            data.Add(relative);
        }

        return data;
    }

    private const string InputField = "context.input";

    private static IEnumerable<string> PublishedInputFiles()
    {
        var root = Publication.RepositoryRoot();
        foreach (var contract in OperationCatalog.All)
        {
            var directory = Path.Combine(root, "docs", "contracts", "fixtures", contract.Id);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("input-", StringComparison.Ordinal) || name.StartsWith("preview-", StringComparison.Ordinal))
                {
                    yield return Path.GetRelativePath(root, file).Replace('\\', '/');
                }
            }
        }
    }

    /// <summary>
    /// What a fixture states about the arguments: each entry of its <c>invalid</c> list at <c>/args</c> or under it,
    /// as a pointer into the arguments themselves, with the reason where the fixture names one.
    /// </summary>
    private static List<(string Pointer, string? Reason)> StatedAboutTheArguments(JsonObject fixture)
    {
        const string Arguments = "/args";
        if (fixture["expect"] is not JsonObject expect || expect["invalid"] is not JsonArray stated)
        {
            return [];
        }

        var entries = new List<(string Pointer, string? Reason)>();
        foreach (var entry in stated)
        {
            var (pointer, reason) = entry is JsonObject named ? ((string)named["pointer"]!, (string?)named["reason"]) : ((string)entry!, null);
            if (pointer == Arguments || pointer.StartsWith(Arguments + "/", StringComparison.Ordinal))
            {
                entries.Add((pointer[Arguments.Length..], reason));
            }
        }

        return entries;
    }

    /// <summary>What one door refuses, by field; a request answered as one to send again is no reading at all.</summary>
    private static List<ErrorDetail> RefusedAt(Action<ValidationErrors> validate)
    {
        var errors = new ValidationErrors();
        validate(errors);
        var answer = Record.Exception(errors.ThrowIfAny);
        if (answer is not null and not ValidationException)
        {
            Assert.Fail($"The arguments were not measured: {answer.Message}");
        }

        return [.. ((answer as ValidationException)?.Details ?? []).OrderBy(detail => detail.Field, StringComparer.Ordinal)];
    }

    private static string ListedWithMessages(IReadOnlyList<(string Field, string Message)> entries) =>
        Listed([.. entries.Select(entry => $"{entry.Field}: {entry.Message}")]);

    private static string Listed(IReadOnlyList<string> entries) =>
        entries.Count == 0 ? "nothing" : string.Join(", ", entries.Select(entry => "`" + entry + "`"));

    private static JsonObject ReadFixture(string relative) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), relative)))!.AsObject();

    /// <summary>
    /// A read whose contact is optional is the same work with a person or without one, so creation takes either and
    /// measures the arguments the same way.
    /// </summary>
    [Fact]
    public async Task A_read_whose_contact_is_optional_is_created_with_a_person_or_without_one()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var contact = new Contact
        {
            PublicId = Jason.Contracts.Ids.PublicId.New("cnt"),
            FirstName = "Marta",
            CreatedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        };
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact
        {
            Campaign = campaign,
            Contact = contact,
            State = MembershipState.Enrolled,
            AddedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        });
        db.SaveChanges();
        var service = NewService(db);
        var listing = new JsonObject { ["input"] = new JsonObject { ["limit"] = 20, ["offset"] = 0, ["source"] = "inbound" } };

        var aboutTheCampaign = await service.CreateAsync(ProviderOp(campaign.PublicId, "conversation.list") with { Context = listing }, Ct);
        var aboutThePerson = await service.CreateAsync(
            ProviderOp(campaign.PublicId, "conversation.list") with { ContactId = contact.PublicId, Context = (JsonObject)listing.DeepClone() },
            Ct);

        Assert.Null(aboutTheCampaign.ContactId);
        Assert.Equal(contact.PublicId, aboutThePerson.ContactId);
        Assert.Equal("inbound", (string?)aboutThePerson.Context["input"]!["source"]);
    }

    [Fact]
    public async Task An_ai_role_item_carries_whatever_its_planner_wrote()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var item = await service.CreateAsync(
            new WorkItemCreateRequest(
                campaign.PublicId,
                WorkItemKind.AiRole,
                "researcher",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new JsonObject { ["input"] = "whatever the brief says" },
                null,
                null,
                null),
            Ct);

        Assert.Equal("whatever the brief says", (string?)item.Context["input"]);
    }

    [Fact]
    [Critical(Family.R3, "context.input at update")]
    public async Task Rewriting_the_arguments_is_measured_against_the_items_own_operation()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);
        var replacement = Membership();
        replacement["channel"] = 7;

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { Set = new JsonObject { ["input"] = replacement } }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input/channel", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task Arguments_the_contract_accepts_replace_the_ones_the_item_held()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);
        var replacement = Membership();
        replacement["list"]!["external_id"] = "lst_9";

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { Set = new JsonObject { ["input"] = replacement } }, Ct);

        Assert.Equal("lst_9", (string?)updated.Context["input"]!["list"]!["external_id"]);
    }

    /// <summary>
    /// A patch that rewrites the arguments is measured by the same reading as the create, shared definitions
    /// included: a sender the definition accepts is kept, and one it refuses is refused inside the sender.
    /// </summary>
    [Fact]
    public async Task Rewritten_arguments_that_name_a_shared_definition_replace_the_ones_the_item_held()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "message.send", Message());
        var service = NewService(db);
        var replacement = Message();
        replacement["sender"]!["external_id"] = "s_14";

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { Set = new JsonObject { ["input"] = replacement } }, Ct);

        Assert.Equal("s_14", (string?)updated.Context["input"]!["sender"]!["external_id"]);
    }

    [Fact]
    public async Task A_rewritten_sender_its_shared_definition_refuses_is_reported_inside_the_sender()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "message.send", Message());
        var service = NewService(db);
        var replacement = Message();
        replacement["sender"]!["external_id"] = 14;

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { Set = new JsonObject { ["input"] = replacement } }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.True(detail.Field == "context.input/sender/external_id", $"The sender was refused at {detail.Field}: {detail.Message}");
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task Removing_the_arguments_an_operation_needs_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { Unset = ["input"] }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task Writing_nothing_into_the_reserved_key_is_the_same_as_taking_the_arguments_away()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { Set = new JsonObject { ["input"] = null } }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task An_unset_wins_over_a_set_of_the_same_key_exactly_as_the_patch_applies_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", Membership());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(
                Patch(item.PublicId) with { Set = new JsonObject { ["input"] = Membership() }, Unset = ["input"] },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("context.input", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task A_patch_that_leaves_the_arguments_alone_is_not_measured_again()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "list_membership.add", input: null);
        var service = NewService(db);

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { Priority = Optional<int>.Of(5) }, Ct);

        Assert.Equal(5, updated.Priority);
    }

    [Fact]
    public async Task An_ai_role_items_context_is_still_its_own_business()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var item = WorkItemFactory.NewAiRole(campaign);
        db.WorkItems.Add(item);
        db.SaveChanges();
        var service = NewService(db);

        var updated = await service.UpdateAsync(
            Patch(item.PublicId) with { Set = new JsonObject { ["input"] = "a sentence, not arguments" } },
            Ct);

        Assert.Equal("a sentence, not arguments", (string?)updated.Context["input"]);
    }

    [Fact]
    public async Task An_item_whose_operation_this_build_no_longer_publishes_is_left_to_fail_at_the_claim()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = SeedProviderOp(db, "contacts.enroll", input: null);
        var service = NewService(db);

        var updated = await service.UpdateAsync(
            Patch(item.PublicId) with { Set = new JsonObject { ["input"] = new JsonObject { ["campaign"] = "cmp_provider_7" } } },
            Ct);

        Assert.Equal("cmp_provider_7", (string?)updated.Context["input"]!["campaign"]);
    }

    private static WorkItemService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    private static WorkItemCreateRequest ProviderOp(string? campaignId, string operation) =>
        new(campaignId, WorkItemKind.ProviderOp, null, operation, null, null, null, null, null, null, null, null, null, null, null, null);

    private static WorkItemUpdateRequest Patch(string workItemId) =>
        new(workItemId, null, null, default, default, default, default, default, default, default, default, null, null);

    /// <summary>A complete set of <c>list_membership.add</c> arguments, as a planner would write them.</summary>
    private static JsonObject Membership() => new()
    {
        ["list"] = new JsonObject { ["external_id"] = "lst_7" },
        ["channel"] = "email",
    };

    /// <summary>A complete set of <c>message.send</c> arguments: an email, from an account <c>sender.list</c> listed.</summary>
    private static JsonObject Message() => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "s_12", ["address"] = "sales@example.com" },
        ["message"] = new JsonObject
        {
            ["format"] = "email",
            ["subject"] = "The Lisbon meetup",
            ["body"] = "Hi Marta, thanks for the chat at the meetup.",
        },
    };

    private static Campaign Seed(JasonDbContext db, Campaign campaign)
    {
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign;
    }

    private static WorkItem SeedProviderOp(JasonDbContext db, string operation, JsonObject? input)
    {
        var item = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(), operation);
        if (input is not null)
        {
            item.Context = new JsonObject { ["input"] = input };
        }

        db.WorkItems.Add(item);
        db.SaveChanges();
        return item;
    }
}
