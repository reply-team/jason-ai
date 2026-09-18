using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Profiles;

/// <summary>
/// What a profile has to look like before anything is written. Every rule here is refused now rather than
/// discovered at a launch: a profile is read when work is already claimed and an agent is about to start, and a
/// field nobody checked until then would fail an attempt for something a person typed days earlier.
/// </summary>
internal static partial class ProfileValidation
{
    /// <summary>The same shape as a role's name: work, campaigns, roles and settings all spell it from memory.</summary>
    public static Regex ProfileName { get; } = Name();

    public static void ValidateCreate(ProfileCreateRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        ValidateDescription(request.Description, errors);
        ValidateHost(request.Host, errors);
        ValidateProgram(request.Program, errors);
        ValidateArgs(request.Args, errors);
        ValidateDeny(request.Deny, errors);
        ValidateCliCommand(request.CliCommand, errors);
        ValidateHostVersion(request.HostVersionVerified, errors);
        ValidateReason(request.Reason, errors);
    }

    /// <summary>
    /// A patch is checked for what it names. An absent field is not a value the caller offered, so there is
    /// nothing to hold it to; a present one is held to exactly the rule it would have been held to at creation.
    /// </summary>
    public static void ValidateUpdate(ProfileUpdateRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        if (request.Description.IsSet)
        {
            ValidateDescription(request.Description.Value, errors);
        }

        if (request.Host.IsSet)
        {
            // Null would leave the revision without a host, and a revision is a whole statement of how to launch
            // something. Clearing it is refused the same way an unknown one is.
            ValidateHost(request.Host.Value, errors);
        }

        if (request.Program.IsSet)
        {
            ValidateProgram(request.Program.Value, errors);
        }

        if (request.Args.IsSet)
        {
            ValidateArgs(request.Args.Value, errors);
        }

        if (request.Deny.IsSet)
        {
            ValidateDeny(request.Deny.Value, errors);
        }

        if (request.CliCommand.IsSet)
        {
            ValidateCliCommand(request.CliCommand.Value, errors);
        }

        if (request.HostVersionVerified.IsSet)
        {
            ValidateHostVersion(request.HostVersionVerified.Value, errors);
        }

        ValidateReason(request.Reason, errors);
    }

    /// <summary>A read names a profile, and may name one of its revisions. They are numbered from 1.</summary>
    public static void ValidateGet(ProfileGetRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        if (request.Revision is < 1)
        {
            errors.Add("revision", "invalid", "revision must be 1 or more; revisions are numbered from 1.");
        }
    }

    /// <summary>Taking a profile out of service, or putting it back: the name, and why.</summary>
    public static void ValidateToggle(ProfileToggleRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        ValidateName(request.Name, errors);
        ValidateReason(request.Reason, errors);
    }

    /// <summary>
    /// The host as the closed vocabulary spells it. A value outside it is a name this build has no launcher for,
    /// and the message says what it does have rather than leaving the caller to guess.
    /// </summary>
    public static AgentHostKind? ParseHost(string? host)
    {
        var text = host?.Trim();
        return text is null
            ? null
            : Enum.GetValues<AgentHostKind>()
                .Where(kind => string.Equals(SnakeCaseEnumConverter<AgentHostKind>.Format(kind), text, StringComparison.Ordinal))
                .Select(kind => (AgentHostKind?)kind)
                .FirstOrDefault();
    }

    private static void ValidateName(string? name, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name", "required", "name is required.");
            return;
        }

