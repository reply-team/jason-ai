using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// Reply refuses a direct email for two limits it counts: the mailbox's daily limit of direct emails
/// (<c>contact.dailyLimitReached</c>) and the account's active contacts for its billing period
/// (<c>contact.activeContactsLimitReached</c>). The package knows them by Reply's own code, whatever the status and whatever
/// code the installed reply-cli prints for them — <c>rate_limited</c> today, a word of its own later — and either is the
/// mailbox's block for its limit, on the email channel: never a wait on a busy Reply repeated blind. The daily one clears at
/// the next midnight UTC, when Reply counts a new day, unless Reply said how long to wait; the active contacts one only where
/// Reply said so, and is otherwise tried at the runtime's own cadence. A refusal that names the send it made a record of names
/// a dead record: its reference is never kept as the work item's send, and the send a later trial makes is.
/// </summary>
public partial class ReplyMessageSendTests
{
    private const string DailyLimit = "contact.dailyLimitReached";
    private const string ActiveContactsLimit = "contact.activeContactsLimitReached";

    /// <summary>
    /// Each code is the mailbox's block for its limit, read by Reply's code alone: under the <c>rate_limited</c> every
    /// reply-cli from the package's minimum prints at 429, under another status Reply may answer it at, and under a code of
    /// reply-cli's own it may give it later. Reply's code is in the trace beside the CLI's, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("rate_limited", 429, DailyLimit)]
    [InlineData("rate_limited", 429, ActiveContactsLimit)]
    [InlineData("reply.refused", 400, DailyLimit)]
    [InlineData("reply.refused", 403, ActiveContactsLimit)]
    [InlineData("email.limit_reached", 429, DailyLimit)]
    [InlineData("email.limit_reached", 429, ActiveContactsLimit)]
    public async Task An_email_refused_for_a_limit_reply_counts_is_the_mailbox_s_block_whatever_the_cli_calls_it(string cliCode, int status, string replyCode)
    {
        using var account = ByEmail().FailsTheNextSend(cliCode, 1, status: status, replyCode: replyCode);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        ReplyOperations.Blocked(error);
        Assert.Equal(("sender", "12", "email", "limit"), ((string?)error.Block!["scope"], (string?)error.Block["sender_external_id"], (string?)error.Block["channel"], (string?)error.Block["reason"]));
        Assert.Equal((replyCode, cliCode), ((string?)error.Details!["provider_code"], (string?)error.Details["cli_code"]));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply counts a mailbox's day in UTC, so the daily limit's block says it clears at the next midnight UTC after the
    /// answer: the trial then is the first send of Reply's new day. Read here around the call; the moment itself is pinned
    /// by <c>ReplyEmailLimitClockTests</c> in the plugin host's tests, beside the engine.
    /// </summary>
    [Fact]
    public async Task The_daily_limit_s_block_clears_at_the_next_midnight_utc()
    {
        using var account = ByEmail().FailsTheNextSend("rate_limited", 1, status: 429, replyCode: DailyLimit);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var before = DateTimeOffset.UtcNow;
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));
        var after = DateTimeOffset.UtcNow;

        var clears = DateTimeOffset.Parse((string?)error.Block!["clears_at"] ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Contains(clears, new[] { NextMidnight(before), NextMidnight(after) });
    }

    /// <summary>
    /// The active contacts of a billing period renew on a day Reply does not say, so that block names no moment it clears and
    /// the runtime tries it on its own cadence.
    /// </summary>
    [Fact]
    public async Task The_active_contacts_block_names_no_moment_it_clears()
    {
        using var account = ByEmail().FailsTheNextSend("rate_limited", 1, status: 429, replyCode: ActiveContactsLimit);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal("""{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit"}""", ReplyOperations.Blocked(error));
    }

    /// <summary>
    /// Where reply-cli says how long to wait, either block clears that long after the answer, the daily one no later than
    /// the next midnight UTC, when Reply's day turns.
    /// </summary>
    [Theory]
    [InlineData(DailyLimit)]
    [InlineData(ActiveContactsLimit)]
    public async Task A_limit_with_a_wait_clears_that_long_after_the_answer(string replyCode)
    {
        const int Wait = 5400;
        using var account = ByEmail();
        account.Refuses("contact send-email", "rate_limited", status: 429, replyCode: replyCode, ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 }, retryAfter: Wait);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var before = DateTimeOffset.UtcNow;
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        ReplyOperations.Blocked(error);
        if (replyCode == DailyLimit && after.AddSeconds(Wait) >= NextMidnight(before))
        {
            // Run within the wait of a midnight: the daily block clears at that midnight instead.
            var clears = DateTimeOffset.Parse((string?)error.Block!["clears_at"] ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            Assert.True(clears <= NextMidnight(after), $"{clears:O} is past the next midnight UTC.");
        }
        else
        {
            ReplyOperations.ClearsAfter(error, before, after, Wait);
        }
    }

