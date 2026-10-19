using System.Globalization;
using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Domain;

/// <summary>Every error code the business surface can answer, spelled exactly once.</summary>
public static class DomainErrors
{
    public static NotFoundException CampaignNotFound(string id) => new("campaign_not_found", $"No campaign with id '{id}'.");

    public static NotFoundException ContactNotFound(string id) => new("contact_not_found", $"No contact with id '{id}'.");

    public static ConflictException InvalidTransition(CampaignStatus from, CampaignStatus to) =>
        new("invalid_transition", $"A campaign cannot go from {SnakeCaseEnumConverter<CampaignStatus>.Format(from)} to {SnakeCaseEnumConverter<CampaignStatus>.Format(to)}.");

    public static ConflictException CampaignArchived(string id) => new("campaign_archived", $"Campaign '{id}' is archived; only journal entries can still be appended.");

    public static ConflictException ContactArchived(string id) => new("contact_archived", $"Contact '{id}' is archived.");

    /// <summary>
    /// A create under a key that already names an entity made by a different request. It names the entity and never
    /// quotes the key, which is the caller's text; and it is final, because sending it again cannot make the two
    /// requests the same.
    /// </summary>
    public static ConflictException IdempotencyKeyReused(string id) =>
        new("idempotency_key_reused", $"This idempotency key already names '{id}', which a different request created; give a different request a key of its own.");

    public static InvalidRequestException ContextTooLarge(int limitBytes) =>
        new("context_too_large", string.Create(CultureInfo.InvariantCulture, $"The campaign context must serialize to at most {limitBytes} bytes."));

    public static InvalidRequestException ReservedKind(string kind) =>
        new("reserved_kind", $"Journal kind '{kind}' is written by the runtime itself and cannot be appended by callers.");

    public static InvalidRequestException BatchTooLarge(int limit) =>
        new("batch_too_large", string.Create(CultureInfo.InvariantCulture, $"At most {limit} items per call."));

    public static NotFoundException WorkItemNotFound(string id) => new("work_item_not_found", $"No work item with id '{id}'.");

    public static NotFoundException UiNotInstalled(string why) => new("ui_not_installed", why);

    public static NotFoundException UiTokenNotFound(string id) => new("ui_token_not_found", $"No UI token '{id}' exists; `jason ui tokens` lists the live ones.");

    public static ConflictException WorkItemTerminal(string id, WorkItemStatus status) =>
        new("workitem_terminal", $"Work item '{id}' is {SnakeCaseEnumConverter<WorkItemStatus>.Format(status)}; finished items are not changed.");

    public static ConflictException ContactNotMember(string contactId, string campaignId) =>
        new("contact_not_member", $"Contact '{contactId}' is not a member of campaign '{campaignId}'.");

    public static ConflictException StaleAttempt(string attemptId) =>
        new("stale_attempt", $"Attempt '{attemptId}' is not the current running attempt of this work item; stop working on it.");

    public static InvalidRequestException ResultTooLarge(int limitBytes) =>
        new("result_too_large", string.Create(CultureInfo.InvariantCulture, $"A result must serialize to at most {limitBytes} bytes."));

    /// <summary>
    /// The answer is not the shape the work item asked for. Refused rather than stored: an executor still holds
    /// its attempt when it hears this, so it can answer again properly, and a paragraph explaining how thoroughly
    /// the job was done is never recorded as the job being done.
    /// </summary>
    public static DomainException ResultInvalid(IReadOnlyList<ErrorDetail> details) =>
        new(
            StatusCodes.Status400BadRequest,
            "result_invalid",
            "The result does not satisfy the result_format this work item declared.",
            retryable: false,
            details);

    /// <summary>
    /// A value this request carried could not be measured: a pattern in the schema it is held to did not finish
    /// matching, twice. That is a fact about the pattern and the machine and never about the value, so the request is
    /// neither accepted nor refused — nothing was written, and sending it again is the whole remedy. A 503 rather than
    /// a 400, because a 400 tells its sender to change something that may be perfectly right.
    /// </summary>
    public static DomainException ValidationTimedOut(IReadOnlyList<ErrorDetail> details) =>
        new(
            StatusCodes.Status503ServiceUnavailable,
            AttemptErrors.ValidationTimedOut,
            "A pattern this request is measured against did not finish matching in time, twice, so nothing was decided and nothing was changed; send it again.",
            retryable: true,
            details);

