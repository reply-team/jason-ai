using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Roles;

namespace Jason.Runtime.Notes;

/// <summary>
/// What a note request has to look like before anything is loaded. The role name is held to the roster's own
/// shape, so a name that could never have been registered is the caller's typo rather than a role nobody added; and
/// the request names one level — a campaign, a project, or neither for the client's — never two.
/// </summary>
internal static class RoleNoteValidation
{
    public static void ValidateGet(RoleNoteGetRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);
        ValidateLevel(request.CampaignId, request.ProjectId, errors);
        ValidateRole(request.Role, errors);
    }

    public static void ValidateSet(RoleNoteSetRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateLevel(request.CampaignId, request.ProjectId, errors);
        ValidateRole(request.Role, errors);
        ValidateNote(request.Note, errors);
        ValidateReason(request.Reason, errors);
    }

    public static void ValidateList(RoleNoteListRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);
        ValidateLevel(request.CampaignId, request.ProjectId, errors);
    }

    /// <summary>
    /// One level per call. Both ids is two levels, and neither is the one the caller meant more than the other, so both
    /// fields are named. A blank id names no level — it is what an empty variable makes — and is refused by its field
    /// rather than read as the client's, where a write would replace a role's general craft.
    /// </summary>
    private static void ValidateLevel(string? campaignId, string? projectId, ValidationErrors errors)
    {
        if (campaignId is not null && projectId is not null)
        {
            const string OneLevel = "a note is at one level: name campaign_id or project_id, not both, or neither for the client's.";
            errors.Add("campaign_id", "not_allowed", OneLevel);
            errors.Add("project_id", "not_allowed", OneLevel);
            return;
        }

        if (campaignId is not null && string.IsNullOrWhiteSpace(campaignId))
        {
            errors.Add("campaign_id", "invalid", "campaign_id names no campaign; leave it out for a note at the client's level.");
        }

        if (projectId is not null && string.IsNullOrWhiteSpace(projectId))
        {
            errors.Add("project_id", "invalid", "project_id names no project; leave it out for a note at the client's level.");
        }
    }

    /// <summary>
    /// A note is an object, and a caller who sends an array or a sentence is told which field is wrong. The
    /// document's own keys are never inspected: what a role writes is its business, and the runtime neither reads
    /// a note nor acts on one.
    /// </summary>
    private static void ValidateNote(JsonNode? note, ValidationErrors errors)
    {
        if (note is null)
        {
            errors.Add("note", "required", "note is required; write an empty object to clear one.");
            return;
        }

        if (note is not JsonObject)
        {
            errors.Add("note", "invalid", "note must be a JSON object.");
        }
    }

    private static void ValidateRole(string? role, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            errors.Add("role", "required", "role is required.");
            return;
        }

        if (!RoleValidation.RoleName.IsMatch(role.Trim()))
        {
            errors.Add("role", "invalid", "role must be lowercase letters, digits and hyphens, starting with a letter.");
        }
    }

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > RoleNoteService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {RoleNoteService.MaxReasonLength} characters."));
        }
    }
}
