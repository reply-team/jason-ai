using System.Text.Json.Serialization;

namespace Jason.Contracts.Api;

/// <summary>
/// What one plugin calls one of our entities, and when Jason learned it. The pin is written once and never
/// rewritten; when a plugin later answers with something else the disagreement is carried in the three
/// <c>diverged_*</c> fields, which are absent while the provider keeps agreeing with itself.
/// </summary>
/// <remarks>
/// A caller cannot write one of these. A pin is what a plugin answered with, recorded by the attempt that ran it,
/// so an <c>external_ids</c> field in a request means nothing and changes nothing.
/// </remarks>
public sealed record ExternalIdDto(
    string PluginId,
    string Kind,
    string Value,
    DateTimeOffset RecordedAt,
    string RecordedByAttemptId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DivergedValue = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DivergedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DivergedByAttemptId = null);