    /// <summary>
    /// An email whose due day is already full names a wait to the end of that day, weeks out, and the mailbox's daily block
    /// still clears at the next midnight UTC: the mailbox is free tomorrow, and it is tried again then.
    /// </summary>
    [Fact]
    public async Task A_far_due_day_already_full_clears_the_mailbox_at_the_next_midnight_utc()
    {
        const int Wait = 20 * 24 * 60 * 60;
        using var account = ByEmail();
        account.Refuses("contact send-email", "email.limit_reached", status: 429, replyCode: DailyLimit, ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 }, retryAfter: Wait);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var before = DateTimeOffset.UtcNow;
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        var clears = DateTimeOffset.Parse((string?)error.Block!["clears_at"] ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Contains(clears, new[] { NextMidnight(before), NextMidnight(after) });
    }

    /// <summary>
    /// A mailbox Reply says cannot send direct emails at all, as reply-cli 0.22.0 prints it (`email.sending_disabled` at 403,
    /// Reply's `contact.emailAccountSendingDisabled` carried, no wait), is the mailbox's block for its limit with no moment it
    /// clears, never a sign-in: the message waits on it, and nothing is sent.
    /// </summary>
    [Fact]
    public async Task A_mailbox_that_cannot_send_is_its_block_and_the_message_waits()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(12, "email", "Ann Owner", Mailbox, limit: "sending_disabled");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        Assert.Equal("""{"scope":"sender","sender_external_id":"12","channel":"email","reason":"limit"}""", ReplyOperations.Blocked(error));
        Assert.Equal(("contact.emailAccountSendingDisabled", "email.sending_disabled"), ((string?)error.Details!["provider_code"], (string?)error.Details["cli_code"]));
        Assert.Contains("cannot send direct emails now", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sign in", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(account.Sent);
        Assert.Empty(account.DirectSends);
    }

    /// <summary>
    /// Reply makes a record of an email it dispatches under a key before it counts the mailbox's limits, and a refusal for one
    /// of them names that record, failed. It is a dead record: the refusal is the same mailbox's block, nothing of the record
    /// is kept as the work item's send, and Reply released the key with it. Once the limit lifts, the same send under the
    /// same key — the block's trial — makes a record of its own, which is the one kept.
    /// </summary>
    [Theory]
    [InlineData("daily", DailyLimit)]
    [InlineData("active_contacts", ActiveContactsLimit)]
    public async Task A_limit_refusal_naming_a_failed_record_keeps_none_of_it_and_the_trial_s_send_is_kept(string limit, string replyCode)
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(12, "email", "Ann Owner", Mailbox, limit: limit);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("provider_blocked", FailureClass.Blocked), (error.Code, error.Class));
        ReplyOperations.Blocked(error);
        Assert.Equal(("12", "email", replyCode), ((string?)error.Block!["sender_external_id"], (string?)error.Block["channel"], (string?)error.Details!["provider_code"]));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.DoesNotContain("email-1001", error.Message, StringComparison.Ordinal);
        var dead = Assert.Single(account.DirectSends)!;
        Assert.Equal(("email-1001", "failed", replyCode), ((string?)dead["reference"], (string?)dead["state"], (string?)dead["failureCode"]));
        Assert.Empty(account.Sent);

        account.WithSender(12, "email", "Ann Owner", Mailbox);
        var trial = await InvokeAsync(api, Input("email", pinned: "1001"));
        var sent = ReplyOperations.Succeeded(Operation, trial)["items"]![0]!.AsObject();

        Assert.Equal(("sent", "email-1002"), ((string?)sent["status"], (string?)sent["reference"]));
        Assert.Equal("""{"contact":"1001","send":"email-1002"}""", Assert.IsType<InvocationOutcome.Succeeded>(trial.Outcome).ExternalIds!.ToJsonString());
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// Either code answered by a command that ran from no sending account — here the read of the pinned contact, before the
    /// send — has no account a block could name: it is the operation's permanent word for a spent allowance, as a LinkedIn
    /// account's is, and holds nothing.
    /// </summary>
    [Theory]
    [InlineData(DailyLimit)]
    [InlineData(ActiveContactsLimit)]
    public async Task A_limit_refusal_from_a_command_run_from_no_sender_is_no_block(string replyCode)
    {
        using var account = ByEmail();
        account.Refuses("contact get", "rate_limited", status: 429, replyCode: replyCode);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("sender_limit_reached", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Null(error.Block);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A 429 under any other code of Reply's, or none, is still the wait on a busy Reply: the two codes are read as a block,
    /// and nothing else is.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("contact.somethingElse")]
    public async Task An_email_refused_at_429_under_no_limit_code_waits_as_reply_asked(string? replyCode)
    {
        using var account = ByEmail().FailsTheNextSend("rate_limited", 1, status: 429, replyCode: replyCode);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("rate_limited", FailureClass.Transient), (error.Code, error.Class));
        Assert.Null(error.Block);
    }

    /// <summary>The first midnight UTC after <paramref name="at"/>.</summary>
    private static DateTimeOffset NextMidnight(DateTimeOffset at) =>
        new DateTimeOffset(at.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
}
