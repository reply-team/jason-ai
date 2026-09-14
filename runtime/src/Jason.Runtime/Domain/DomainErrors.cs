using System.Globalization;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

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

    public static ValidationException Required(string field) => new([new ErrorDetail(field, "required", $"{field} is required.")]);
}
