using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Events;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// One campaign that sends email through one plugin and LinkedIn messages through another, end to end: a real runtime on a
/// held clock, the real claim, a real plugin host for every provider call, and two stand-in providers, each with an
/// account of its own at the other end. Each channel's sends go to the plugin its channel route names; a reply on each is
/// seen as the runtime sees a reply to a direct send, by reading that account's events, and answered at the account that
/// holds the thread, while a reply routed to the other plugin is refused before a child exists; a deferred send on each
/// holds its route against removal until its cancel, which goes where the send ran, lets it go.
/// </summary>
/// <remarks>
/// Each stand-in reaches its account through the stand-in vendor program: its events by the account's feed, and every act
/// by a read of the account's ledger under the work item's key, so the account's own call log says which work reached it.
/// The data is made up throughout; nothing reaches a network.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ChannelRoutesEndToEndTests
{
    private const string Mailer = "mailer";

    private const string Social = "social";

    /// <summary>What each account calls the campaign's one person, pinned by that account's first send.</summary>
    private const string MailerContact = "m-1001";

    private const string SocialContact = "s-2001";

    private const string MailerThread = "m-th-1";

    private const string SocialThread = "s-th-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.W11, "the one row that proves channel routes whole: sends, replies, cancels and a held route across two plugins")]
    public async Task Email_and_LinkedIn_go_through_two_plugins_with_their_replies_cancels_and_held_routes()
    {
        using var mailbox = new TestWorkspace();
        using var profile = new TestWorkspace();
        await using var host = await StartAsync();
        var mailerBinding = new JsonObject { ["workspace"] = mailbox.Root };
        var socialBinding = new JsonObject { ["workspace"] = profile.Root };
        var mailerAccount = BindingIdentity.Of(mailerBinding)!;
        var socialAccount = BindingIdentity.Of(socialBinding)!;

        var campaign = await host.CampaignAsync(Ct, "Two channels");
        var contact = await ContactAsync(host, campaign);
        await RouteAsync(host, campaign, "message.send", "email", Mailer, mailerBinding);
        await RouteAsync(host, campaign, "message.send", "linkedin", Social, socialBinding);
        await RouteAsync(host, campaign, "conversation.reply", "email", Mailer, mailerBinding);
        await RouteAsync(host, campaign, "conversation.reply", "linkedin", Social, socialBinding);

        // 1. A send on each channel goes to the plugin its channel route names, at that route's account, and its attempt
        //    says so: the plugin, the binding's identity and the channel scope that won.
        var email = await SendAsync(host, campaign, contact, "email", sendAfter: null);
        var linkedin = await SendAsync(host, campaign, contact, "linkedin", sendAfter: null);
        await ApprovedAndSucceededAsync(host, email, linkedin);

        await AssertRanAtAsync(host, email, Mailer, mailerAccount);
        await AssertRanAtAsync(host, linkedin, Social, socialAccount);
        Assert.Equal([email], ActsAt(mailbox));
        Assert.Equal([linkedin], ActsAt(profile));

        // 2. The person answers on each channel. Each account's next read of its events finds its thread moved by the
        //    other side, about the person its send pinned, and the campaign is told once per thread, by that person.
        host.Clock.Advance(TimeSpan.FromMinutes(20));
        WriteEvents(mailbox, Reply(MailerThread, MailerContact, "email", host.Clock.Now.AddMinutes(-5)));
        WriteEvents(profile, Reply(SocialThread, SocialContact, "linkedin", host.Clock.Now.AddMinutes(-4)));
        await SettleAsync(host, async () => (await SeenAsync(host, campaign)).Count == 2);

        var seen = await SeenAsync(host, campaign);
        Assert.Equal(
            [(Mailer, MailerThread, "email", "contact_pin"), (Social, SocialThread, "linkedin", "contact_pin")],
            seen.Select(line => ((string)line["plugin"]!, (string)line["thread"]!, (string)line["channel"]!, (string)line["attributed_by"]!))
                .OrderBy(line => line.Item1, StringComparer.Ordinal));
        Assert.All(seen, line => Assert.Equal(contact, (string?)line["contact_id"]));

        // 3. Each reply is answered at the account that holds its thread. A reply naming the email thread but sent on
        //    LinkedIn is routed to the other plugin, which does not hold that thread: it is refused at the claim with no
        //    child started, and nobody is asked to approve it.
        var crossed = await ReplyAsync(host, campaign, contact, "linkedin", MailerThread);
        var mailReply = await ReplyAsync(host, campaign, contact, "email", MailerThread);
        var socialReply = await ReplyAsync(host, campaign, contact, "linkedin", SocialThread);
        await SettleAsync(host, async () => (await host.GetAsync(crossed, Ct)).Status == WorkItemStatus.Failed);

        var refused = await host.GetAsync(crossed, Ct);
        var refusal = Assert.Single(refused.Attempts!);
        Assert.Equal(AttemptErrors.ThreadRouteMismatch, refusal.Error!.Code);
        Assert.Null(refusal.StartedAt);
        Assert.Null(refusal.Launch);
        Assert.Equal((Social, socialAccount), (refusal.Provenance!.PluginId, refusal.Provenance.BindingIdentity));
        Assert.DoesNotContain(await ApprovalsAsync(host), approval => approval.WorkItemId == crossed);
        Assert.DoesNotContain(crossed, ActsAt(mailbox));
        Assert.DoesNotContain(crossed, ActsAt(profile));

        await ApprovedAndSucceededAsync(host, mailReply, socialReply);
        await AssertRanAtAsync(host, mailReply, Mailer, mailerAccount);
        await AssertRanAtAsync(host, socialReply, Social, socialAccount);
        Assert.Equal(MailerThread, (string?)(await host.GetAsync(mailReply, Ct)).Result!["items"]![0]!["thread_external_id"]);
        Assert.Equal(SocialThread, (string?)(await host.GetAsync(socialReply, Ct)).Result!["items"]![0]!["thread_external_id"]);
        Assert.Equal([email, mailReply], ActsAt(mailbox));
        Assert.Equal([linkedin, socialReply], ActsAt(profile));

        // 4. A deferred send on each channel: two hours after the runtime's clock, which is long past by the machine's
        //    own, so only a guard that reads the runtime's clock still finds them held. Each result says the provider
        //    holds it and can cancel it.
        var later = host.Clock.Now.AddHours(2);
        var heldEmail = await SendAsync(host, campaign, contact, "email", later);
        var heldLinkedin = await SendAsync(host, campaign, contact, "linkedin", later);
        await ApprovedAndSucceededAsync(host, heldEmail, heldLinkedin);
        foreach (var held in new[] { heldEmail, heldLinkedin })
        {
            var outcome = (await host.GetAsync(held, Ct)).Result!["items"]![0]!;
            Assert.Equal(("accepted", true), ((string?)outcome["status"], (bool?)outcome["cancelable"]));
        }

        Assert.Equal([email, mailReply, heldEmail], ActsAt(mailbox));
        Assert.Equal([linkedin, socialReply, heldLinkedin], ActsAt(profile));

        // The replies are answered, so their routes go; each send route is now the last one naming its account.
        await UnrouteOkAsync(host, campaign, "conversation.reply", "email");
        await UnrouteOkAsync(host, campaign, "conversation.reply", "linkedin");

        // 5. While each deferred send is held, taking away the route it ran under is refused and writes nothing.
        var before = await RoutesAsync(host, campaign);
        var lines = await LineCountAsync(host, JournalKinds.RoutesUpdated);
        foreach (var (channel, held) in new[] { ("email", heldEmail), ("linkedin", heldLinkedin) })
        {
            var (status, body) = await UnrouteAsync(host, campaign, "message.send", channel);
            Assert.Equal(HttpStatusCode.Conflict, status);
            using var answer = JsonDocument.Parse(body);
            Assert.Equal("route_orphans_send", answer.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains(held, body, StringComparison.Ordinal);
        }

        Assert.Equal(before, await RoutesAsync(host, campaign));
        Assert.Equal(lines, await LineCountAsync(host, JournalKinds.RoutesUpdated));
        Assert.Equal([email, mailReply, heldEmail], ActsAt(mailbox));
        Assert.Equal([linkedin, socialReply, heldLinkedin], ActsAt(profile));

        // 6. A cancel of each goes to the plugin and account its send ran under, with no route of its own.
        var cancelEmail = await CancelAsync(host, campaign, heldEmail);
        var cancelLinkedin = await CancelAsync(host, campaign, heldLinkedin);
        await SettleAsync(host, async () => await AllAsync(host, WorkItemStatus.Succeeded, cancelEmail, cancelLinkedin));
        await AssertRanAtAsync(host, cancelEmail, Mailer, mailerAccount);
        await AssertRanAtAsync(host, cancelLinkedin, Social, socialAccount);
        Assert.Equal([email, mailReply, heldEmail, cancelEmail], ActsAt(mailbox));
        Assert.Equal([linkedin, socialReply, heldLinkedin, cancelLinkedin], ActsAt(profile));

        // 7. Once each cancel succeeded, the same removal goes through, the clock unmoved: the moments are still to come.
        Assert.True(host.Clock.Now < later);
        await UnrouteOkAsync(host, campaign, "message.send", "email");
        await UnrouteOkAsync(host, campaign, "message.send", "linkedin");
        Assert.Equal(lines + 2, await LineCountAsync(host, JournalKinds.RoutesUpdated));
    }

    // ------------------------------------------------------------------------------------------------------------
    // The stand-in providers
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A provider that sends on one channel, takes a send for a later moment there, cancels it, replies in its threads and
    /// reads its account's events. Everything it answers comes from its input; its account is reached only to read the
    /// account's feed and, for each act, the account's ledger under the work item's key.
    /// </summary>
    private static string Manifest(string id, string channel) => $"""
        manifest_version: 1
        id: {id}
        version: 1.0.0
        kind: provider
        name: Stand-in {id}
        description: A stand-in provider for one channel, for the end-to-end row of two plugins in one campaign.
        contracts:
          protocol: [1]
          operations: [1]
          events: [1]
        operations: [message.send, message.cancel, conversation.reply]
        entry:
          module: main.js
          function: invoke
        capabilities:
          exec:
            executables:
              - name: Jason.FakeProviderCli
        arguments:
          message.send:
            send_after: [{channel}]
        binding:
          type: object
          additionalProperties: false
          required: [workspace]
          properties:
            workspace:
              type: string
              minLength: 1
              description: The directory holding the provider account this route acts in.
        """;

    private static string MainJs(string prefix, string contactPin) => $$"""
        // A stand-in provider for one channel. It answers from its input; its account is the stand-in vendor program's
        // workspace the binding names, read for its events and, for every act, by its ledger under the work item's key.
        const PROVIDER_CLI = "Jason.FakeProviderCli";
        const PREFIX = "{{prefix}}";
        const CONTACT = "{{contactPin}}";

        function cli(context, subcommand, request, code) {
          const answer = host.exec({
            executable: PROVIDER_CLI,
            args: ["--workspace", context.binding.workspace, ...subcommand],
            stdin: JSON.stringify(request),
          });
          if (answer.exit_code !== 0) {
            throw host.fail({ class: "transient", code: code, message: "The provider did not answer." });
          }

          return JSON.parse(answer.stdout);
        }

        function act(input, context) {
          cli(context, ["ledger", "get"], { key: input.idempotency_key }, "provider_unavailable");
        }

        function send(input, context) {
          act(input, context);
          const later = input.args.send_after || null;
          const reference = PREFIX + "-send-" + input.idempotency_key;
          const item = {
            contact_id: input.contacts[0].id,
            status: later ? "accepted" : "sent",
            accepted_for: later,
            reference: reference,
            channel: input.args.channel,
            format: input.args.message.format,
            sender_external_id: input.args.sender.external_id,
            message: { external_id: PREFIX + "-msg-" + input.idempotency_key, sent_at: null },
            external_ids: { contact: CONTACT, send: reference },
          };
          if (later) {
            item.cancelable = true;
          }

          return { result: { items: [item] }, external_ids: { contact: CONTACT, send: reference } };
        }

        function cancel(input, context) {
          act(input, context);
          return { result: { items: [{ state: "cancelled", cancelled_at: null }] } };
        }

        function reply(input, context) {
          act(input, context);
          return {
            result: {
              items: [{
                contact_id: input.contacts[0].id,
                status: "sent",
                thread_external_id: input.args.thread.external_id,
                channel: input.args.channel,
                message: { external_id: PREFIX + "-msg-" + input.idempotency_key, sent_at: null },
              }],
            },
          };
        }

        function events(input, context) {
          const page = cli(context, ["events", "feed"], { after: input.cursor, since: input.since, until: input.until, limit: input.limit }, "events_unreachable");
          if (page.error) {
            throw host.fail({ class: "permanent", code: page.error.code, message: page.error.message });
          }

          return { result: page };
        }

        export function invoke(operation, input, context) {
          switch (operation) {
            case "message.send":
              return send(input, context);
            case "message.cancel":
              return cancel(input, context);
            case "conversation.reply":
              return reply(input, context);
            case "events.read":
              return events(input, context);
            default:
              throw host.fail({ class: "permanent", code: "operation_unsupported", message: "This plugin does not perform " + operation + "." });
          }
        }
        """;

    /// <summary>A thread of the account moved by the other side, about the person the account's send pinned.</summary>
    private static JsonObject Reply(string thread, string contact, string channel, DateTimeOffset at) => new()
    {
        ["kind"] = "thread_moved",
        ["id"] = "ev-" + thread,
        ["occurred_at"] = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        ["thread"] = thread,
        ["contact"] = contact,
        ["campaign"] = null,
        ["moved_by"] = "other_side",
        ["channel"] = channel,
        ["unread"] = true,
        ["has_meeting_intent"] = null,
    };

    /// <summary>The account's feed, as the stand-in vendor program reads it.</summary>
    private static void WriteEvents(TestWorkspace account, params JsonObject[] events) =>
        File.WriteAllText(
            Path.Combine(account.Root, "events.json"),
            new JsonObject { ["prefix"] = "c-", ["events"] = new JsonArray([.. events]) }.ToJsonString(),
            new UTF8Encoding(false));

    /// <summary>The work items whose acts reached the account, in the order they reached it.</summary>
    private static string[] ActsAt(TestWorkspace account) =>
        [.. account.KeyedCalls.Where(call => call.Subcommand == "ledger get").Select(call => call.Key)];

    // ------------------------------------------------------------------------------------------------------------
    // The runtime and the work
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The fake host's idle loop, and a manager woken by nothing this row writes: its cadence is the longest there is, and
    /// its one trigger is a decision answered, so the claim's one item per campaign per scan is always the row's own.
    /// </summary>
    private static string Settings()
    {
        var settings = JsonNode.Parse(FakeHostRuntime.Settings)!.AsObject();
        settings["Manager"] = new JsonObject
        {
            ["Triggers"] = new JsonArray(JournalKinds.DecisionAnswered),
            ["ReviewSeconds"] = ManagerOptions.MaximumReviewSeconds,
        };
        return settings.ToJsonString();
    }

    private static Task<FakeHostRuntime> StartAsync() =>
        FakeHostRuntime.StartAsync(
            Ct,
            Settings(),
            prepare: paths =>
            {
                TestPlugins.Write(paths, Mailer, Manifest(Mailer, "email"), MainJs("m", MailerContact));
                TestPlugins.Write(paths, Social, Manifest(Social, "linkedin"), MainJs("s", SocialContact));
                TestPlugins.Grant(paths, Mailer, exec: ["Jason.FakeProviderCli"]);
                TestPlugins.Grant(paths, Social, exec: ["Jason.FakeProviderCli"]);
            },
            configureServices: services =>
            {
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
                services.AddSingleton(TestPlugins.SearchPath);
            });

    /// <summary>The campaign's one person, reachable on both channels.</summary>
    private static async Task<string> ContactAsync(FakeHostRuntime host, string campaign)
    {
        var contact = await host.Fixture.PostOkAsync<ContactDto>(
            Operations.ContactCreate,
            new
            {
                first_name = "Ada",
                channels = new object[]
                {
                    new { channel = "email", value = "ada@example.test" },
                    new { channel = "linkedin", value = "https://www.linkedin.com/in/ada-example" },
                },
            },
            Ct);
        await host.Fixture.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaign, contacts = new[] { new { contact_id = contact.Id } } },
            Ct);
        return contact.Id;
    }

    private static async Task RouteAsync(FakeHostRuntime host, string campaign, string operation, string channel, string plugin, JsonObject binding)
    {
        var (status, body) = await host.Fixture.PostAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation, channel, plugin, binding },
            Ct);
        Assert.True(status == HttpStatusCode.OK, $"route.set answered {(int)status}: {body}");
    }

    private static Task<(HttpStatusCode Status, string Body)> UnrouteAsync(FakeHostRuntime host, string campaign, string operation, string channel) =>
        host.Fixture.PostAsync(
            Operations.RouteUnset,
            new { campaign_id = campaign, operation, channel, actor = new { type = "human", id = "operator" }, reason = "tidying routes" },
            Ct);

    private static async Task UnrouteOkAsync(FakeHostRuntime host, string campaign, string operation, string channel)
    {
        var (status, body) = await UnrouteAsync(host, campaign, operation, channel);
        Assert.True(status == HttpStatusCode.OK, $"route.unset of {operation}/{channel} answered {(int)status}: {body}");
    }

    private static async Task<string> RoutesAsync(FakeHostRuntime host, string campaign)
    {
        var (status, body) = await host.Fixture.PostAsync(Operations.RouteList, new { campaign_id = campaign }, Ct);
        Assert.Equal(HttpStatusCode.OK, status);
        return body;
    }

    private static async Task<string> SendAsync(FakeHostRuntime host, string campaign, string contact, string channel, DateTimeOffset? sendAfter)
    {
        var args = channel == "email"
            ? new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = "m-acct-1", ["address"] = "outreach@mailer.example" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "A short question", ["body"] = "Would a call next week suit you?" },
            }
            : new JsonObject
            {
                ["channel"] = "linkedin",
                ["sender"] = new JsonObject { ["external_id"] = "s-acct-1", ["address"] = null },
                ["message"] = new JsonObject { ["format"] = "linkedin_message", ["body"] = "Would a call next week suit you?" },
            };
        if (sendAfter is { } moment)
        {
            args["send_after"] = moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        return (await host.CreateAsync(
            new { campaign_id = campaign, kind = "provider_op", operation = "message.send", contact_id = contact, context = new JsonObject { ["input"] = args } },
            Ct)).Id;
    }

    private static async Task<string> ReplyAsync(FakeHostRuntime host, string campaign, string contact, string channel, string thread) =>
        (await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation = "conversation.reply",
                contact_id = contact,
                context = new JsonObject
                {
                    ["input"] = new JsonObject
                    {
                        ["channel"] = channel,
                        ["thread"] = new JsonObject { ["external_id"] = thread },
                        ["body"] = "Thank you, Tuesday works.",
                    },
                },
            },
            Ct)).Id;

    private static async Task<string> CancelAsync(FakeHostRuntime host, string campaign, string send) =>
        (await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation = "message.cancel",
                context = new JsonObject { ["input"] = new JsonObject { ["send"] = send } },
            },
            Ct)).Id;

    /// <summary>The items parked for a person, approved by one, then run to their end.</summary>
    private static async Task ApprovedAndSucceededAsync(FakeHostRuntime host, params string[] items)
    {
        await SettleAsync(host, async () => await AllAsync(host, WorkItemStatus.AwaitingApproval, items));
        foreach (var approval in await ApprovalsAsync(host))
        {
            await host.Fixture.PostOkAsync<ApprovalDto>(
                Operations.ApprovalApprove,
                new { approval_id = approval.Id, actor = new { type = "human", id = "operator" }, reason = "read it" },
                Ct);
        }

        await SettleAsync(host, async () => await AllAsync(host, WorkItemStatus.Succeeded, items));
    }

    private static async Task<IReadOnlyList<ApprovalSummaryDto>> ApprovalsAsync(FakeHostRuntime host) =>
        (await host.Fixture.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items;

    /// <summary>
    /// Scans until the row's condition holds, each scan followed by a wait for what it started: the handler pool's
    /// children and the events reader's read. The claim takes one item of a campaign per scan, and the reader one account
    /// per scan, so a step of the row takes a few scans.
    /// </summary>
    private static async Task SettleAsync(FakeHostRuntime host, Func<Task<bool>> done)
    {
        var pool = host.Fixture.Resolve<HandlerPool>();
        var reader = host.Fixture.Resolve<EventReader>();
        for (var scan = 0; scan < 12; scan++)
        {
            await host.ScanAsync(Ct);
            Assert.True(
                await DispatchHarness.EventuallyAsync(() => pool.InFlightAttemptIds.Count == 0 && reader.Idle, Ct, timeoutMilliseconds: 30_000),
                "A child the scan started never finished.");
            if (await done())
            {
                return;
            }
        }

        Assert.Fail("Twelve scans did not bring the row to the state it waits for.");
    }

    private static async Task<bool> AllAsync(FakeHostRuntime host, WorkItemStatus status, params string[] items)
    {
        foreach (var item in items)
        {
            if ((await host.GetAsync(item, Ct)).Status != status)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The item succeeded in one attempt, which ran at that plugin and account by a campaign channel route or the send it follows.</summary>
    private static async Task AssertRanAtAsync(FakeHostRuntime host, string item, string plugin, string account)
    {
        var read = await host.GetAsync(item, Ct);
        var attempt = Assert.Single(read.Attempts!);
        Assert.Equal(AttemptStatus.Succeeded, attempt.Status);
        Assert.Equal((plugin, account, RouteScope.CampaignChannel), (attempt.Provenance!.PluginId, attempt.Provenance.BindingIdentity, attempt.Provenance.RouteScope));
    }

    /// <summary>The campaign's <c>inbound_reply_observed</c> lines, as the events store wrote them.</summary>
    private static async Task<List<JsonObject>> SeenAsync(FakeHostRuntime host, string campaign)
    {
        await using var db = Open(host);
        var lines = await db.Journal.AsNoTracking()
            .Where(entry => entry.Kind == JournalKinds.InboundReplyObserved && entry.Campaign!.PublicId == campaign)
            .OrderBy(entry => entry.Id)
            .ToListAsync(Ct);
        return [.. lines.Select(line => line.New!.AsObject())];
    }

    private static async Task<int> LineCountAsync(FakeHostRuntime host, string kind)
    {
        await using var db = Open(host);
        return await db.Journal.CountAsync(entry => entry.Kind == kind, Ct);
    }

    private static JasonDbContext Open(FakeHostRuntime host) => new(JasonDbContext.CreateOptions(host.Paths.DatabaseFile));
}
