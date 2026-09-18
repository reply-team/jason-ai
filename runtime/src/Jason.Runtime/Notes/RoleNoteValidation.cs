using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Roles;

namespace Jason.Runtime.Notes;

/// <summary>
/// What a note request has to look like before anything is loaded. The role name is held to the roster's own
/// shape, so a name that could never have been registered is the caller's typo rather than a role nobody added.
/// </summary>
internal static class RoleNoteValidation
{
    public static void ValidateGet(RoleNoteGetRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);
        ValidateRole(request.Role, errors);
    }

    public static void ValidateSet(RoleNoteSetRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateRole(request.Role, errors);
        ValidateNote(request.Note, errors);
        ValidateReason(request.Reason, errors);
    }

    /// <summary>
    /// A note is an object, and a caller who sends an array or a sentence is told which field is wrong. The
    /// document's own keys are never inspected: what a role writes about a campaign is its business, and the
    /// runtime neither reads a note nor acts on one.
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
