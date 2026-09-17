using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Json;

namespace Jason.Runtime.Reports;

/// <summary>
/// One submission, read out of the document it arrived as. The document itself is kept: a report is somebody's
/// words, and words that have been normalised on the way in are no longer the ones that were said.
/// </summary>
/// <param name="Assertion">The submitted body minus the envelope, exactly as it was sent.</param>
/// <param name="AssertionHash">Its canonical <c>sha256:</c>, which decides a repeat when no key was given.</param>
internal sealed record ReportSubmission(
    JsonObject Assertion,
    string AssertionHash,
    ActorRef Reporter,
    string? Reason,
    string? IdempotencyKey,
    string Effect,
    string Tool,
    string? Provider,
    string? Account,
    DateTime? OccurredAt,
    DateTime? ObservedAt,
    string? CampaignId,
    string? ContactId,
    string? WorkItemId,
    string? Operation,
    string Summary)
{
    /// <summary>
    /// Reads a submission, refusing in one order: keys the API does not know, then what is missing, then what is
    /// the wrong shape, then what is too large. Field problems accumulate, so one refusal names everything wrong
    /// with the request rather than making a caller find the next problem by fixing this one.
    /// </summary>
    public static ReportSubmission Parse(JsonObject body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var errors = new ValidationErrors();
        Keys(body, errors);
        errors.ThrowIfAny();

        var reporter = ReadReporter(body);

        var effect = Text(body, ReportFields.Effect, errors, required: true);
        var tool = Text(body, ReportFields.Tool, errors, required: true);
        var provider = Text(body, ReportFields.Provider, errors, required: false);
        var account = Text(body, ReportFields.Account, errors, required: false);
        var operation = Text(body, ReportFields.Operation, errors, required: false);
        var idempotencyKey = Text(body, ReportFields.IdempotencyKey, errors, required: false);
        var campaignId = Text(body, ReportFields.CampaignId, errors, required: false);
        var contactId = Text(body, ReportFields.ContactId, errors, required: false);
        var workItemId = Text(body, ReportFields.WorkItemId, errors, required: false);

        var summary = Prose(body, ReportFields.Summary, errors, required: true);
        var uncertainty = Prose(body, ReportFields.Uncertainty, errors, required: false);
        var reason = Prose(body, ReportFields.Reason, errors, required: false);
        _ = uncertainty;

        var occurredAt = Timestamp(body, ReportFields.OccurredAt, errors);
        var observedAt = Timestamp(body, ReportFields.ObservedAt, errors);

        ExternalIds(body, errors);
        Evidence(body, errors);
        UnknownFields(body, errors);

        errors.ThrowIfAny();

        var assertion = body.DeepClone().AsObject();
        assertion.Remove(ReportFields.Actor);
        assertion.Remove(ReportFields.Reason);

        return new ReportSubmission(
            assertion,
            CanonicalJson.Hash(assertion),
            reporter,
            reason,
            idempotencyKey,
            effect!,
            tool!,
            provider,
            account,
            occurredAt,
            observedAt,
            campaignId,
            contactId,
            workItemId,
            operation,
            summary!);
    }

    private static void Keys(JsonObject body, ValidationErrors errors)
    {
        foreach (var (name, _) in body)
        {
            if (ReportFields.Reserved.Contains(name))
            {
                errors.Add(name, "field_reserved", $"'{name}' is written by the runtime when a report is admitted and cannot be submitted.");
            }
            else if (!ReportFields.Assertion.Contains(name) && !ReportFields.Envelope.Contains(name))
            {
                errors.Add(name, "field_unknown", $"'{name}' is not a field of a report submission.");
            }
        }
    }

    /// <summary>
    /// Who is speaking. A report names its reporter or it is not provenance, so an anonymous human is refused
    /// here even though it is the right default for creating work.
    /// </summary>
    private static ActorRef ReadReporter(JsonObject body)
    {
        if (!body.TryGetPropertyValue(ReportFields.Actor, out var node) || node is null)
        {
            throw DomainErrors.ActorRequired();
        }

        ActorRef? claimed;
        try
        {
            claimed = node.Deserialize<ActorRef>(JasonJson.Options);
        }
        catch (JsonException)
        {
            throw new ValidationException([new ErrorDetail(ReportFields.Actor, "invalid", "actor must be an object naming a type and an id.")]);
        }

        if (claimed is null)
        {
            throw DomainErrors.ActorRequired();
        }

        if (claimed.Type == ActorType.System)
        {
            throw DomainErrors.ReporterReserved();
        }

        var resolved = Actors.Resolve(claimed);
        return string.IsNullOrWhiteSpace(resolved.Id) ? throw DomainErrors.ActorRequired() : resolved;
    }

    private static string? Text(JsonObject body, string field, ValidationErrors errors, bool required) =>
        String(body, field, errors, required, ReportService.MaxTextLength);

    private static string? Prose(JsonObject body, string field, ValidationErrors errors, bool required) =>
        String(body, field, errors, required, ReportService.MaxProseLength);

    private static string? String(JsonObject body, string field, ValidationErrors errors, bool required, int max)
    {
        if (!body.TryGetPropertyValue(field, out var node) || node is null)
        {
            if (required)
            {
                errors.Add(field, "required", $"{field} is required.");
            }

            return null;
        }

        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
        {
            errors.Add(field, "invalid", $"{field} must be a string.");
            return null;
        }

        var text = value.GetValue<string>().Trim();
        if (text.Length == 0)
        {
            if (required)
            {
                errors.Add(field, "required", $"{field} is required.");
            }

            return null;
        }

        if (text.Length > max)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must be at most {max} characters."));
            return null;
        }

        return text;
    }

    private static DateTime? Timestamp(JsonObject body, string field, ValidationErrors errors)
    {
        if (!body.TryGetPropertyValue(field, out var node) || node is null)
        {
            return null;
        }

        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
        {
            errors.Add(field, "invalid", $"{field} must be an ISO-8601 timestamp.");
            return null;
        }

        // A time carrying no zone is read as UTC rather than as the zone of whatever machine this runtime runs
        // on: the reporter named an instant, and a host-local reading would store a different one per
        // installation with nothing in the row to say which had been assumed.
        const DateTimeStyles AsUtc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (!DateTimeOffset.TryParse(value.GetValue<string>(), CultureInfo.InvariantCulture, AsUtc, out var parsed))
        {
            errors.Add(field, "invalid", $"{field} must be an ISO-8601 timestamp.");
            return null;
        }

        return parsed.UtcDateTime;
    }

    private static void ExternalIds(JsonObject body, ValidationErrors errors)
    {
        if (!body.TryGetPropertyValue(ReportFields.ExternalIds, out var node) || node is null)
        {
            return;
        }

        if (node is not JsonArray entries)
        {
            errors.Add(ReportFields.ExternalIds, "invalid", "external_ids must be an array of {kind, value} objects.");
            return;
        }

        if (entries.Count > ReportService.MaxListEntries)
        {
            errors.Add(ReportFields.ExternalIds, "too_many", string.Create(CultureInfo.InvariantCulture, $"external_ids must have at most {ReportService.MaxListEntries} entries."));
            return;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var field = string.Create(CultureInfo.InvariantCulture, $"external_ids[{index}]");
            if (entries[index] is not JsonObject entry)
            {
                errors.Add(field, "invalid", $"{field} must be an object naming a kind and a value.");
                continue;
            }

            foreach (var part in (string[])["kind", "value"])
            {
                if (entry[part] is not JsonValue text || text.GetValueKind() != JsonValueKind.String || text.GetValue<string>().Trim().Length == 0)
                {
                    errors.Add($"{field}.{part}", "required", $"{field}.{part} is required and must be a string.");
                }
                else if (text.GetValue<string>().Length > ReportService.MaxTextLength)
                {
                    errors.Add($"{field}.{part}", "too_long", string.Create(CultureInfo.InvariantCulture, $"{field}.{part} must be at most {ReportService.MaxTextLength} characters."));
                }
            }
        }
    }

    /// <summary>Evidence is a note about what happened, not the thing itself: the journal's own limit is the
    /// right size for one and refuses the other.</summary>
    private static void Evidence(JsonObject body, ValidationErrors errors)
    {
        if (!body.TryGetPropertyValue(ReportFields.Evidence, out var node) || node is null)
        {
            return;
        }

        if (node is not JsonObject evidence)
        {
            errors.Add(ReportFields.Evidence, "invalid", "evidence must be an object.");
            return;
        }

        if (CanonicalJson.Measure(evidence) is { } measured && measured.Bytes > ReportService.MaxEvidenceBytes)
        {
            errors.Add(ReportFields.Evidence, "too_large", string.Create(CultureInfo.InvariantCulture, $"evidence must serialize to at most {ReportService.MaxEvidenceBytes} bytes."));
        }
    }

    /// <summary>
    /// A reporter may say which fields they cannot supply. Each has to name a field that exists: a marker
    /// nobody can interpret is worse than no marker, and a typo would be exactly that.
    /// </summary>
    private static void UnknownFields(JsonObject body, ValidationErrors errors)
    {
        if (!body.TryGetPropertyValue(ReportFields.UnknownFields, out var node) || node is null)
        {
            return;
        }

        if (node is not JsonArray named)
        {
            errors.Add(ReportFields.UnknownFields, "invalid", "unknown_fields must be an array of field names.");
            return;
        }

        if (named.Count > ReportService.MaxListEntries)
        {
            errors.Add(ReportFields.UnknownFields, "too_many", string.Create(CultureInfo.InvariantCulture, $"unknown_fields must have at most {ReportService.MaxListEntries} entries."));
            return;
        }

        for (var index = 0; index < named.Count; index++)
        {
            var field = string.Create(CultureInfo.InvariantCulture, $"unknown_fields[{index}]");
            if (named[index] is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
            {
                errors.Add(field, "invalid", $"{field} must be a string.");
            }
            else if (!ReportFields.Assertion.Contains(value.GetValue<string>()))
            {
                errors.Add(field, "unknown_field_unknown", $"{field} must name a field of a report submission.");
            }
        }
    }
}