    public static ConflictException RoleExists(string name) => new("role_exists", $"A role named '{name}' already exists.");

    public static NotFoundException RoleNotFound(string name) => new("role_not_found", $"No role named '{name}'.");

    /// <summary>
    /// A note past the cap. Its own code rather than the campaign context's: a role reading the refusal has to
    /// know which of the two documents it was writing is too big.
    /// </summary>
    public static InvalidRequestException RoleNoteTooLarge(int limitBytes) =>
        new("role_note_too_large", string.Create(CultureInfo.InvariantCulture, $"A role note must serialize to at most {limitBytes} bytes."));

    public static ConflictException ProfileExists(string name) => new("profile_exists", $"An execution profile named '{name}' already exists.");

    public static NotFoundException ProfileNotFound(string name) => new("profile_not_found", $"No execution profile named '{name}'.");

    /// <summary>
    /// A revision the profile has never had. Its own code rather than the profile's: the profile is there, and a
    /// caller who asked for a revision number needs to be told which of the two they got wrong.
    /// </summary>
    public static NotFoundException ProfileRevisionNotFound(string name, int revision) =>
        new("profile_revision_not_found", string.Create(CultureInfo.InvariantCulture, $"Execution profile '{name}' has no revision {revision}."));

    /// <summary>Two writers reached the same row; the loser is told to read again rather than given a merged result.</summary>
    public static DomainException ConcurrentUpdate() =>
        new(StatusCodes.Status409Conflict, "concurrent_update", "Another change reached the same row first; read it again and retry.", retryable: true);

    /// <summary>
    /// The settings file is invalid and this runtime has never read one that was not, so there is nothing to
    /// work from. It is the file that is broken rather than the request: retryable, because repairing the file
    /// is all it takes. The section is named because three of them are read this way, and an operator told only
    /// that "the settings" are invalid would have the whole file to search.
    /// </summary>
    public static DomainException SettingsUnreadable(string section) =>
        new(
            StatusCodes.Status503ServiceUnavailable,
            "settings_unreadable",
            $"The runtime's '{section}' settings are invalid and none have validated since it started; repair the settings file and try again.",
            retryable: true);

    /// <summary>
    /// A reload that found something wrong with a package. The whole candidate set stays out and the previous
    /// snapshot stays active: a registry that is only partly right hides the problem instead of showing it.
    /// </summary>
    public static DomainException PluginReloadRejected(IReadOnlyList<ErrorDetail> details) =>
        new(
            StatusCodes.Status409Conflict,
            "plugin_reload_rejected",
            string.Create(CultureInfo.InvariantCulture, $"The plugin reload was rejected: {details.Count} problem(s) in the candidate set; the previous snapshot stays active."),
            retryable: false,
            details);

    public static NotFoundException ApprovalNotFound(string id) => new("approval_not_found", $"No approval with id '{id}'.");

    /// <summary>
    /// The decision has already been made, or the work it was about has moved on without it. Either way there is
    /// nothing here to decide, and the row says which of the two happened.
    /// </summary>
    public static ConflictException ApprovalNotPending(string id, ApprovalStatus status) =>
        new(
            "approval_not_pending",
            status == ApprovalStatus.Pending
                ? $"Approval '{id}' is still pending, and the work it is about is no longer waiting for it; read it again."
                : $"Approval '{id}' is {SnakeCaseEnumConverter<ApprovalStatus>.Format(status)}; only a pending decision about work that is still waiting can be made.");

    /// <summary>
    /// A role or an attempt tried to decide. Approval is a person's to give: the runtime's own actor is already
    /// refused to every caller, and a role that asks on somebody's behalf is asking to be that person.
    /// </summary>
    public static InvalidRequestException ApprovalNotHuman(ActorType type) =>
        new(
            "approval_not_human",
            $"An actor of type '{SnakeCaseEnumConverter<ActorType>.Format(type)}' cannot approve or reject; a decision is a person's to make.");

