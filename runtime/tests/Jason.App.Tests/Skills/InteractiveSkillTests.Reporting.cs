namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    [Fact]
    public void The_reporting_skill_keeps_a_guess_a_credential_and_a_repeat_out_of_the_record()
    {
        var skill = Flattened("reporting-outside-effects");

        // A reporter who does not know when something happened will write down a plausible time unless the
        // skill hands them the option that says so. `--unknown` is the only way the record can distinguish
        // "nobody knows" from "somebody checked", and a skill that never names it never gets one.
        Assert.Contains("--unknown", skill, StringComparison.Ordinal);

        // And naming the option is not enough to make anybody prefer it: filling a field in looks tidier than
        // leaving it out. This is the sentence that says why the tidier answer is the worse one - a guess is
        // indistinguishable from a fact afterwards, and the report is read long after the guesser has gone.
        Assert.Contains("worse than a missing one", skill, StringComparison.Ordinal);

        // An account is asked for by name, and an API key or a password is the thing a session most easily
        // reaches for when asked to identify one. A report is stored as it was sent, is never editable, and is
        // read back to people - so a secret put here would be a secret published, permanently.
        Assert.Contains("never a credential", skill, StringComparison.Ordinal);

        // A submission that failed halfway is resent by anybody sensible. Without a key of the reporter's own,
        // a resend whose wording changed at all is admitted as a second effect, and the campaign then records
        // two mails where one was sent.
        Assert.Contains("--idempotency-key", skill, StringComparison.Ordinal);

        // The opposite mistake, and the one nobody notices: two sends really did reach that person, and with
        // no key the second is swallowed as a repeat of the first. This is the only line that tells a reporter
        // the record can silently lose a true report, and what to type so that it does not.
        Assert.Contains("a fresh key", skill, StringComparison.Ordinal);

        // What a report is worth, said before anything is submitted. A session that believed admission meant
        // the runtime had accepted the effect as fact would tell a person their view of the world had been
        // corrected, when all that happened is that somebody's claim was written down.
        Assert.Contains("your word and nothing else", skill, StringComparison.Ordinal);

        // And the specific expectation that would be acted on: reporting an effect does not complete, fail or
        // cancel the queued work item that would produce it again. A session that waited for that would leave
        // the duplicate work armed and tell the person it had been handled.
        Assert.Contains("moves no work item", skill, StringComparison.Ordinal);
    }
}
