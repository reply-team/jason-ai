using System.Text.Json.Nodes;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Plugins.Invocation;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// A notification plugin an operator named, as a channel: decided step by step before any child starts — is it
/// loaded, does it speak the delivery contract, does the binding satisfy its own schema — and then invoked through
/// the real plugin host. A channel that cannot reach anybody is recorded with its reason, never skipped in silence.
/// </summary>
public sealed class PluginNotificationChannelTests
{
    private static readonly NotificationMessage Message = NotificationComposer.Compose(3, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Speaks = """
        manifest_version: 1
        id: ID
        version: 1.0.0
        kind: notification
        contracts:
          protocol: [1]
          operations: [1]
        binding:
          type: object
          properties:
            topic: { type: string, minLength: 1 }
          required: [topic]
          additionalProperties: false
        """;

    private static void Package(Jason.Contracts.Discovery.JasonPaths paths, string id, string mainJs, string manifest = Speaks) =>
        TestPlugins.Write(paths, id, manifest.Replace("ID", id, StringComparison.Ordinal), mainJs);

    private static PluginNotificationChannel Channel(RuntimeApiFixture api, string id, JsonObject? binding, PatternTiming? timing = null, DeliveryContract? delivery = null) =>
        new(id, new NotificationPluginEntry { Binding = binding }, api.Resolve<PluginRegistry>(), api.Resolve<IServiceScopeFactory>(), timing, delivery);

    private static JsonObject Topic(string topic = "walk") => new() { ["topic"] = topic };

    /// <summary>Nothing was started: no invocation has a directory of its own.</summary>
    private static void NothingStarted(RuntimeApiFixture api)
    {
        var work = api.Resolve<Jason.Contracts.Discovery.JasonPaths>().PluginWorkDirectory;
        Assert.True(!Directory.Exists(work) || !Directory.EnumerateDirectories(work).Any(), "an invocation was started");
    }

    private static void NotAttempted(ChannelOutcome outcome, string id, string reason)
    {
        Assert.Equal((id, ChannelKinds.Plugin, ChannelResult.NotAttempted, reason), (outcome.Channel, outcome.Kind, outcome.Result, outcome.Reason));
    }

    [Fact]
    public async Task A_plugin_that_is_not_loaded_is_unavailable()
    {
        await using var api = await PluginInvokerTests.StartAsync();

        var outcome = await Channel(api, "nobody-has-this", Topic()).DeliverAsync(Message, "ntf_01K0test", Ct);

        NotAttempted(outcome, "nobody-has-this", NotAttemptedReasons.Unavailable);
        Assert.Equal("not loaded", outcome.Detail);
        NothingStarted(api);
    }

    [Fact]
    public async Task A_plugin_that_cannot_run_on_this_machine_is_unavailable()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(
            paths,
            "stranded",
            "export function invoke() { return { result: {} }; }",
            Speaks + "\ncapabilities:\n  exec:\n    executables:\n      - name: jason-no-such-program-anywhere\n"));

        var outcome = await Channel(api, "stranded", Topic()).DeliverAsync(Message, "ntf_01K0test", Ct);