    /// <summary>The question is gone, or was never there. Nothing deletes a decision, so this is a wrong id.</summary>
    public static NotFoundException DecisionNotFound(string id) =>
        new("decision_not_found", $"Decision '{id}' does not exist.");

    /// <summary>
    /// It has been answered already, or the campaign it was about is archived. Either way there is nothing
    /// here to decide, and the row says which of the two happened.
    /// </summary>
    public static ConflictException DecisionNotPending(string id, DecisionStatus status) =>
        new(
            "decision_not_pending",
            $"Decision '{id}' is {SnakeCaseEnumConverter<DecisionStatus>.Format(status)}; only a pending question can be answered.");

    /// <summary>
    /// A role or an attempt tried to answer. The point of escalation is that the answer comes from outside the
    /// run that asked; a role answering its own question is the run deciding after all.
    /// </summary>
    public static InvalidRequestException DecisionNotHuman(ActorType type) =>
        new(
            "decision_not_human",
            $"An actor of type '{SnakeCaseEnumConverter<ActorType>.Format(type)}' cannot answer a decision; a question raised for a person is a person's to answer.");

    /// <summary>
    /// The chosen answer is not one of the ones offered. What is recorded is the label, so a label nobody
    /// offered would be an answer that reads like a quotation and is not one.
    /// </summary>
    public static InvalidRequestException DecisionOptionUnknown(string option) =>
        new("decision_option_unknown", $"'{option}' is not one of the options this decision named.");

    /// <summary>
    /// A causal reference has to point at something in this campaign. References are identifiers rather than
    /// copies precisely so they can be read later — one that resolves to nothing never could be.
    /// </summary>
    public static InvalidRequestException DecisionReferenceUnresolved(string kind, string id) =>
        new("decision_reference_unresolved", $"The {kind} reference '{id}' is not one of this campaign's own rows.");

    /// <summary>
    /// An absent actor is an anonymous human, which is right for creating work and wrong for deciding it: an
    /// accountable decision names the person who made it.
    /// </summary>
    public static InvalidRequestException ActorRequired() =>
        new("actor_required", "actor.id is required on a decision: what is recorded has to name the person who made it.");

    /// <summary>
    /// The runtime performs effects; it does not report them. Every caller is already refused the system actor,
    /// but a reporter is told why in the words of the thing they were doing.
    /// </summary>
    public static InvalidRequestException ReporterReserved() =>
        new("reporter_reserved", "actor type 'system' cannot report an effect: the runtime performs effects rather than reporting them, and a report has to name who did.");

    /// <summary>
    /// The ids in a report disagree with each other. Not a fact about the world worth keeping — a typo, and one
    /// the caller can see at a glance once it is named.
    /// </summary>
    public static InvalidRequestException CorrelationInconsistent(string message) => new("correlation_inconsistent", message);

    public static NotFoundException ReportNotFound(string id) => new("report_not_found", $"No report with id '{id}'.");

    public static NotFoundException EventSourceNotFound(string id) =>
        new("event_source_not_found", $"No account whose events are read has id '{id}'; `jason events sources` lists them.");

    /// <summary>
    /// The account is not read now: no route binds it at a plugin that reads events here. Nothing it is asked for could
    /// happen until one does, so it is refused rather than kept waiting unseen.
    /// </summary>
    public static ConflictException EventSourceUnbound(string id) =>
        new("event_source_unbound", $"No route binds account '{id}' at a plugin that reads events here now, so it is not read; route it again first.");

    /// <summary>As many spans of one account wait already as one account may have waiting: one has to end before another is asked for.</summary>
    public static ConflictException EventBackfillLimit(string id, int limit) =>
        new("event_backfill_limit", string.Create(CultureInfo.InvariantCulture, $"Account '{id}' already has {limit} spans waiting to be read again; one has to end before another is asked for."));

    public static ValidationException Required(string field) => new([new ErrorDetail(field, "required", $"{field} is required.")]);
}
