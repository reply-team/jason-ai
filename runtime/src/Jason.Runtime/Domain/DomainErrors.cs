using System.Globalization;
using Jason.Contracts.Api;
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

    public static InvalidRequestException ContextTooLarge(int limitBytes) =>
        new("context_too_large", string.Create(CultureInfo.InvariantCulture, $"The campaign context must serialize to at most {limitBytes} bytes."));

    public static InvalidRequestException ReservedKind(string kind) =>
        new("reserved_kind", $"Journal kind '{kind}' is written by the runtime itself and cannot be appended by callers.");

    public static InvalidRequestException BatchTooLarge(int limit) =>
        new("batch_too_large", string.Create(CultureInfo.InvariantCulture, $"At most {limit} items per call."));

    public static NotFoundException WorkItemNotFound(string id) => new("work_item_not_found", $"No work item with id '{id}'.");

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

    public static ConflictException RoleExists(string name) => new("role_exists", $"A role named '{name}' already exists.");

    public static NotFoundException RoleNotFound(string name) => new("role_not_found", $"No role named '{name}'.");

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

    public static ValidationException Required(string field) => new([new ErrorDetail(field, "required", $"{field} is required.")]);
}
