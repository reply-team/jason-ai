using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// What a caller may ask a work item to be. Every rule collects into one <see cref="ValidationErrors"/> rather
/// than throwing at the first problem, so a caller fixing a create call learns everything wrong with it at once;
/// no message repeats the submitted value.
/// </summary>
internal static partial class WorkItemValidation
{
    /// <summary>Role names are the roster's own spelling: lowercase with hyphens, as in <c>deliverability-specialist</c>.</summary>
    public static Regex RoleName { get; } = RoleNamePattern();

    /// <summary>A vendor-neutral operation is dotted lowercase, as in <c>contacts.enroll</c>.</summary>
    public static Regex OperationName { get; } = OperationNamePattern();

    public static void ValidateCreate(WorkItemCreateRequest request, bool roleExists, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        if (string.IsNullOrWhiteSpace(request.CampaignId))
        {
            errors.Add("campaign_id", "required", "campaign_id is required.");
        }

        if (request.Kind is null)
        {
            errors.Add("kind", "required", "kind is required.");
        }
        else if (request.Kind == WorkItemKind.AiRole)
        {
            ValidateRole(request.Role, roleExists, errors);
            if (request.Operation is not null)
            {
                errors.Add("operation", "not_allowed", "operation belongs to provider_op work; ai_role work names a role.");
            }
        }
        else
        {
            ValidateOperation(request.Operation, errors);
            if (request.Role is not null)
            {
                errors.Add("role", "not_allowed", "role belongs to ai_role work; provider_op work names an operation.");
            }

            if (request.ResultFormat is not null)
            {
                errors.Add("result_format", "not_allowed", "result_format belongs to ai_role work; a provider operation answers in its own shape.");
            }
        }

        ValidateExecutionProfile(request.ExecutionProfile, errors);
        ValidateWindow(request.NotBefore?.UtcDateTime, request.DueAt?.UtcDateTime, errors);
        ValidateOverrides(request.TimeoutSeconds, request.HeartbeatSeconds, request.MaxAttempts, errors);

        // A provider_op has already been told it may not carry one at all; measuring it too would report the
        // same field twice for a single mistake.
        if (request.Kind != WorkItemKind.ProviderOp)
        {
            ValidateResultFormat(request.ResultFormat, errors);
        }

        ValidateReason(request.Reason, errors);
    }

    /// <summary>The per-item overrides of the kind's defaults; null means "use the default" and is always allowed.</summary>
    public static void ValidateOverrides(int? timeoutSeconds, int? heartbeatSeconds, int? maxAttempts, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (timeoutSeconds is { } timeout && timeout is < 30 or > 86400)
        {
            errors.Add("timeout_seconds", "invalid", "timeout_seconds must be between 30 and 86400.");
        }

        if (heartbeatSeconds is { } heartbeat && heartbeat != 0 && heartbeat is < 10 or > 3600)
        {
            errors.Add("heartbeat_seconds", "invalid", "heartbeat_seconds must be 0 (no heartbeat) or between 10 and 3600.");
        }

        if (maxAttempts is { } attempts && attempts is < 1 or > 10)
        {
            errors.Add("max_attempts", "invalid", "max_attempts must be between 1 and 10.");
        }
    }

    /// <summary>A window that closes before it opens can never be run, so it is refused rather than stored.</summary>
    public static void ValidateWindow(DateTime? notBefore, DateTime? dueAt, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (notBefore is { } start && dueAt is { } deadline && deadline <= start)
        {
            errors.Add("due_at", "due_before_start", "due_at must be later than not_before.");
        }
    }

    public static void ValidateResultFormat(JsonNode? resultFormat, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (resultFormat is null)
        {
            return;
        }

        if (JsonSerializer.SerializeToUtf8Bytes(resultFormat, JasonJson.Options).Length > WorkItemService.MaxResultFormatBytes)
        {
            errors.Add(
                "result_format",
                "too_large",
                string.Create(CultureInfo.InvariantCulture, $"result_format must serialize to at most {WorkItemService.MaxResultFormatBytes} bytes."));
        }
    }

    public static void ValidateExecutionProfile(string? executionProfile, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (executionProfile is not null && executionProfile.Trim().Length > WorkItemService.MaxExecutionProfileLength)
        {
            errors.Add(
                "execution_profile",
                "too_long",
                string.Create(CultureInfo.InvariantCulture, $"execution_profile must be at most {WorkItemService.MaxExecutionProfileLength} characters."));
        }
    }

    public static void ValidateReason(string? reason, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (reason is not null && reason.Trim().Length > CampaignService.MaxReasonLength)
        {
            errors.Add(
                "reason",
                "too_long",
                string.Create(CultureInfo.InvariantCulture, $"reason must be at most {CampaignService.MaxReasonLength} characters."));
        }
    }

    private static void ValidateRole(string? role, bool roleExists, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            errors.Add("role", "required", "role is required for ai_role work.");
            return;
        }

        if (!RoleName.IsMatch(role.Trim()))
        {
            errors.Add(
                "role",
                "invalid",
                string.Create(CultureInfo.InvariantCulture, $"role must be lowercase letters, digits and hyphens, start with a letter and be at most {WorkItemService.MaxRoleLength} characters."));
            return;
        }

        // An unknown role would never be launchable, and a typo is far likelier than a role nobody added yet.
        if (!roleExists)
        {
            errors.Add("role", "unknown", "role must name a role this runtime knows; register it with role.add first.");
        }
    }

    private static void ValidateOperation(string? operation, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            errors.Add("operation", "required", "operation is required for provider_op work.");
            return;
        }

        var name = operation.Trim();
        if (name.Length > WorkItemService.MaxOperationLength || !OperationName.IsMatch(name))
        {
            errors.Add(
                "operation",
                "invalid",
                string.Create(CultureInfo.InvariantCulture, $"operation must be dotted lowercase words, such as contacts.enroll, and at most {WorkItemService.MaxOperationLength} characters."));
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$")]
    private static partial Regex RoleNamePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$")]
    private static partial Regex OperationNamePattern();
}