        NotAttempted(outcome, "stranded", NotAttemptedReasons.Unavailable);
        Assert.Contains("executable_missing", outcome.Detail, StringComparison.Ordinal);
        NothingStarted(api);
    }

    /// <summary>A provider named as a notification plugin is recorded, never quietly left out.</summary>
    [Fact]
    public async Task A_provider_named_as_a_notification_plugin_does_not_declare_the_contract()
    {
        await using var api = await PluginInvokerTests.StartAsync();

        var outcome = await Channel(api, TestPlugins.FakeProviderId, Topic()).DeliverAsync(Message, "ntf_01K0test", Ct);

        NotAttempted(outcome, TestPlugins.FakeProviderId, NotAttemptedReasons.NotDeclared);
        NothingStarted(api);
    }

    [Fact]
    public async Task A_notification_plugin_that_speaks_no_delivery_contract_does_not_declare_it()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(
            paths,
            "mute",
            "export function invoke() { return { result: {} }; }",
            "manifest_version: 1\nid: mute\nversion: 1.0.0\nkind: notification\ncontracts:\n  protocol: [1]\n"));

        var outcome = await Channel(api, "mute", null).DeliverAsync(Message, "ntf_01K0test", Ct);

        NotAttempted(outcome, "mute", NotAttemptedReasons.NotDeclared);
        NothingStarted(api);
    }

    /// <summary>A binding names an account and never a credential: a credential's place is the plugin's environment.</summary>
    [Fact]
    [Critical(Family.S3, "a binding never names a credential")]
    public async Task A_binding_that_names_a_credential_is_invalid()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: {} }; }"));

        var outcome = await Channel(api, "carrier", new JsonObject { ["topic"] = "walk", ["auth"] = new JsonObject { ["api_key"] = "k" } })
            .DeliverAsync(Message, "ntf_01K0test", Ct);

        NotAttempted(outcome, "carrier", NotAttemptedReasons.BindingInvalid);
        Assert.Contains("/auth/api_key", outcome.Detail, StringComparison.Ordinal);
        NothingStarted(api);
    }

    /// <summary>A binding that is not an object is refused by name, never read as an empty one.</summary>
    [Fact]
    public async Task A_binding_that_is_not_an_object_is_invalid()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: { receipt: 'x' } }; }"));
        var channel = new PluginNotificationChannel(
            "carrier",
            new NotificationPluginEntry { Binding = JsonValue.Create("walk") },
            api.Resolve<PluginRegistry>(),
            api.Resolve<IServiceScopeFactory>());

        var outcome = await channel.DeliverAsync(Message, "ntf_01K0test", Ct);

        NotAttempted(outcome, "carrier", NotAttemptedReasons.BindingInvalid);
        Assert.Equal("the binding is an object; this one is a string", outcome.Detail);
        NothingStarted(api);
    }

    /// <summary>
    /// The pointer is checked against the delivery contract's input schema before anything starts: an input outside it
    /// is the runtime's own failure, final, and no plugin is asked to deliver it.
    /// </summary>
    [Fact]
    [Critical(Family.R3, "notification.send is enforced")]
    public async Task An_input_outside_the_contract_is_refused_before_anything_starts()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: { receipt: 'x' } }; }"));
        var tooLong = new NotificationMessage(new string('J', 65), Message.Summary, Message.Look, Message.Approvals, Message.Decisions);

        var outcome = await Channel(api, "carrier", Topic()).DeliverAsync(tooLong, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "input_invalid", (FailureClass?)FailureClass.Validation), (outcome.Result, outcome.Code, outcome.Class));
        NothingStarted(api);
    }

    [Fact]
    public async Task A_binding_over_64_kib_is_invalid()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: {} }; }"));

        var outcome = await Channel(api, "carrier", Topic(new string('t', 70_000))).DeliverAsync(Message, "ntf_01K0test", Ct);

        NotAttempted(outcome, "carrier", NotAttemptedReasons.BindingInvalid);
        NothingStarted(api);
    }

    [Fact]
    public async Task A_binding_its_own_schema_refuses_is_invalid()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: {} }; }"));

        var outcome = await Channel(api, "carrier", new JsonObject { ["channel"] = "walk" }).DeliverAsync(Message, "ntf_01K0test", Ct);

        NotAttempted(outcome, "carrier", NotAttemptedReasons.BindingInvalid);
        Assert.Contains("/channel", outcome.Detail, StringComparison.Ordinal);
        NothingStarted(api);
    }

    /// <summary>
    /// A binding the plugin's own pattern could not finish measuring, twice, is not invalid — nothing was found wrong
    /// with it. Nothing has been sent either, so the channel fails transient and is tried again like any transient
    /// failure, rather than written off as a binding somebody has to repair.
    /// </summary>
    [Fact]
    public async Task A_binding_its_own_pattern_could_not_measure_is_a_transient_failure_and_nothing_starts()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: { receipt: 'x' } }; }", Patterned));

        var outcome = await Channel(api, "carrier", Topic(), TimingOut.Always(value => value == "walk")).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "validation_timed_out", (FailureClass?)FailureClass.Transient), (outcome.Result, outcome.Code, outcome.Class));
        Assert.Contains("/topic", outcome.Detail, StringComparison.Ordinal);
        NothingStarted(api);
    }

    [Fact]
    public async Task A_binding_whose_pattern_times_out_once_is_measured_warm_and_delivered()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: { receipt: 'x' } }; }", Patterned));

        var outcome = await Channel(api, "carrier", Topic(), new TimingOut(1, value => value == "walk")).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Delivered, "x"), (outcome.Result, outcome.Receipt));
    }

    /// <summary>
    /// The runtime's own half of the contract, over the input it is about to hand a plugin, measured under the timing
    /// the channel is given: a pattern that could not finish is no refusal — nothing was sent, so the channel fails
    /// transient — and nothing starts. The published contract carries no pattern, so this one adds one to the title.
    /// </summary>
    [Fact]
    public async Task An_input_no_pattern_could_measure_is_a_transient_failure_and_nothing_starts()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: { receipt: 'r-1' } }; }"));
        var timing = TimingOut.Always(value => value == NotificationComposer.Title);

        var outcome = await Channel(api, "carrier", Topic(), timing, PatternedContract()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "validation_timed_out", (FailureClass?)FailureClass.Transient), (outcome.Result, outcome.Code, outcome.Class));
        Assert.Contains("/title", outcome.Detail, StringComparison.Ordinal);
        NothingStarted(api);
    }

    /// <summary>
    /// Over the plugin's answer the plugin has already run, so a pattern that could not finish measuring it is a pointer
    /// that may have arrived: ambiguous, recorded, never repeated — and never <c>result_invalid</c>, which would blame it.
    /// </summary>
    [Fact]
    public async Task An_answer_no_pattern_could_measure_is_an_ambiguous_failure_and_never_result_invalid()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: { receipt: 'r-1' } }; }"));

        var outcome = await Channel(api, "carrier", Topic(), TimingOut.Always(value => value == "r-1"), PatternedContract()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "validation_timed_out", (FailureClass?)FailureClass.Ambiguous), (outcome.Result, outcome.Code, outcome.Class));
    }

    /// <summary>One timeout, measured warm: the input passes, and the answer is measured and delivered.</summary>
    [Fact]
    public async Task An_input_whose_pattern_times_out_once_is_measured_warm_and_delivered()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(paths, "carrier", "export function invoke() { return { result: { receipt: 'r-1' } }; }"));
        var timing = new TimingOut(1, value => value is NotificationComposer.Title);

        var outcome = await Channel(api, "carrier", Topic(), timing, PatternedContract()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Delivered, "r-1"), (outcome.Result, outcome.Receipt));
        Assert.Equal(1, timing.TimedOut);
    }

    /// <summary>The published delivery contract with a pattern on the input's title and on the result's receipt.</summary>
    private static DeliveryContract PatternedContract()
    {
        var published = DeliveryCatalog.Find("notification.send")!;
        var input = published.InputSchema.DeepClone().AsObject();
        input["properties"]!["title"]!["pattern"] = "^[A-Za-z]+$";
        var output = published.OutputSchema.DeepClone().AsObject();
        output["properties"]!["receipt"]!["pattern"] = "^r-[0-9]+$";
        return published with { InputSchema = input, OutputSchema = output };
    }

    private const string Patterned = """
        manifest_version: 1
        id: ID
        version: 1.0.0
        kind: notification
        contracts:
          protocol: [1]
          operations: [1]
        binding:
          type: object
          properties:
            topic: { type: string, minLength: 1, pattern: "^[a-z]+$" }
          required: [topic]
          additionalProperties: false
        """;

    [Fact]
    public async Task A_delivery_the_plugin_answers_is_delivered_with_its_receipt()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(
            paths,
            "carrier",
            "export function invoke(operation, input, context) { return { result: { receipt: context.binding.topic + ':' + input.counts.approvals + ':' + input.counts.decisions } }; }"));

        var outcome = await Channel(api, "carrier", Topic()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Delivered, "walk:3:1"), (outcome.Result, outcome.Receipt));
    }

    /// <summary>
    /// The one item a Windows toast carries by its id never reaches a plugin: the input is the title, the message and the
    /// counts, as it was before there was an item.
    /// </summary>
    [Fact]
    public async Task A_plugin_is_given_the_title_the_message_and_the_counts_and_never_the_item()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(
            paths,
            "carrier",
            "export function invoke(operation, input, context) { var text = JSON.stringify(input); return { result: { receipt: Object.keys(input).sort().join(',') + (text.indexOf('apr_') >= 0 || text.indexOf('dec_') >= 0 ? ':id' : ':none') } }; }"));
        var one = NotificationComposer.Compose(1, 0, item: "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6");
        Assert.NotNull(one.Item);

        var outcome = await Channel(api, "carrier", Topic()).DeliverAsync(one, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Delivered, "counts,message,title:none"), (outcome.Result, outcome.Receipt));
    }

    /// <summary>An answer the contract's result schema refuses is a pointer that may have arrived: ambiguous, never repeated.</summary>
    [Fact]
    public async Task An_answer_outside_the_contract_is_an_ambiguous_failure()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(
            paths,
            "carrier",
            "export function invoke() { return { result: { receipt: 'x', said: 'more' } }; }"));

        var outcome = await Channel(api, "carrier", Topic()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "result_invalid", (FailureClass?)FailureClass.Ambiguous), (outcome.Result, outcome.Code, outcome.Class));
    }

    [Fact]
    public async Task An_unreachable_service_is_a_transient_failure()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(
            paths,
            "carrier",
            "export function invoke() { throw host.fail({ class: 'transient', code: 'notification_unreachable', message: 'the server did not answer' }); }"));

        var outcome = await Channel(api, "carrier", Topic()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "notification_unreachable", (FailureClass?)FailureClass.Transient), (outcome.Result, outcome.Code, outcome.Class));
    }

    /// <summary>The delivery family declares no block, so a channel that answers blocked has answered outside its contract.</summary>
    [Fact]
    public async Task A_channel_that_answers_blocked_is_an_answer_outside_the_contract()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(
            paths,
            "carrier",
            "export function invoke() { throw host.fail({ class: 'blocked', code: 'provider_blocked', message: 'no', block: { scope: 'account', reason: 'billing' } }); }"));

        var outcome = await Channel(api, "carrier", Topic()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "result_invalid", (FailureClass?)FailureClass.Ambiguous), (outcome.Result, outcome.Code, outcome.Class));
    }

    [Fact]
    public void A_channel_failure_is_never_blocked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChannelOutcome.Failed("carrier", ChannelKinds.Plugin, "provider_blocked", FailureClass.Blocked));
    }

    [Fact]
    public async Task A_plugin_that_throws_is_the_host_s_failure_recorded_with_its_class()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Package(
            paths,
            "carrier",
            "export function invoke() { throw new Error('something broke'); }"));

        var outcome = await Channel(api, "carrier", Topic()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "plugin_exception", (FailureClass?)FailureClass.Permanent), (outcome.Result, outcome.Code, outcome.Class));
    }

    /// <summary>Each enabled named plugin joins the channels; one turned off does not.</summary>
    [Fact]
    public async Task Each_enabled_named_plugin_is_a_channel()
    {
        await using var api = await PluginInvokerTests.StartAsync();
        var options = new NotificationsOptions();
        options.Plugins["one"] = new NotificationPluginEntry { Binding = Topic() };
        options.Plugins["two"] = new NotificationPluginEntry { Enabled = false };

        var channels = api.Resolve<INotificationChannels>().Resolve(options);

        Assert.Equal(["toast", "one"], channels.Select(channel => channel.Name));
    }
}
