using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Everything that can stop a provider work item, decided before a child process exists — and decided in one
/// order, because a manager reading the failure should meet the reason the item cannot run at all before the
/// reasons that need a planner's attention.
/// </summary>
public partial class ProviderOpPreflightTests
{
    private const string Add = "list_membership.add";
    private const string Enroll = "campaign.enroll";
    private const string Get = "campaign.get";
    private const string Threads = "conversation.list";
    private const string Optout = "optout.record";
    private const string Stop = "enrollment.stop";
    private const string Delete = "contact.delete";
    private const string Pause = "campaign.pause";
    private const string Activate = "campaign.activate";
    private const string Send = "message.send";

    /// <summary>
    /// The order, proven by repairing one thing at a time: everything is wrong at the start, and each repair
    /// reveals exactly the next check. A test per code would say that each one can fail; only this one says which
    /// of them wins when several are true at once, which is what a person reading the item actually gets.
    /// </summary>
    [Fact]
    [Critical(Family.W3, "the twenty-six checks' order")]
    public void The_first_failing_check_is_the_one_that_names_the_failure()
    {
        var met = new List<string>();
        var scenario = new Scenario { Operation = "nothing.published" };
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Operation = Add;
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Route = new Route("ghost-provider", null, null);
        met.Add(Refused(scenario, FailureClass.Permanent));

        // From here the route names the package that is installed, and the package itself is what is wrong.
        scenario.Route = Scenario.ToTheInstalledPackage;
        scenario.Plugin = Scenario.Package(status: PluginStatus.Unavailable);
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Plugin = Scenario.Package(operations: []);
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Plugin = Scenario.Package(contractVersions: [2]);
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Plugin = Scenario.Package(binding: Scenario.BindingSchema);
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Route = new Route(Scenario.PluginId, new JsonObject { ["workspace"] = "west" }, "sha256:west");

        // From here the work is a delete, which works by the pin alone, and its arguments also name the contact to
        // destroy, which its schema refuses: the person's checks come first, the person's pin among them, and only
        // then the document.
        scenario.Operation = Delete;
        scenario.Arguments = new JsonObject { ["channel"] = "email", ["contact"] = new JsonObject { ["external_id"] = "48211" } };
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Contact = Scenario.Person(("phone", "+15555550100"));
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Contact = Scenario.Person(("email", "ada@example.test"));
        scenario.Suppressed = true;
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Suppressed = false;
        met.Add(Refused(scenario, FailureClass.Permanent));

        // Twelfth to fourteenth: a read of a send acts by the reference the send's own attempt returned, so it is asked before the input
        // that carries the reference is composed — a send the campaign does not hold, then one that kept no reference, then
        // one made under another account than this route's.
        var deleting = (scenario.Operation, scenario.Plugin, scenario.Arguments);
        scenario.Operation = Status;
        scenario.Plugin = Scenario.Package(operations: [Add, Enroll, Get, Delete, Status], binding: Scenario.BindingSchema);
        scenario.Arguments = new JsonObject { ["send"] = SendItem };
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Named = Made() with { Reference = null };
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Named = Made();
        met.Add(Refused(scenario, FailureClass.Permanent));

        // Fifteenth to seventeenth: the same three questions of a search a status read names, by the reference the search's
        // own attempt returned — a search the campaign does not hold, then one that kept no reference, as one the provider
        // finished inside its own call keeps none, then one made under another account than this route's.
        scenario.Named = null;
        scenario.Operation = SearchStatus;
        scenario.Plugin = Scenario.Package(operations: [Add, Enroll, Get, Delete, SearchStatus], binding: Scenario.BindingSchema);
        scenario.Arguments = new JsonObject { ["search"] = SearchItem, ["limit"] = 25 };
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Searched = Started() with { Reference = null };
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Searched = Started();
        met.Add(Refused(scenario, FailureClass.Permanent));

        // Eighteenth: a search of an audience named by its number, which the campaign does not hold, before the input that
        // would carry that audience's definition is composed.
        scenario.Searched = null;
        scenario.Operation = Search;
        scenario.Plugin = Scenario.Searching(binding: Scenario.BindingSchema);
        scenario.Arguments = new JsonObject { ["audience"] = 3, ["count"] = 50 };
        met.Add(Refused(scenario, FailureClass.Permanent));

        (scenario.Operation, scenario.Plugin, scenario.Arguments) = deleting;
        scenario.ContactPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "48211" };
        met.Add(Refused(scenario, FailureClass.Validation));
        Assert.Equal("/args/contact", Assert.Single(scenario.Check().Details!).Field);

        // Twentieth: a definition that is not the routed plugin's shape, asked once the envelope around it satisfies the
        // operation's own schema and before anybody is asked to approve a search the provider would refuse; it points into
        // the definition as the envelope's refusals point into the input.
        scenario.Operation = Search;
        scenario.Plugin = Scenario.Searching(binding: Scenario.BindingSchema);
        scenario.Arguments = new JsonObject { ["definition"] = new JsonObject { ["industries"] = "Software" }, ["count"] = 50 };
        met.Add(Refused(scenario, FailureClass.Validation));
        Assert.Equal("/definition/industries", Assert.Single(scenario.Check().Details!).Field);

        // Twenty-first: an argument the operation's document names per channel, on a channel the routed plugin's manifest
        // does not list it for. It is asked once the document satisfies its schema, so a moment that is no moment is the
        // planner's to fix first, and before anybody is asked to approve a send that could not go out as written.
        scenario.Operation = Send;
        scenario.Plugin = Scenario.Package(operations: [Add, Enroll, Get, Delete, Send], binding: Scenario.BindingSchema);
        scenario.Arguments = Scenario.Sending("snd-1").Arguments;
        scenario.Arguments!["send_after"] = Later;
        met.Add(Refused(scenario, FailureClass.Permanent));

        // Twenty-second: a message written as a template on a channel the routed plugin's manifest does not list under
        // `templates`, asked beside the twenty-first and before anybody is asked to approve a message the plugin would not
        // write.
        scenario.Arguments.Remove("send_after");
        scenario.Arguments["message"]!["compose"] = "template";
        met.Add(Refused(scenario, FailureClass.Permanent));

        // Twenty-third: a repeat of a send an earlier attempt of which lost its answer, under a plugin whose manifest does
        // not say it settles the send by the work item's key. The refusal keeps the class of the attempt that may have
        // acted, and it is asked before anybody is asked to approve a repeat that could reach the person twice.
        scenario.Arguments["message"]!["compose"] = "text";
        scenario.Unsettled = true;
        met.Add(Refused(scenario, FailureClass.Ambiguous));

        // Twenty-fourth, and the first that is not a refusal: a search while another of its campaign has not ended waits
        // behind it, and it is asked before the approval, so nobody is asked to approve a spend that cannot start now.
        scenario.Unsettled = false;
        scenario.Operation = Search;
        scenario.Plugin = Scenario.Searching(binding: Scenario.BindingSchema);
        scenario.Arguments = new JsonObject { ["definition"] = new JsonObject { ["industries"] = new JsonArray("Software") }, ["count"] = 50 };
        scenario.SearchInTheWay = SearchItem;
        var behind = scenario.Check();
        Assert.True(behind.Parks);
        Assert.False(behind.Refused);
        Assert.NotNull(behind.Plan);
        Assert.Null(behind.Class);
        Assert.Contains(SearchItem, behind.Message, StringComparison.Ordinal);
        met.Add(behind.Code!);
        scenario.SearchInTheWay = null;

        // Twenty-fifth: an operation a person has to confirm is parked rather than failed, and it is asked here — after the
        // input it would be approved for has been composed and held to its own schema — rather than before any of that was
        // known.
        scenario.Unsettled = true;
        scenario.Operation = Enroll;
        scenario.Arguments = Scenario.CompleteEnrollment;
        var parked = scenario.Check();
        Assert.True(parked.Parks);
        Assert.False(parked.Refused);
        Assert.NotNull(parked.Plan);
        Assert.Null(parked.Class);
        met.Add(parked.Code!);

