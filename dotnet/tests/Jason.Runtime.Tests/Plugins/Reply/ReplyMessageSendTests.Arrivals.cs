using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The key reaches Reply's send once per attempt: it travels only on the send command, and at most one send command of
/// an attempt reaches Reply's send. A lookup, a contact's update and a person Reply already held, named before the send,
/// carry it to no send of Reply's; a send Reply refuses at its route is the one.
/// </summary>
public partial class ReplyMessageSendTests
{
    [Theory]
    [InlineData("held for a moment ahead", true)]
    [InlineData("sent as soon as it can", true)]
    [InlineData("to a person the send imports", true)]
    [InlineData("to a person the import matched, by its identifier", true)]
    [InlineData("after the contact is given its profile", true)]
    [InlineData("refused by Reply at its route", false)]
    public async Task The_key_reaches_reply_s_send_once_per_attempt(string send, bool succeeds)
    {
        var (account, input, command) = send switch
        {
            "held for a moment ahead" => (ByEmail(), WithMoment(Input("email", pinned: "1001"), "2099-11-03T09:00:00-05:00"), "contact send-email"),
            "sent as soon as it can" => (ByEmail(), Input("email", pinned: "1001"), "contact send-email"),
            "to a person the send imports" => (new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox), Input("email"), "contact send-email"),
            "to a person the import matched, by its identifier" => (ByEmail().Unlisted(1001), Input("email"), "contact send-email"),
            "after the contact is given its profile" => (OnLinkedIn(linkedIn: null), Input("linkedin_message", pinned: "1001"), "contact linkedin-message"),
            _ => (ByEmail().WithBlacklisted(1001), Input("email", pinned: "1001"), "contact send-email"),
        };

        using (account)
        {
            using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
            await using var api = await ReplyPlugins.StartAsync(Ct);

            var result = await InvokeAsync(api, input);

            Assert.Equal(succeeds, result.Outcome is InvocationOutcome.Succeeded);
            var arrival = Assert.Single(account.KeyedArrivals)!;
            Assert.Equal((command, Key), ((string?)arrival["command"], (string?)arrival["key"]));
            Assert.All(account.Calls.Where(call => call.Line != command), call => Assert.Null(call.Option("--idempotency-key")));
        }
    }
}
