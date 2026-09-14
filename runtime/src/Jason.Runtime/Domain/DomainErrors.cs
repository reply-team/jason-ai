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

    public static ConflictException RoleExists(string name) => new("role_exists", $"A role named '{name}' already exists.");

    /// <summary>Two writers reached the same row; the loser is told to read again rather than given a merged result.</summary>
    public static DomainException ConcurrentUpdate() =>
        new(StatusCodes.Status409Conflict, "concurrent_update", "Another change reached the same row first; read it again and retry.", retryable: true);

    public static ValidationException Required(string field) => new([new ErrorDetail(field, "required", $"{field} is required.")]);
}