        // The unsettled attempt stays, and asks nothing of an operation whose repeat does not rest on the key.
        // Twenty-sixth, and last: work nobody has to confirm, which a block the provider holds on this account still
        // parks. It is asked after the approval because it says nothing about the work, so the block travels on the
        // approval's park as well, and work a person releases still waits for it.
        var block = Scenario.Block(binding: "sha256:west");
        scenario.Blocks = [block];
        Assert.Same(block, scenario.Check().Block);
        scenario.Operation = Pause;
        scenario.Plugin = Scenario.Package(operations: [Add, Enroll, Get, Delete, Pause], binding: Scenario.BindingSchema);
        scenario.Arguments = null;
        scenario.CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" };
        var held = scenario.Check();
        Assert.True(held.Parks);
        Assert.False(held.Refused);
        Assert.NotNull(held.Plan);
        Assert.Null(held.Class);
        Assert.Same(block, held.Block);
        met.Add(held.Code!);

        // Every check in the published order, met one at a time, and then an item with nothing left wrong.
        Assert.Equal(ProviderOpPreflight.Codes, met);

        scenario.Blocks = null;
        var verdict = scenario.Check();
        Assert.True(verdict.Passed);
        Assert.Null(verdict.Code);
        Assert.Null(verdict.Class);
    }

    /// <summary>
    /// What the gate reads is the published value and nothing beside it. `campaign.enroll` states its approval
    /// as a conditional block — `confirm_once`, with a condition that only decides whether a preview is
    /// *mandatory* — and a conditional block always states the dangerous reading in `value`. Jason builds a
    /// preview every time, so there is nothing here for a condition to soften, and no condition is evaluated:
    /// the operation whose value is `confirm_once` parks, the operations whose value is `auto` run.
    /// </summary>
    [Fact]
    [Critical(Family.S2, "the conditional approval reads the dangerous value")]
    public void The_gate_reads_the_dangerous_value_and_never_the_condition_beside_it()
    {
        // The case is a real one: this operation's approval is published as a condition, not as a plain value.
        var enroll = OperationCatalog.Find(Enroll)!;
        Assert.True(enroll.Approval.Conditional);
        Assert.Equal("confirm_once", enroll.Approval.Value);
        Assert.NotNull(enroll.Approval.Detail);

        var parked = new Scenario
        {
            Operation = Enroll,
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteEnrollment,
        }.Check();

        Assert.True(parked.Parks);
        Assert.Equal(AttemptErrors.ApprovalRequired, parked.Code);

        // And the two whose value is `auto` are the one approval a runtime may give itself.
        Assert.Equal("auto", OperationCatalog.Find(Add)!.Approval.Value);
        Assert.Equal("auto", OperationCatalog.Find(Get)!.Approval.Value);
        var runs = new Scenario
        {
            Operation = Add,
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
        }.Check();

        Assert.True(runs.Passed);
        Assert.False(runs.Parks);
    }

    /// <summary>
    /// None of the twenty-six is worth another attempt: nothing about the work changed between two scans, so an item
    /// released back into the queue would fail the same way for as long as the queue existed. The claim says so
    /// itself — it fails a refused item with <c>retriable: false</c> — and this is the table agreeing, so that a
    /// code later added to both places cannot come to mean two different things.
    /// </summary>
    [Fact]
    [Critical(Family.W3, "pre-flight refusals are final")]
    public void No_reason_the_pre_flight_gives_is_ever_worth_another_attempt() =>
        Assert.All(ProviderOpPreflight.Codes, code => Assert.DoesNotContain(code, FailureClassifier.RetriableCodes));

    /// <summary>
    /// The one answer three of the checks give that is not a refusal is named beside the twenty-six rather than among
    /// them: the list is the published order of reasons an item cannot run, and a pattern that did not finish is not one.
    /// What the binding's, the input's and the definition's checks answer when their pattern does not finish is that named
    /// outcome, and nothing else.
    /// </summary>
    [Fact]
    public void The_claim_s_transient_outcome_is_what_checks_7_19_and_20_answer_and_is_not_among_the_twenty_five()
    {
        var schema = Scenario.BindingSchema;
        schema["properties"]!["workspace"]!["pattern"] = "^[a-z]+$";
        var atTheBinding = new Scenario
        {
            Route = new Route(Scenario.PluginId, new JsonObject { ["workspace"] = "west" }, "sha256:west"),
            Plugin = Scenario.Package(binding: schema),
            Timing = TimingOut.Always(),
        }.Check();
        var atTheInput = new Scenario
        {
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
            Timing = TimingOut.Always(),
        }.Check();

        var definition = Scenario.Definition;
        definition.Schema["properties"]!["industries"]!["items"]!["pattern"] = "^[A-Za-z ]+$";
        var atTheDefinition = new Scenario
        {
            Operation = Search,
            Route = Scenario.ToTheInstalledPackage,
            Plugin = Scenario.Searching(definition: definition),
            Arguments = new JsonObject { ["definition"] = new JsonObject { ["industries"] = new JsonArray("Software") }, ["count"] = 50 },
            Timing = TimingOut.Always(value => value == "Software"),
        }.Check();

        Assert.Equal(
            [ProviderOpPreflight.TransientOutcome, ProviderOpPreflight.TransientOutcome, ProviderOpPreflight.TransientOutcome],
            [atTheBinding.Code, atTheInput.Code, atTheDefinition.Code]);
        Assert.DoesNotContain(ProviderOpPreflight.TransientOutcome, ProviderOpPreflight.Codes);
    }

    /// <summary>
    /// An argument a document names per channel reaches the plugin only where its manifest lists it for the channel the
    /// arguments name. Anywhere else the item is refused before a person is asked to approve it, and the refusal says
    /// what to do instead: leave the argument out and wait with the item, or route it to a plugin that takes it.
    /// </summary>
    [Theory]
    [InlineData(null, AttemptErrors.ArgumentUnsupported)]
    [InlineData("linkedin", AttemptErrors.ArgumentUnsupported)]
    [InlineData("email", null)]
    [InlineData("linkedin,email", null)]
    public void A_moment_reaches_the_plugin_only_on_a_channel_its_manifest_lists_it_for(string? listed, string? expected)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Arguments!["send_after"] = Later;
        scenario.Plugin = Scenario.Taking(listed?.Split(',') ?? []);

        var verdict = scenario.Check();

        Assert.Equal(expected ?? AttemptErrors.ApprovalRequired, verdict.Code);
        if (expected is not null)
        {
            Assert.True(verdict.Refused);
            Assert.Equal(FailureClass.Permanent, verdict.Class);
            Assert.NotNull(verdict.Resolution);
            Assert.Equal(
                $"'{Scenario.PluginId}' takes no 'send_after' for 'message.send' on the 'email' channel: its manifest does not list it there. Leave the argument out and set not_before to the moment, or route the operation to a plugin that takes it.",
                verdict.Message);
        }
    }

    /// <summary>
    /// A message written as a template reaches the plugin only on a channel its manifest lists for the send under
    /// <c>templates</c>, and an absent key means none. Anywhere else the item is refused before a person is asked to
    /// approve it, and the refusal says what to do instead: write the message as final text, or route it to a plugin
    /// that writes a template there. A final message asks nothing of the list.
    /// </summary>
    [Theory]
    [InlineData("email", null, "template", AttemptErrors.TemplateNotOffered)]
    [InlineData("email", "linkedin", "template", AttemptErrors.TemplateNotOffered)]
    [InlineData("linkedin", "email", "template", AttemptErrors.TemplateNotOffered)]
    [InlineData("email", "email", "template", null)]
    [InlineData("linkedin", "email,linkedin", "template", null)]
    [InlineData("email", null, "text", null)]
    public void A_template_reaches_the_plugin_only_on_a_channel_its_manifest_lists_it_for(string channel, string? listed, string compose, string? expected)
    {
        var scenario = Scenario.SendingATemplate(channel, channel == "email" ? "Hello" : null, "Hello, {{contact.first_name}}.");
        scenario.Arguments!["message"]!["compose"] = compose;
        scenario.Plugin = Scenario.Writing(listed?.Split(',') ?? []);

        var verdict = scenario.Check();

        Assert.Equal(expected ?? AttemptErrors.ApprovalRequired, verdict.Code);
        if (expected is not null)
        {
            Assert.True(verdict.Refused);
            Assert.Equal(FailureClass.Permanent, verdict.Class);
            Assert.NotNull(verdict.Resolution);
            Assert.Equal(
                $"'{Scenario.PluginId}' writes no template of 'message.send' on the '{channel}' channel: its manifest does not list the channel under templates. Write the message as final text, or route the operation to a plugin that writes a template there.",
                verdict.Message);
        }
    }

    /// <summary>
    /// A template may name the sending account's address on LinkedIn as on an email: the input holds it at
    /// <c>/args/sender/address</c> on both, and on LinkedIn it is the account's profile. The claim reads no name of a
    /// template, so the send waits for its approval on either channel, the address in its subject or in its text.
    /// </summary>
    [Theory]
    [InlineData("linkedin", false, "Hi {{contact.first_name}}, my profile is {{sender.address}}.")]
    [InlineData("linkedin", true, "{{#if contact.company}}From {{sender.address | 'my profile'}}{{/if}}")]
    [InlineData("email", false, "Hi {{contact.first_name}}, write to me at {{sender.address}}.")]
    public void A_template_naming_the_sending_account_s_address_is_claimed_on_either_channel(string channel, bool inTheSubject, string template)
    {
        var scenario = Scenario.SendingATemplate(
            channel,
            inTheSubject ? template : channel == "email" ? "Hello" : null,
            inTheSubject ? "Hello." : template);

        Assert.Equal(AttemptErrors.ApprovalRequired, scenario.Check().Code);
    }

    /// <summary>
    /// A repeat of a send whose earlier attempt lost its answer is made only under a plugin whose manifest lists the send
    /// under <c>settles_by_key</c>; under any other it is refused at the claim, ambiguous and final, with the attempt
    /// kept, and nothing runs. A send no earlier attempt left unsettled asks nothing of the marker.
    /// </summary>
    [Theory]
    [InlineData(true, false, AttemptErrors.ProviderAnswerLost)]
    [InlineData(true, true, null)]
    [InlineData(false, false, null)]
    public void A_send_repeated_after_a_lost_answer_is_handed_only_to_a_plugin_that_settles_it_by_the_key(bool unsettled, bool listed, string? expected)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Unsettled = unsettled;
        if (listed)
        {
            scenario.Plugin = scenario.Plugin with { Manifest = scenario.Plugin.Manifest with { KeyedOperations = [Send] } };
        }

        var verdict = scenario.Check();

        Assert.Equal(expected ?? AttemptErrors.ApprovalRequired, verdict.Code);
        if (expected is not null)
        {
            Assert.True(verdict.Refused);
            Assert.Equal(FailureClass.Ambiguous, verdict.Class);
            Assert.Null(verdict.Plan);
            Assert.NotNull(verdict.Resolution);
            Assert.Contains("settles_by_key", verdict.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The work item's key settles a send only at the provider and the account that took it: a repeat after a lost
    /// answer is handed to the package, by its id and its digest, and the binding the attempt that lost it ran under,
    /// by its provenance, and to no other, even one that lists the send under <c>settles_by_key</c>, since that provider
    /// never saw the key, and another version of the package may keep another scheme of keys. An attempt whose
    /// provenance does not say where it ran leaves nothing to compare, so the repeat is refused.
    /// </summary>
    [Theory]
    [InlineData(Scenario.PluginId, Scenario.RoutedDigest, null, null)]
    [InlineData(Scenario.PluginId, Scenario.RoutedDigest, "acct-2", AttemptErrors.ProviderAnswerLost)]
    [InlineData("another-provider", Scenario.RoutedDigest, null, AttemptErrors.ProviderAnswerLost)]
    [InlineData(Scenario.PluginId, "sha256:1", null, AttemptErrors.ProviderAnswerLost)]
    [InlineData(null, null, null, AttemptErrors.ProviderAnswerLost)]
    public void A_send_repeated_after_a_lost_answer_goes_only_to_the_provider_and_account_that_took_it(string? ranUnder, string? digest, string? binding, string? expected)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = scenario.Plugin with { Manifest = scenario.Plugin.Manifest with { KeyedOperations = [Send] } };
        Assert.Equal(Scenario.RoutedDigest, scenario.Plugin.Digest);
        scenario.Unsettled = true;
        scenario.UnsettledUnder = ranUnder is null ? Scenario.NoProvenance : (ranUnder, digest, binding);

        var verdict = scenario.Check();

        Assert.Equal(expected ?? AttemptErrors.ApprovalRequired, verdict.Code);
        if (expected is not null)
        {
            Assert.True(verdict.Refused);
            Assert.Equal(FailureClass.Ambiguous, verdict.Class);
            Assert.Null(verdict.Plan);
            Assert.NotNull(verdict.Resolution);
            Assert.Contains(ranUnder is null ? "does not say" : "only at the provider and the account that took it", verdict.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The work item's key settles only the request it went with: a provider holding the key answers a repeat with the
    /// send the first request made, whatever the repeat asks for. So a repeat after a lost answer is made only with the
    /// arguments that attempt ran with, as its snapshot kept them; an item edited since — another subject, another
    /// moment, an argument added — is refused, ambiguous and final, since a repeat would answer for a message the
    /// person never got. The same arguments, in another order of their keys, are the same.
    /// </summary>
    [Theory]
    [InlineData("the same arguments", null)]
    [InlineData("the same arguments in another order", null)]
    [InlineData("another subject", AttemptErrors.ProviderAnswerLost)]
    [InlineData("a moment added", AttemptErrors.ProviderAnswerLost)]
    [InlineData("no arguments kept", AttemptErrors.ProviderAnswerLost)]
    public void A_send_repeated_after_a_lost_answer_goes_only_with_the_arguments_that_attempt_ran_with(string earlier, string? expected)
    {
        var scenario = Scenario.Sending("snd-1");
        var taking = Scenario.Taking(["email"]);
        scenario.Plugin = taking with { Manifest = taking.Manifest with { KeyedOperations = [Send] } };
        scenario.Unsettled = true;
        var ran = scenario.Arguments!.DeepClone().AsObject();
        switch (earlier)
        {
            case "the same arguments":
                break;
            case "the same arguments in another order":
                ran = new JsonObject { ["message"] = ran["message"]!.DeepClone(), ["sender"] = ran["sender"]!.DeepClone(), ["channel"] = "email" };
                break;
            case "another subject":
                ran["message"]!["subject"] = "Hello again";
                break;
            case "a moment added":
                scenario.Arguments!["send_after"] = Later;
                break;
            case "no arguments kept":
                ran = null;
                break;
        }

        scenario.UnsettledArguments = ran is null ? new JsonObject() : new JsonObject { [WorkItemService.InputKey] = ran };

        var verdict = scenario.Check();

        Assert.Equal(expected ?? AttemptErrors.ApprovalRequired, verdict.Code);
        if (expected is not null)
        {
            Assert.True(verdict.Refused);
            Assert.Equal(FailureClass.Ambiguous, verdict.Class);
            Assert.Null(verdict.Plan);
            Assert.Contains("other arguments than the work item carries now", verdict.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A search spends per person found and a blind repeat would start a second that spends again, so a repeat after a lost
    /// answer is held to the attempt that may have started it: the plugin by its id, the account, and the arguments and the
    /// input that attempt ran with. A route moved to another account, a count or a definition edited while the item
    /// waited: each ends the repeat ambiguous and final rather than start a second search that nothing could find, read or
    /// stop; a route gone since keeps that class too. The same request goes on to the person, and so does one after the
    /// package was updated in between: a metered operation's recovery read finds what the earlier attempt did at the
    /// account, whichever version of the package made it, unlike a send's key, which only the version that kept it reads.
    /// An enrollment and an activation are metered too, and their contracts ask for a recovery read before a repeat, so the
    /// same hold keeps them from enrolling the person, or starting the campaign, a second time at another account or for
    /// another request.
    /// </summary>
    [Theory]
    [InlineData(Search, "the same request", null)]
    [InlineData(Search, "another account", AttemptErrors.ProviderAnswerLost)]
    [InlineData(Search, "another package version", null)]
    [InlineData(Search, "another count", AttemptErrors.ProviderAnswerLost)]
    [InlineData(Search, "another definition", AttemptErrors.ProviderAnswerLost)]
    [InlineData(Search, "no route", AttemptErrors.NoRoute)]
    [InlineData(Enroll, "the same request", null)]
    [InlineData(Enroll, "another account", AttemptErrors.ProviderAnswerLost)]
    [InlineData(Enroll, "another package version", null)]
    [InlineData(Enroll, "another start", AttemptErrors.ProviderAnswerLost)]
    [InlineData(Activate, "the same request", null)]
    [InlineData(Activate, "another account", AttemptErrors.ProviderAnswerLost)]
    [InlineData(Activate, "another package version", null)]
    [InlineData(Activate, "another population", AttemptErrors.ProviderAnswerLost)]
    public void A_metered_repeat_after_a_lost_answer_goes_only_where_and_as_that_attempt_acted(string operation, string since, string? expected)
    {
        var searching = operation == Search;
        var enrolling = operation == Enroll;
        var scenario = new Scenario
        {
            Operation = operation,
            Route = Scenario.ToTheInstalledPackage,
            Plugin = searching ? Scenario.Searching() : Scenario.Package(operations: [Add, Enroll, Get, Delete, Activate]),
            Contact = enrolling ? Scenario.Person(("email", "ada@example.test")) : null,
            Arguments = operation switch
            {
                Search => new JsonObject { ["definition"] = new JsonObject { ["industries"] = new JsonArray("Software") }, ["count"] = 50 },
                Enroll => Scenario.CompleteEnrollment,
                _ => new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "sq-1129" }, ["expected"] = new JsonObject { ["enrolled"] = 12 } },
            },
            Unsettled = true,
        };
        var ran = scenario.Arguments.DeepClone().AsObject();
        switch (since)
        {
            case "another account":
                scenario.UnsettledUnder = (Scenario.PluginId, Scenario.RoutedDigest, "sha256:east");
                break;
            case "another package version":
                scenario.UnsettledUnder = (Scenario.PluginId, "sha256:1", null);
                break;
            case "another count":
                ran["count"] = 25;
                break;
            case "another definition":
                ran["definition"] = new JsonObject { ["industries"] = new JsonArray("Logistics") };
                break;
            case "another start":
                ran["start"] = new JsonObject { ["position"] = "step", ["step"] = 2 };
                break;
            case "another population":
                ran["expected"] = new JsonObject { ["enrolled"] = 11 };
                break;
            case "no route":
                scenario.Route = null;
                break;
        }

        scenario.UnsettledArguments = new JsonObject { [WorkItemService.InputKey] = ran };

        var verdict = scenario.Check();

        Assert.Equal(expected ?? AttemptErrors.ApprovalRequired, verdict.Code);
        if (expected is not null)
        {
            Assert.Equal(FailureClass.Ambiguous, verdict.Class);
            Assert.True(verdict.Refused);
            Assert.Null(verdict.Plan);
        }
    }

    /// <summary>
    /// While a send carries an earlier attempt that may have reached the person, every reason the claim refuses it for,
    /// from the route to the arguments, keeps its own code and says that the earlier attempt may have acted: ambiguous
    /// and final, as that attempt left the work. Read alone, "the route moved" or "the moment is not taken" would have a
    /// planner create the send again. The same refusal of a send nothing left unsettled keeps its own class and words.
    /// </summary>
    [Theory]
    [InlineData("no route", AttemptErrors.NoRoute, FailureClass.Permanent)]
    [InlineData("a plugin not loaded", AttemptErrors.PluginNotLoaded, FailureClass.Permanent)]
    [InlineData("a plugin unavailable", AttemptErrors.PluginUnavailable, FailureClass.Permanent)]
    [InlineData("a plugin without the send", AttemptErrors.PluginOperationUnsupported, FailureClass.Permanent)]
    [InlineData("another contract version", AttemptErrors.ContractIncompatible, FailureClass.Permanent)]
    [InlineData("a binding the plugin refuses", AttemptErrors.BindingInvalid, FailureClass.Permanent)]
    [InlineData("no contact", AttemptErrors.ContactRequired, FailureClass.Permanent)]
    [InlineData("no email", AttemptErrors.NoChannelValue, FailureClass.Permanent)]
    [InlineData("a suppressed address", AttemptErrors.Suppressed, FailureClass.Permanent)]
    [InlineData("no sender", AttemptErrors.InputInvalid, FailureClass.Validation)]
    [InlineData("a moment the plugin does not take", AttemptErrors.ArgumentUnsupported, FailureClass.Permanent)]
    [InlineData("a template the plugin does not write", AttemptErrors.TemplateNotOffered, FailureClass.Permanent)]
    public void Every_refusal_of_a_send_that_may_have_reached_the_person_says_so_and_stays_final(string wrong, string code, FailureClass own)
    {
        var settled = Mistaken(wrong);
        var unsettled = Mistaken(wrong);
        unsettled.Unsettled = true;

        var plain = settled.Check();
        var masked = unsettled.Check();

        Assert.Equal((code, own), (plain.Code, plain.Class));
        Assert.DoesNotContain(MayHaveActed, plain.Message, StringComparison.Ordinal);
        Assert.Equal((code, FailureClass.Ambiguous), (masked.Code, masked.Class));
        Assert.True(masked.Refused);
        Assert.Null(masked.Plan);
        Assert.StartsWith(plain.Message!, masked.Message, StringComparison.Ordinal);
        Assert.Contains(MayHaveActed, masked.Message, StringComparison.Ordinal);
        Assert.Equal(plain.Details?.Count, masked.Details?.Count);
    }

    /// <summary>
    /// What is not a refusal is left as it is: a pattern that could not finish puts the send back in the queue, transient,
    /// for the claim to ask again, and an operation whose repeat does not rest on the key keeps its refusal's class.
    /// </summary>
    [Fact]
    public void Only_the_refusals_of_a_send_the_key_settles_carry_the_earlier_attempt()
    {
        var schema = Scenario.BindingSchema;
        schema["properties"]!["workspace"]!["pattern"] = "^[a-z]+$";
        var timedOut = Scenario.Sending("snd-1");
        timedOut.Route = new Route(Scenario.PluginId, new JsonObject { ["workspace"] = "west" }, "sha256:west");
        timedOut.Plugin = Scenario.Package(operations: [Send], binding: schema);
        timedOut.Unsettled = true;
        timedOut.Timing = TimingOut.Always();
        var transient = timedOut.Check();

        var add = new Scenario { Operation = Add, Contact = Scenario.Person(("email", "ada@example.test")), Arguments = Scenario.CompleteArguments, Unsettled = true };
        var unrouted = add.Check();

        Assert.Equal((AttemptErrors.ValidationTimedOut, FailureClass.Transient), (transient.Code, transient.Class));
        Assert.DoesNotContain(MayHaveActed, transient.Message, StringComparison.Ordinal);
        Assert.Equal((AttemptErrors.NoRoute, FailureClass.Permanent), (unrouted.Code, unrouted.Class));
        Assert.DoesNotContain(MayHaveActed, unrouted.Message, StringComparison.Ordinal);
    }

    /// <summary>What a refusal of a send that may have reached the person says beside its own reason.</summary>
    private const string MayHaveActed = "An earlier attempt of this work item may have reached the person";

    /// <summary>The send of <see cref="Scenario.Sending"/>, with the one thing wrong with it that <paramref name="wrong"/> names.</summary>
    private static Scenario Mistaken(string wrong)
    {
        var scenario = Scenario.Sending("snd-1");
        switch (wrong)
        {
            case "no route":
                scenario.Route = null;
                break;
            case "a plugin not loaded":
                scenario.Route = new Route("ghost-provider", null, null);
                break;
            case "a plugin unavailable":
                scenario.Plugin = Scenario.Package(operations: [Send], status: PluginStatus.Unavailable);
                break;
            case "a plugin without the send":
                scenario.Plugin = Scenario.Package(operations: [Add]);
                break;
            case "another contract version":
                scenario.Plugin = Scenario.Package(operations: [Send], contractVersions: [2]);
                break;
            case "a binding the plugin refuses":
                scenario.Plugin = Scenario.Package(operations: [Send], binding: Scenario.BindingSchema);
                break;
            case "no contact":
                scenario.Contact = null;
                break;
            case "no email":
                scenario.Contact = Scenario.Person(("phone", "+15555550100"));
                break;
            case "a suppressed address":
                scenario.Suppressed = true;
                break;
            case "no sender":
                scenario.Arguments!.Remove("sender");
                break;
            case "a moment the plugin does not take":
                scenario.Arguments!["send_after"] = Later;
                break;
            case "a template the plugin does not write":
                scenario.Arguments!["message"]!["compose"] = "template";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(wrong), wrong, "No such mistake.");
        }

        return scenario;
    }

    /// <summary>
    /// The marker is asked only where the operation's document rests the repeat on the key: an operation that repeats
    /// after a recovery read of its own runs after a lost answer under a plugin that lists nothing, as it always has.
    /// </summary>
    [Fact]
    public void A_repeat_whose_document_does_not_rest_on_the_key_asks_nothing_of_the_marker()
    {
        var contract = OperationCatalog.Find(Add)!;
        Assert.Equal(RepeatAfterAmbiguous.AfterRecoveryRead, contract.RepeatAfterAmbiguous);
        Assert.False(OutcomeContract.SettlesByKey(contract));
        var scenario = new Scenario
        {
            Operation = Add,
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
            Unsettled = true,
        };

        Assert.True(scenario.Check().Passed);
    }

    /// <summary>An argument the work does not carry asks nothing of the manifest: a send for now runs wherever it ran before.</summary>
    [Fact]
    public void A_send_that_names_no_moment_asks_nothing_of_the_manifest()
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = Scenario.Taking([]);

        Assert.Equal(AttemptErrors.ApprovalRequired, scenario.Check().Code);
    }

    /// <summary>
    /// A block holds only the work whose operation declares that it waits under one. A read runs through it and tells the
    /// truth, a control operation that declares it runs succeeds or fails on its own, and the take-back of a send runs
    /// too, because its moment passes while it waits.
    /// </summary>
    [Fact]
    public void A_read_and_a_control_operation_a_block_does_not_hold_run_through_it()
    {
        var read = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" },
            Blocks = [Scenario.Block()],
        }.Check();
        var control = new Scenario
        {
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
            Blocks = [Scenario.Block()],
        }.Check();
        var cancelling = Naming(Cancel, Made());
        cancelling.Blocks = [Scenario.Block()];
        var cancel = cancelling.Check();

        Assert.True(read.Passed, $"The read answered {read.Code}.");
        Assert.True(control.Passed, $"The control operation answered {control.Code}.");
        Assert.True(cancel.Passed, $"The take-back of a send answered {cancel.Code}.");
        Assert.Equal([null, null, null], [read.Block, control.Block, cancel.Block]);
    }

    /// <summary>
    /// The account's block covers every sending account under it; a sender's block holds only the work whose input names
    /// that sender, and work that names none is held by no sender's block at all.
    /// </summary>
    [Fact]
    public void An_account_block_holds_every_sender_and_a_sender_block_only_work_naming_its_own()
    {
        var marta = Scenario.Block(ProviderBlockScope.Sender, sender: "snd-1");
        var account = Scenario.Block();

        Assert.Same(marta, Scenario.Sending("snd-1", marta).Check().Block);
        Assert.Null(Scenario.Sending("snd-2", marta).Check().Block);
        Assert.Same(account, Scenario.Sending("snd-2", marta, account).Check().Block);

        var enrolment = new Scenario
        {
            Operation = Enroll,
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteEnrollment,
            Blocks = [marta],
        }.Check();
        Assert.Equal((AttemptErrors.ApprovalRequired, null), (enrolment.Code, enrolment.Block));
    }

    /// <summary>A block is the provider's word about one plugin and one binding, and holds no other plugin's or binding's work.</summary>
    [Theory]
    [InlineData("other-provider", "none")]
    [InlineData(Scenario.PluginId, "sha256:east")]
    public void A_block_of_another_plugin_or_binding_holds_nothing_here(string plugin, string binding)
    {
        var verdict = Scenario.Sending("snd-1", Scenario.Block(plugin: plugin, binding: binding)).Check();

        Assert.Equal((AttemptErrors.ApprovalRequired, null), (verdict.Code, verdict.Block));
    }

    /// <summary>
    /// A plugin that cannot be invoked at all is the same fact as one that does not implement the operation, and
    /// it is caught by the same check: a notification plugin implements no canonical operation by construction.
    /// </summary>
    [Fact]
    public void A_plugin_that_only_notifies_is_never_asked_to_perform_an_operation()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            Plugin = Scenario.Package(kind: PluginKind.Notification),
        };

        Assert.Equal(AttemptErrors.PluginOperationUnsupported, scenario.Check().Code);
    }

    /// <summary>
    /// Never a fallback to another plugin: an operation the routed plugin cannot perform ends the item with the
    /// reason, rather than being handed quietly to whichever other plugin happens to implement it.
    /// </summary>
    [Fact]
    [Critical(Family.W3, "no fallback to another plugin")]
    public void An_operation_the_routed_plugin_cannot_perform_is_never_handed_to_one_that_can()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            Plugin = Scenario.Package(operations: []),
            Others = [Scenario.Package(id: "willing-provider", operations: [Get])],
        };

        var verdict = scenario.Check();

        Assert.Equal(AttemptErrors.PluginOperationUnsupported, verdict.Code);
        Assert.Null(verdict.Plan);
    }

    /// <summary>
    /// A6. The planner's arguments contradict Jason's own record of what the provider calls this campaign. The
    /// rule is to work by the pinned identifier, so the contradiction is shown rather than silently resolved
    /// either way — and the message names the pin, because that is the value everything else would use.
    /// </summary>
    [Fact]
    [Critical(Family.W10, "arguments cannot contradict a pin")]
    public void An_identifier_in_the_arguments_that_contradicts_the_pin_is_refused_by_pointer()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            Arguments = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "cmp-provider-9" } },
            CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" },
        };

        var verdict = scenario.Check();

        Assert.Equal(AttemptErrors.InputInvalid, verdict.Code);
        var detail = Assert.Single(verdict.Details!);
        Assert.Equal("/args/campaign/external_id", detail.Field);
        Assert.Contains("cmp-provider-7", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of A6, and the reason it is not "refuse whenever both are present": a planner repeating
    /// what Jason already knows contradicts nothing, and an item that says the same thing twice still runs.
    /// </summary>
    [Fact]
    public void An_identifier_in_the_arguments_that_agrees_with_the_pin_passes()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            Arguments = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "cmp-provider-7" } },
            CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" },
        };

        Assert.True(scenario.Check().Passed);
    }

    /// <summary>
    /// The provider's slot at the claim: the composed input is checked with it set aside, and the plan hands the
    /// plugin the object exactly as the item holds it. A slot that is not an object is refused there by pointer, as
    /// the create would have refused it.
    /// </summary>
    [Fact]
    public void The_provider_slot_reaches_the_plan_as_written_and_is_held_to_being_an_object()
    {
        var provider = new JsonObject
        {
            ["send_window"] = new JsonObject { ["days"] = new JsonArray("mon", "tue"), ["label"] = "café de mañana" },
            ["notes"] = new JsonArray(1, true, null),
        };
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            Arguments = new JsonObject { ["provider"] = provider.DeepClone() },
            CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" },
        };

        var plan = scenario.Check().Plan;

        Assert.NotNull(plan);
        Assert.Equal(provider.ToJsonString(), plan.Input["args"]!["provider"]!.ToJsonString());

        scenario.Arguments = new JsonObject { ["provider"] = "send on Tuesdays" };
        var verdict = scenario.Check();

        Assert.Equal(AttemptErrors.InputInvalid, verdict.Code);
        var detail = Assert.Single(verdict.Details!);
        Assert.Equal(("/args/provider", "type"), (detail.Field, detail.Code));
    }

    /// <summary>
    /// The rule over the whole document is a claim-time check: creation validates the caller's own arguments, and
    /// only the composed document can say whether the campaign is named at all — by the arguments, or by a pin.
    /// </summary>
    [Fact]
    public void A_document_whose_properties_are_all_right_and_whose_root_rule_is_not_is_still_refused()
    {
        var scenario = new Scenario { Operation = Get, Route = Scenario.ToTheInstalledPackage };

        var verdict = scenario.Check();

        Assert.Equal(AttemptErrors.InputInvalid, verdict.Code);
        Assert.Equal("/", Assert.Single(verdict.Details!).Field);

        scenario.CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" };
        Assert.True(scenario.Check().Passed);
    }

    /// <summary>
    /// The contact check asks for a person only where the operation requires one. A read whose contact is optional
    /// runs about the campaign when the item names nobody, and about the person — by their pins — when it names one.
    /// </summary>
    [Fact]
    public void An_operation_whose_contact_is_optional_runs_without_a_person_and_with_one()
    {
        var scenario = Scenario.Reading(Threads);

        var alone = scenario.Check();
        Assert.True(alone.Passed);
        Assert.Null(alone.Plan!.Input["contacts"]);

        scenario.Contact = Scenario.Person(("email", "ada@example.test"));
        scenario.ContactPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "48211" };
        var about = scenario.Check();
        Assert.True(about.Passed);
        var person = Assert.Single(about.Plan!.Input["contacts"]!.AsArray())!;
        Assert.Equal("48211", (string?)person["external_ids"]!["contact"]);
        Assert.Null(person["channels"]);
    }

    /// <summary>
    /// A read about a person works by the routed plugin's pin alone, and a person that plugin never pinned is somebody
    /// nobody at the provider is known to be. The claim says so itself, with the code the plugin would answer, rather
    /// than starting a child to be told it: the item ends where it was decided, and keeps where it was routed.
    /// </summary>
    [Fact]
    public void A_read_about_a_person_the_routed_plugin_never_pinned_ends_at_the_claim()
    {
        var scenario = Scenario.Reading(Threads);
        scenario.Contact = Scenario.Person(("email", "ada@example.test"));

        var verdict = scenario.Check();

        Assert.True(verdict.Refused, verdict.Code);
        Assert.Equal(AttemptErrors.ContactNotLinked, verdict.Code);
        Assert.Equal(FailureClass.Permanent, verdict.Class);
        Assert.Null(verdict.Plan);
        Assert.Equal(Scenario.PluginId, verdict.Resolution!.Route.PluginId);
    }

    /// <summary>
    /// A delete works by the pin alone, and somebody nobody pinned is refused before anybody is asked to approve it: a
    /// person would otherwise approve a delete that can never run. The same delete for a pinned person is parked for
    /// that person's decision, as before.
    /// </summary>
    [Fact]
    public void A_delete_for_somebody_nobody_pinned_is_refused_before_anybody_is_asked_to_approve_it()
    {
        var scenario = new Scenario
        {
            Operation = Delete,
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = new JsonObject { ["channel"] = "email" },
        };

        var refused = scenario.Check();
        Assert.True(refused.Refused, refused.Code);
        Assert.False(refused.Parks);
        Assert.Equal(AttemptErrors.ContactNotLinked, refused.Code);
        Assert.Equal(FailureClass.Permanent, refused.Class);
        Assert.NotNull(refused.Resolution);

        // The routed plugin's own pin is all it takes, and it is what the person approving is shown.
        scenario.ContactPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "48211" };
        var parked = scenario.Check();
        Assert.True(parked.Parks, parked.Code);
        Assert.Equal(AttemptErrors.ApprovalRequired, parked.Code);
        Assert.Equal("48211", (string?)parked.Plan!.Input["contacts"]![0]!["external_ids"]!["contact"]);
    }

    /// <summary>
    /// The checks after the contact ask about the channel the operation consumes, and a read that consumes none is
    /// asked neither whether the person is reachable on one nor whether their address is suppressed.
    /// </summary>
    [Fact]
    public void A_read_that_consumes_no_channel_is_not_asked_about_reachability_or_suppression()
    {
        var scenario = Scenario.Reading(Threads);
        scenario.Contact = Scenario.Person(("phone", "+15555550100"));
        scenario.ContactPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "48211" };
        scenario.Suppressed = true;

        Assert.True(scenario.Check().Passed);
    }

    /// <summary>
    /// The register refuses what would reach a person, and the record that somebody said stop reaches nobody: its
    /// contract declares that the register does not apply, and a suppressed person is exactly who it is for. Taking
    /// them out of a campaign is the same, and is parked for a person rather than refused. The same person, the same
    /// register, and an operation that would reach them is still refused.
    /// </summary>
    [Fact]
    [Critical(Family.S1, "the register at the pre-flight")]
    public void A_suppressed_person_is_refused_what_would_reach_them_and_not_the_record_that_they_said_stop()
    {
        Scenario Suppressed(string operation, JsonObject arguments) => new()
        {
            Operation = operation,
            Plugin = Scenario.Package(operations: [Add, Enroll, Optout, Stop]),
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = arguments,
            Suppressed = true,
        };

        var recorded = Suppressed(Optout, new JsonObject
        {
            ["channel"] = "email",
            ["statement"] = "please take me off your list",
            ["arrived_via"] = "reply",
            ["said_at"] = "2026-10-03T08:14:00Z",
            ["learned_at"] = "2026-10-03T08:20:00Z",
        }).Check();
        var stopped = Suppressed(Stop, new JsonObject
        {
            ["campaign"] = new JsonObject { ["external_id"] = "7" },
            ["channel"] = "email",
            ["reason"] = new JsonObject { ["code"] = "opted_out" },
        }).Check();
        var added = Suppressed(Add, Scenario.CompleteArguments).Check();
        var enrolled = Suppressed(Enroll, Scenario.CompleteEnrollment).Check();

        Assert.True(recorded.Passed, recorded.Code);
        Assert.True(stopped.Parks, stopped.Code);
        Assert.Equal(AttemptErrors.Suppressed, added.Code);
        Assert.Equal(AttemptErrors.Suppressed, enrolled.Code);
    }

    /// <summary>What the claim hands on: the plan carries what the run needs, and every id it was decided by.</summary>
    [Fact]
    public void A_passing_pre_flight_hands_on_what_was_resolved()
    {
        var scenario = new Scenario
        {
            Operation = Add,
            Plugin = Scenario.Package(binding: Scenario.BindingSchema),
            Route = new Route(Scenario.PluginId, new JsonObject { ["workspace"] = "west" }, "sha256:west"),
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
        };

        var plan = scenario.Check().Plan;

        Assert.NotNull(plan);
        Assert.Equal(Scenario.PluginId, plan.Plugin.Manifest.Id);
        Assert.Equal(RouteScope.GlobalDefault, plan.Scope);
        Assert.Equal("sha256:west", plan.BindingIdentity);
        Assert.Equal("west", (string?)plan.Binding!["workspace"]);
        Assert.Equal(Add, plan.Contract.Id);
        Assert.Equal(scenario.PluginSnapshotId, plan.PluginSnapshotId);
        Assert.Equal(scenario.RoutingSnapshotId, plan.RoutingSnapshotId);
        Assert.Equal("ada@example.test", (string?)plan.Input["contacts"]![0]!["channels"]![0]!["value"]);
    }

    /// <summary>
    /// A route with no binding to a plugin whose schema declares only optional fields passes the check as the empty
    /// object, and what reaches the plugin is still no binding: <c>null</c>, with no identity, never <c>{}</c>.
    /// </summary>
    [Fact]
    public void A_route_with_no_binding_to_a_plugin_with_only_optional_fields_hands_it_null()
    {
        var schema = Scenario.BindingSchema;
        schema["required"] = new JsonArray();

        var plan = new Scenario
        {
            Operation = Add,
            Plugin = Scenario.Package(binding: schema),
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
        }.Check().Plan;

        Assert.NotNull(plan);
        Assert.True(plan.Binding is null, $"binding handed on: {plan.Binding?.ToJsonString()}");
        Assert.True(plan.BindingIdentity is null, $"identity handed on: {plan.BindingIdentity}");
    }

    /// <summary>
    /// The claim answers exactly what <c>route.resolve</c> would, because it reads the same snapshot through the
    /// same pure function: the level the route won at travels with the plan and is recorded against the attempt.
    /// </summary>
    [Fact]
    public void The_scope_the_route_won_at_travels_with_the_plan()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            CampaignRoute = new Route(Scenario.PluginId, null, null),
            CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" },
        };

        Assert.Equal(RouteScope.CampaignOperation, scenario.Check().Plan!.Scope);
    }

    /// <summary>
    /// Check 19 asks whether the composed input satisfies the operation's schema, and a pattern that did not finish
    /// matching, twice, leaves that unanswered. Nothing has happened — no child exists — so it is not
    /// <c>input_invalid</c>, which would tell a planner to edit an item that may be right: the attempt ends
    /// <c>transient</c>, under a code of its own, and the next scan asks again.
    /// </summary>
    [Fact]
    public void A_composed_input_a_pattern_could_not_measure_ends_transient_and_never_input_invalid()
    {
        var scenario = new Scenario
        {
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
            Timing = TimingOut.Always(),
        };

        var verdict = scenario.Check();

        Assert.Equal((AttemptErrors.ValidationTimedOut, FailureClass.Transient), (verdict.Code, verdict.Class));
        Assert.True(verdict.Refused);
        Assert.Null(verdict.Plan);
        Assert.NotNull(verdict.Resolution);
        Assert.All(verdict.Details!, detail => Assert.Equal("pattern_timed_out", detail.Code));
    }

    [Fact]
    public void A_composed_input_whose_pattern_times_out_once_is_measured_warm_and_passes()
    {
        var verdict = new Scenario
        {
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
            Timing = new TimingOut(1),
        }.Check();

        Assert.True(verdict.Passed);
    }

    /// <summary>An input wrong somewhere else is wrong whatever the unanswered pattern would have said.</summary>
    [Fact]
    public void A_composed_input_wrong_elsewhere_is_refused_as_input_invalid_for_what_is_wrong()
    {
        var verdict = new Scenario
        {
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = new JsonObject { ["channel"] = "email" },
            Timing = TimingOut.Always(),
        }.Check();

        Assert.Equal((AttemptErrors.InputInvalid, FailureClass.Validation), (verdict.Code, verdict.Class));
        Assert.Equal("/args/list", Assert.Single(verdict.Details!).Field);
    }

    /// <summary>
    /// Check 7 measures the route's binding against the plugin's own schema, and the same rule holds there: a pattern
    /// that did not finish says nothing about the binding, so it is not <c>binding_invalid</c>.
    /// </summary>
    [Fact]
    public void A_binding_a_pattern_could_not_measure_ends_transient_and_never_binding_invalid()
    {
        var schema = Scenario.BindingSchema;
        schema["properties"]!["workspace"]!["pattern"] = "^[a-z]+$";
        var verdict = new Scenario
        {
            Route = new Route(Scenario.PluginId, new JsonObject { ["workspace"] = "west" }, "sha256:west"),
            Plugin = Scenario.Package(binding: schema),
            Timing = TimingOut.Always(),
        }.Check();

        Assert.Equal((AttemptErrors.ValidationTimedOut, FailureClass.Transient), (verdict.Code, verdict.Class));
        Assert.Equal("/workspace", Assert.Single(verdict.Details!).Field);
    }

    /// <summary>A moment far ahead, with its offset, as a planner writes one: no clock these rows run under reaches it.</summary>
    private const string Later = "2099-11-02T09:00:00+01:00";

    /// <summary>The code this state is refused with, having first said that the class travels with it.</summary>
    private static string Refused(Scenario scenario, FailureClass expected)
    {
        var verdict = scenario.Check();

        Assert.False(verdict.Passed);
        Assert.Null(verdict.Plan);
        Assert.NotNull(verdict.Message);
        Assert.Equal(expected, verdict.Class);
        return verdict.Code!;
    }

    /// <summary>A mutable little world: everything the pre-flight reads, repaired one thing at a time.</summary>
    private sealed class Scenario
    {
        public const string PluginId = "stand-in-provider";

        public static JsonObject BindingSchema => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("workspace"),
            ["properties"] = new JsonObject { ["workspace"] = new JsonObject { ["type"] = "string" } },
        };

        /// <summary>The ordinary case: one global default, naming the package this scenario has installed.</summary>
        public static Route ToTheInstalledPackage => new(PluginId, null, null);

        public static JsonObject CompleteArguments => new()
        {
            ["list"] = new JsonObject { ["external_id"] = "L-1129" },
            ["channel"] = "email",
        };

        /// <summary>An enrollment with nothing wrong with it, so that the only thing left to stop it is a person.</summary>
        public static JsonObject CompleteEnrollment => new()
        {
            ["campaign"] = new JsonObject { ["external_id"] = "sq-1129" },
            ["channel"] = "email",
            ["collision"] = "skip",
            ["start"] = new JsonObject { ["position"] = "first_step" },
            ["first_touch"] = "authored_delay",
        };

        /// <summary>
        /// An email to Ada from the sending account <paramref name="sender"/>, through the installed package, with the
        /// provider's open <paramref name="blocks"/>: nothing is wrong with it, so only a person — and a block — stops it.
        /// </summary>
        public static Scenario Sending(string sender, params ProviderBlock[] blocks) => new()
        {
            Operation = Send,
            Route = ToTheInstalledPackage,
            Plugin = Package(operations: [Send]),
            Contact = Person(("email", "ada@example.test")),
            Arguments = new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = sender, ["address"] = "marta@example.com" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello, Ada." },
            },
            Blocks = blocks,
        };

        /// <summary>
        /// A send to Ada written as a template on <paramref name="channel"/>: an email, an InMail where it has a subject, a
        /// LinkedIn message where it has none, through the installed package writing a template on that channel. The
        /// sending account's address on LinkedIn is its profile.
        /// </summary>
        public static Scenario SendingATemplate(string channel, string? subject, string body)
        {
            var email = channel == "email";
            var message = new JsonObject
            {
                ["format"] = email ? "email" : subject is null ? "linkedin_message" : "linkedin_inmail",
                ["compose"] = "template",
                ["body"] = body,
            };
            if (subject is not null)
            {
                message["subject"] = subject;
            }

            return new Scenario
            {
                Operation = Send,
                Route = ToTheInstalledPackage,
                Plugin = Writing([channel]),
                Contact = Person(email ? ("email", "ada@example.test") : ("linkedin", "https://www.linkedin.com/in/ada-example")),
                Arguments = new JsonObject
                {
                    ["channel"] = channel,
                    ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = email ? "marta@example.com" : "https://www.linkedin.com/in/marta-owner" },
                    ["message"] = message,
                },
            };
        }

        /// <summary>An open block the provider said it holds, on the account as a whole or on one sending account under it.</summary>
        public static ProviderBlock Block(
            ProviderBlockScope scope = ProviderBlockScope.Account,
            string? sender = null,
            string plugin = PluginId,
            string binding = "none") => new()
        {
            PublicId = PublicId.New("blk"),
            PluginId = plugin,
            BindingIdentity = binding,
            Scope = scope,
            SenderExternalId = sender,
            Reason = "billing",
            OpenedAt = DateTime.UnixEpoch,
            LastSeenAt = DateTime.UnixEpoch,
            NextTrialAt = DateTime.UnixEpoch.AddHours(1),
            SeenCount = 1,
        };

        /// <summary>A routed read of a linked campaign with a well-formed page, and nobody named yet.</summary>
        public static Scenario Reading(string operation) => new()
        {
            Operation = operation,
            Route = ToTheInstalledPackage,
            Plugin = Package(operations: [operation]),
            Arguments = new JsonObject { ["limit"] = 20, ["offset"] = 0, ["source"] = "all" },
            CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "c-7714" },
        };

        public string RoutingSnapshotId { get; } = PublicId.New(RouteSnapshot.IdPrefix);

        public string PluginSnapshotId { get; } = PublicId.New(PluginProtocol.SnapshotIdPrefix);

        public string Operation { get; set; } = Add;

        /// <summary>The global default route, and null means nothing is routed anywhere.</summary>
        public Route? Route { get; set; }

        public Route? CampaignRoute { get; set; }

        public LoadedPlugin Plugin { get; set; } = Package();

        public IReadOnlyList<LoadedPlugin> Others { get; set; } = [];

        public Contact? Contact { get; set; }

        public bool Suppressed { get; set; }

        public JsonObject? Arguments { get; set; }

        public IReadOnlyDictionary<string, string> ContactPins { get; set; } = PreflightFacts.NoPins;

        public IReadOnlyDictionary<string, string> CampaignPins { get; set; } = PreflightFacts.NoPins;

        public PatternTiming Timing { get; set; } = PatternTiming.Default;

        /// <summary>The open blocks of the routed plugin the claim would have read, and null where it read none.</summary>
        public IReadOnlyList<ProviderBlock>? Blocks { get; set; }

        /// <summary>Whether an earlier attempt of the item ended ambiguous, as the claim would have read it.</summary>
        public bool Unsettled { get; set; }

        /// <summary>The send the arguments name under <c>args.send</c>, as the claim would have read it; null where it found none.</summary>
        public NamedSend? Named { get; set; }

        /// <summary>The search the arguments name under <c>args.search</c>, as the claim would have read it; null where it found none.</summary>
        public NamedSearch? Searched { get; set; }

        /// <summary>Another search of the campaign that has not ended, as the claim would have read it for a search; null where none.</summary>
        public string? SearchInTheWay { get; set; }

        /// <summary>
        /// The package, by its id and digest, and the account that attempt ran under, as its provenance kept them; null
        /// where it ran under the route and the package this claim resolves, and <see cref="NoProvenance"/> where the
        /// attempt kept none.
        /// </summary>
        public (string? PluginId, string? PluginDigest, string? BindingIdentity)? UnsettledUnder { get; set; }

        /// <summary>The context that attempt's snapshot kept; null where it ran with the item's context as it is now.</summary>
        public JsonObject? UnsettledArguments { get; set; }

        /// <summary>
        /// The facts the claim of the earlier attempt decided on, as a change of this claim's: where set, that attempt's
        /// provenance is the one its claim wrote, the digest of the input it composed included. Null where the attempt
        /// is described by <see cref="UnsettledUnder"/> and <see cref="UnsettledArguments"/>, and kept no digest.
        /// </summary>
        public Func<PreflightFacts, PreflightFacts>? UnsettledClaim { get; set; }

        /// <summary>The kinds of the pins the earlier attempt recorded itself, as the claim reads them; null for none.</summary>
        public PinsLearned? UnsettledLearned { get; set; }

        /// <summary>An earlier attempt whose provenance says nothing of where it ran.</summary>
        public static (string? PluginId, string? PluginDigest, string? BindingIdentity) NoProvenance => (null, null, null);

        /// <summary>The digest every package of these scenarios is loaded with.</summary>
        public const string RoutedDigest = "sha256:0";

        public static LoadedPlugin Package(
            string id = PluginId,
            PluginKind kind = PluginKind.Provider,
            IReadOnlyList<string>? operations = null,
            IReadOnlyList<int>? contractVersions = null,
            JsonObject? binding = null,
            PluginStatus status = PluginStatus.Valid) =>
            TestPlugins.Loaded(id, operations ?? [Add, Enroll, Get, Delete], kind, contractVersions, binding, status);

        /// <summary>
        /// What a provider's audience carries, as a manifest declares it: industries, a list of the provider's own words.
        /// A fresh copy each time, so a test may change it.
        /// </summary>
        public static AudienceDefinition Definition => new(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["industries"] = new JsonObject
                    {
                        ["description"] = "Industries the company is in, as the provider names them.",
                        ["type"] = "array",
                        ["maxItems"] = 25,
                        ["items"] = new JsonObject { ["type"] = "string", ["maxLength"] = 200 },
                    },
                },
            },
            new Dictionary<string, DefinitionKey>(StringComparer.Ordinal));

        /// <summary>The installed package performing the search and its status read, declaring <paramref name="definition"/> or <see cref="Definition"/>.</summary>
        public static LoadedPlugin Searching(JsonObject? binding = null, AudienceDefinition? definition = null)
        {
            var plugin = Package(operations: [Add, Enroll, Get, Delete, Search, SearchStatus], binding: binding);
            return plugin with { Manifest = plugin.Manifest with { AudienceDefinition = definition ?? Definition } };
        }

        /// <summary>The installed package performing the send, its manifest listing <c>send_after</c> on <paramref name="channels"/> and nothing where they are none.</summary>
        public static LoadedPlugin Taking(IReadOnlyList<string> channels)
        {
            var plugin = Package(operations: [Send]);
            var arguments = channels.Count == 0
                ? ManifestArguments.None
                : new ManifestArguments(new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal)
                {
                    [Send] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["send_after"] = channels },
                });
            return plugin with { Manifest = plugin.Manifest with { Arguments = arguments } };
        }

        /// <summary>The installed package performing the send, its manifest listing <paramref name="channels"/> under <c>templates</c> and nothing where they are none.</summary>
        public static LoadedPlugin Writing(IReadOnlyList<string> channels)
        {
            var plugin = Package(operations: [Send]);
            var templates = channels.Count == 0
                ? ManifestTemplates.None
                : new ManifestTemplates(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [Send] = channels });
            return plugin with { Manifest = plugin.Manifest with { Templates = templates } };
        }

        public static Contact Person(params (string Channel, string Value)[] channels)
        {
            var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada" };
            foreach (var (channel, value) in channels)
            {
                contact.Channels.Add(new ContactChannel { Channel = channel, Value = value, IsPrimary = true });
            }

            return contact;
        }

        /// <summary>
        /// The earlier attempt that ended ambiguous, under the plugin and account <see cref="UnsettledUnder"/> names, with
        /// the context <see cref="UnsettledArguments"/> names or the item's own.
        /// </summary>
        private Attempt Earlier(JsonObject context)
        {
            var routed = CampaignRoute ?? Route;
            var (pluginId, digest, binding) = UnsettledUnder ?? (routed?.PluginId, Plugin.Digest, routed?.BindingIdentity);
            return new Attempt
            {
                PublicId = PublicId.New("att"),
                Number = 1,
                Status = AttemptStatus.Failed,
                ContextSnapshot = (UnsettledArguments ?? context).DeepClone().AsObject(),
                Error = new AttemptErrorDto(AttemptErrors.ProviderAnswerLost, "The plugin's answer was lost.", true, Class: FailureClass.Ambiguous),
                Provenance = pluginId is null
                    ? null
                    : new AttemptProvenanceDto(pluginId, "1.0.0", digest, 1, 1, Operation, 1, null, null, RouteScope.GlobalDefault, binding),
            };
        }

        public PreflightVerdict Check()
        {
            var campaign = WorkItemFactory.NewCampaign();
            var item = WorkItemFactory.NewProviderOp(campaign, Operation);
            item.Contact = Contact;
            if (Arguments is not null)
            {
                item.Context = new JsonObject { [WorkItemService.InputKey] = Arguments.DeepClone() };
            }

            var facts = new PreflightFacts(item, campaign, Contact, ContactPins, CampaignPins, Suppressed, Blocks, Unsettled: null, Named, Search: Searched, SearchInTheWay: SearchInTheWay);
            var plugins = new PluginSnapshot(PluginSnapshotId, DateTime.UnixEpoch, SnapshotSource.Startup, [Plugin, .. Others]);

            var campaigns = new Dictionary<string, RouteSet>(StringComparer.Ordinal);
            if (CampaignRoute is not null)
            {
                campaigns[campaign.PublicId] = new RouteSet(
                    null,
                    new Dictionary<string, Route>(StringComparer.Ordinal) { [Operation] = CampaignRoute });
            }

            var routes = new RouteSnapshot(
                RoutingSnapshotId,
                DateTimeOffset.UnixEpoch,
                PluginSnapshotId,
                new RouteSet(Route, RouteSet.Empty.Operations),
                campaigns);

            if (Unsettled)
            {
                var earlier = Earlier(item.Context);
                if (UnsettledClaim is { } claimed)
                {
                    // That attempt's claim decided on the facts as they stood then, and wrote what it decided.
                    var then = ProviderOpPreflight.Check(claimed(facts), plugins, routes, Timing);
                    earlier.Provenance = AttemptProvenance.AtClaim(Operation, then, plugins, routes, earlier.PublicId);
                }

                facts = facts with { Unsettled = earlier, Learned = UnsettledLearned };
            }

            return ProviderOpPreflight.Check(facts, plugins, routes, Timing);
        }
    }
}