        if (!ProfileName.IsMatch(name.Trim()))
        {
            errors.Add(
                "name",
                "invalid",
                string.Create(CultureInfo.InvariantCulture, $"name must be lowercase letters, digits and hyphens, starting with a letter, at most {ProfileService.MaxNameLength} characters."));
        }
    }

    private static void ValidateDescription(string? description, ValidationErrors errors)
    {
        if (description is not null && description.Trim().Length > ProfileService.MaxDescriptionLength)
        {
            errors.Add("description", "too_long", string.Create(CultureInfo.InvariantCulture, $"description must be at most {ProfileService.MaxDescriptionLength} characters."));
        }
    }

    private static void ValidateHost(string? host, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            errors.Add("host", "required", "host is required.");
            return;
        }

        if (ParseHost(host) is null)
        {
            errors.Add("host", "invalid", "host must be one of: " + string.Join(", ", Hosts()) + ".");
        }
    }

    private static void ValidateProgram(string? program, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            errors.Add("program", "required", "program is required.");
            return;
        }

        if (program.Trim().Length > ProfileService.MaxProgramLength)
        {
            errors.Add("program", "too_long", string.Create(CultureInfo.InvariantCulture, $"program must be at most {ProfileService.MaxProgramLength} characters."));
        }
    }

    /// <summary>The list as it will be stored, once it is known to be one. Blank entries never reach here.</summary>
    public static List<string> Strings(IReadOnlyList<JsonNode?>? values) =>
        values is null ? [] : [.. values.Select(value => value!.GetValue<string>().Trim())];

    private static void ValidateArgs(IReadOnlyList<JsonNode?>? args, ValidationErrors errors) =>
        ValidateStrings(args, "args", ProfileService.MaxArgs, ProfileService.MaxArgLength, errors);

    private static void ValidateDeny(IReadOnlyList<JsonNode?>? deny, ValidationErrors errors) =>
        ValidateStrings(deny, "deny", ProfileService.MaxDenyEntries, ProfileService.MaxDenyEntryLength, errors);

    /// <summary>
    /// A bounded list of non-blank, bounded strings. The count is checked before the entries so that a list of
    /// ten thousand does not answer with ten thousand problems.
    /// </summary>
    private static void ValidateStrings(IReadOnlyList<JsonNode?>? values, string field, int maxCount, int maxLength, ValidationErrors errors)
    {
        if (values is null)
        {
            return;
        }

        if (values.Count > maxCount)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must have at most {maxCount} entries."));
            return;
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (Text(values[index]) is { } value && !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength)
            {
                continue;
            }

            errors.Add(
                string.Create(CultureInfo.InvariantCulture, $"{field}[{index}]"),
                "invalid",
                string.Create(CultureInfo.InvariantCulture, $"every {field} entry must be a non-blank string of at most {maxLength} characters."));
        }
    }

    /// <summary>An entry that is a JSON string, or nothing — an object, a number and a null are all "not a string".</summary>
    private static string? Text(JsonNode? entry) =>
        entry is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// A bare file name, because this is what the launched agent types to call home and the runtime is what puts
    /// something of that name within its reach. A path would name a program the runtime did not put there.
    /// </summary>
    private static void ValidateCliCommand(string? cliCommand, ValidationErrors errors)
    {
        if (cliCommand is null)
        {
            return;
        }

        var value = cliCommand.Trim();
        if (value.Length == 0)
        {
            errors.Add("cli_command", "invalid", "cli_command must be a non-blank command name, or absent to use the runtime's own.");
            return;
        }

        if (value.Length > ProfileService.MaxCliCommandLength)
        {
            errors.Add("cli_command", "too_long", string.Create(CultureInfo.InvariantCulture, $"cli_command must be at most {ProfileService.MaxCliCommandLength} characters."));
            return;
        }

        var separated = value.AsSpan().IndexOfAny('/', '\\', ':') >= 0;
        if (separated || value is "." or ".." || value.Any(char.IsControl))
        {
            errors.Add("cli_command", "invalid", "cli_command must be a bare command name: no directory, no path, no drive.");
        }
    }

    private static void ValidateHostVersion(string? hostVersion, ValidationErrors errors)
    {
        if (hostVersion is not null && hostVersion.Trim().Length > ProfileService.MaxHostVersionLength)
        {
            errors.Add("host_version_verified", "too_long", string.Create(CultureInfo.InvariantCulture, $"host_version_verified must be at most {ProfileService.MaxHostVersionLength} characters."));
        }
    }

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > ProfileService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {ProfileService.MaxReasonLength} characters."));
        }
    }

    /// <summary>The vocabulary as the API spells it, so the message names values a caller can send back.</summary>
    private static IEnumerable<string> Hosts() =>
        Enum.GetValues<AgentHostKind>().Select(SnakeCaseEnumConverter<AgentHostKind>.Format);

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$")]
    private static partial Regex Name();
}
