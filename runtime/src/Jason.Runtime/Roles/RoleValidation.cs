using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Roles;

/// <summary>
/// What a role registration has to look like. The name is the identity — work items and actors reference roles
/// by it — so it is held to the same shape as every other name an agent has to type from memory.
/// </summary>
internal static partial class RoleValidation
{
    /// <summary>Lowercase, hyphenated: the roster's own spelling, <c>deliverability-specialist</c> included.</summary>
    public static Regex RoleName { get; } = Name();

    public static void ValidateAdd(RoleAddRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        ValidateDescription(request.Description, errors);
        ValidateEntryCommand(request.EntryCommand, errors);
        ValidateProfileDefaults(request.ProfileDefaults, errors);
        ValidateReason(request.Reason, errors);
    }

    /// <summary>
    /// The narrow verb that moves a role's policy. The name identifies the role, so it is held to the same
    /// shape a registration is — a name that could never have been registered is the caller's typo rather than
    /// a role nobody added.
    /// </summary>
    public static void ValidateSetProfile(RoleSetProfileRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        ValidateReason(request.Reason, errors);
    }

    private static void ValidateName(string? name, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name", "required", "name is required.");
            return;
        }

        if (!RoleName.IsMatch(name.Trim()))
        {
            errors.Add("name", "invalid", string.Create(CultureInfo.InvariantCulture, $"name must be lowercase letters, digits and hyphens, starting with a letter, at most {RoleService.MaxNameLength} characters."));
        }
    }

    private static void ValidateDescription(string? description, ValidationErrors errors)
    {
        if (description is not null && description.Trim().Length > RoleService.MaxDescriptionLength)
        {
            errors.Add("description", "too_long", string.Create(CultureInfo.InvariantCulture, $"description must be at most {RoleService.MaxDescriptionLength} characters."));
        }
    }

    private static void ValidateEntryCommand(IReadOnlyList<string>? entryCommand, ValidationErrors errors)
    {
        if (entryCommand is null)
        {
            return;
        }

        if (entryCommand.Count > RoleService.MaxEntryCommandArgs)
        {
            errors.Add("entry_command", "too_long", string.Create(CultureInfo.InvariantCulture, $"entry_command must have at most {RoleService.MaxEntryCommandArgs} arguments."));
            return;
        }

        for (var index = 0; index < entryCommand.Count; index++)
        {
            var argument = entryCommand[index];
            if (!string.IsNullOrWhiteSpace(argument) && argument.Length <= RoleService.MaxEntryCommandArgLength)
            {
                continue;
            }

            errors.Add(
                string.Create(CultureInfo.InvariantCulture, $"entry_command[{index}]"),
                "invalid",
                string.Create(CultureInfo.InvariantCulture, $"every entry_command argument must be non-blank and at most {RoleService.MaxEntryCommandArgLength} characters."));
        }
    }

    private static void ValidateProfileDefaults(JsonObject? profileDefaults, ValidationErrors errors)
    {
        if (profileDefaults is not null && JsonSerializer.SerializeToUtf8Bytes(profileDefaults, JasonJson.Options).Length > RoleService.MaxProfileDefaultsBytes)
        {
            errors.Add("profile_defaults", "too_large", string.Create(CultureInfo.InvariantCulture, $"profile_defaults must serialize to at most {RoleService.MaxProfileDefaultsBytes} bytes."));
        }
    }

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > RoleService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {RoleService.MaxReasonLength} characters."));
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$")]
    private static partial Regex Name();
}
