namespace Jason.Contracts.Api;

/// <summary>
/// Why a send the automatic mode could have released waits for the person instead: an approval's <c>reason</c>, one closed
/// word each, beside <c>approval_required</c> and <c>input_changed</c>, with the one sentence a person reads for it. A send
/// is parked with one of these only where the mode in force for its contact is automatic; where it is every message the
/// reason is what it always was.
/// </summary>
public static class SendModeReasons
{
    /// <summary>The send was written in the desktop's chat, or its input edited there; it waits whatever the mode.</summary>
    public const string SentFromChat = "sent_from_chat";

    /// <summary>A doubt stands about the send: its creator's <c>ask_person</c>, or a doubting review of its text.</summary>
    public const string Doubted = "doubted";

    /// <summary>A reply of the contact's in this campaign has not been judged since it moved.</summary>
    public const string AwaitingJudgement = "awaiting_judgement";

    /// <summary>A reply was judged by a verdict that says no nature, so the runtime cannot tell what kind of reply it was.</summary>
    public const string AwaitingNature = "awaiting_nature";

    /// <summary>The send answers a reply, and no verdict that stands in the campaign judges the reply it answers.</summary>
    public const string AnswersNoVerdict = "answers_no_verdict";

    /// <summary>The reply it answers was an automatic reply, a bounce or a request to stop, which never gets an automatic answer.</summary>
    public const string ReplyNotFromAPerson = "reply_not_from_a_person";

    /// <summary>The verdict on the reply it answers says <c>escalate</c>: a role marked the reply for the person.</summary>
    public const string Escalated = "escalated";

    /// <summary>A pass that counts stands only for a text the send no longer carries: its input was edited since.</summary>
    public const string EditedAfterReview = "edited_after_review";

    /// <summary>No pass by the critic or by a person stands for the send's text.</summary>
    public const string NotReviewed = "not_reviewed";

    /// <summary>
    /// The campaign has had as many sends released automatically since 00:00Z as <c>Sending:AutomaticMaxPerDay</c> allows; the
    /// ceiling never slips, so no standing approval lets the send go either.
    /// </summary>
    public const string DailyCeiling = "daily_ceiling";

    /// <summary>Every reason, in the order the claim asks, with the sentence a person reads for it.</summary>
    public static IReadOnlyList<(string Code, string Sentence)> All { get; } =
    [
        (SentFromChat, "This send was written in Jason's chat, so it waits for you whatever the send mode."),
        (Doubted, "A doubt was raised about this send, so it waits for you; the doubt is below."),
        (AwaitingJudgement, "A reply from this contact has not been judged yet, so nothing goes to them automatically until it is."),
        (AwaitingNature, "A reply from this contact was judged before Jason recorded what kind of reply it was, so nothing goes to them automatically until it is judged again."),
        (AnswersNoVerdict, "This answer names no judged reply, so it is not sent automatically."),
        (ReplyNotFromAPerson, "The reply this answers was an automatic reply, a bounce or a request to stop, which never gets an automatic answer."),
        (Escalated, "The reply this answers was marked for you to decide."),
        (EditedAfterReview, "The text was edited after it was reviewed, so the review no longer stands for it."),
        (NotReviewed, "No critic's or person's review passed this text, so the automatic mode does not send it."),
        (DailyCeiling, "This campaign has reached its limit of automatic sends for today, so this one waits for you to approve it."),
    ];

    /// <summary>The sentence a person reads for <paramref name="reason"/>, or null where it is none of these.</summary>
    public static string? Sentence(string? reason) =>
        All.Where(entry => string.Equals(entry.Code, reason, StringComparison.Ordinal)).Select(entry => entry.Sentence).FirstOrDefault();
}
