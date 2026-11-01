using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests;

public class JasonJsonTests
{
    private enum Sample { AwaitingApproval }

    private sealed record Envelope(Sample Kind, DateTimeOffset At, string? Missing);

    [Fact]
    [Critical(Family.R2, "the JSON dialect in one assertion")]
    public void Properties_are_snake_case_and_enums_are_snake_case_strings()
    {
        var json = JsonSerializer.Serialize(new Envelope(Sample.AwaitingApproval, DateTimeOffset.UnixEpoch, null), JasonJson.Options);
        Assert.Equal("{\"kind\":\"awaiting_approval\",\"at\":\"1970-01-01T00:00:00.000Z\",\"missing\":null}", json);
    }

    [Fact]
    [Critical(Family.R2, "ISO-8601 UTC timestamps")]
    public void Timestamps_are_written_in_utc_with_z_and_read_back()
    {
        var local = new DateTimeOffset(2026, 9, 14, 12, 30, 0, TimeSpan.FromHours(3));
        var json = JsonSerializer.Serialize(new Envelope(Sample.AwaitingApproval, local, null), JasonJson.Options);
        Assert.Contains("\"at\":\"2026-09-14T09:30:00.000Z\"", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<Envelope>(json, JasonJson.Options)!;
        Assert.Equal(local.ToUniversalTime(), back.At);
    }

    /// <summary>
    /// A timestamp the dialect cannot read is a body that is not valid for the operation, refused the way every other
    /// such body is: as a <see cref="JsonException"/>. And the refusal does not quote what it refused. A runtime log
    /// writes an exception whole, and the text a person typed where a moment belonged is still the text they typed.
    /// </summary>
    [Fact]
    public void A_timestamp_the_dialect_cannot_read_is_refused_without_quoting_it()
    {
        const string sentinel = "SENTINEL_SINCE_yesterday";

        var refused = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Envelope>(
            "{\"kind\":\"awaiting_approval\",\"at\":\"" + sentinel + "\"}",
            JasonJson.Options));

        Assert.DoesNotContain(sentinel, refused.ToString(), StringComparison.Ordinal);
        Assert.Contains("ISO-8601", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enums_travel_as_snake_case_text_and_nothing_else()
    {
        var back = JsonSerializer.Deserialize<Envelope>("{\"kind\":\"awaiting_approval\",\"at\":\"1970-01-01T00:00:00.000Z\"}", JasonJson.Options)!;
        Assert.Equal(Sample.AwaitingApproval, back.Kind);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Envelope>("{\"kind\":0,\"at\":\"1970-01-01T00:00:00.000Z\"}", JasonJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Envelope>("{\"kind\":\"not_a_kind\",\"at\":\"1970-01-01T00:00:00.000Z\"}", JasonJson.Options));
    }

    [Fact]
    [Critical(Family.R2, "{\"error\":{code,message,retryable}}")]
    public void Error_envelope_has_the_agreed_shape()
    {
        var json = JsonSerializer.Serialize(new ErrorResponse(new ErrorBody("not_found", "Unknown operation.", false)), JasonJson.Options);
        Assert.Equal("{\"error\":{\"code\":\"not_found\",\"message\":\"Unknown operation.\",\"retryable\":false}}", json);
    }

    [Fact]
    public void Descriptor_round_trips()
    {
        var descriptor = new RuntimeDescriptor("v1", "0.1.0-dev", "rt_01J0000000000000000000000A", 4242, "http://127.0.0.1:5000", "tok", DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize(descriptor, JasonJson.Options);
        Assert.Contains("\"base_url\":\"http://127.0.0.1:5000\"", json, StringComparison.Ordinal);
        Assert.Contains("\"instance_id\":", json, StringComparison.Ordinal);
        Assert.Equal(descriptor, JsonSerializer.Deserialize<RuntimeDescriptor>(json, JasonJson.Options));
    }

    /// <summary>
    /// The dialect's naming policy reaches dictionary keys, which is right for a map Jason names and wrong for
    /// one a provider names. The identifiers an attempt kept are the provider's own words, so they survive the
    /// round trip spelled as they arrived; and a value that is not a string is refused rather than read as one.
    /// </summary>
    [Fact]
    public void The_identifiers_an_attempt_kept_travel_under_the_keys_the_plugin_used()
    {
        var provenance = new AttemptProvenanceDto(
            null, null, null, null, null, null, null, null, null, null, null,
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["contactId"] = "p_1001" },
            ExternalIdOrigins: new Dictionary<string, string>(StringComparer.Ordinal) { ["contactId"] = "created" });

        var json = JsonSerializer.Serialize(provenance, JasonJson.Options);

        Assert.Contains("\"external_ids_returned\":{\"contactId\":\"p_1001\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"external_id_origins\":{\"contactId\":\"created\"}", json, StringComparison.Ordinal);
        Assert.Equal(
            "contactId",
            Assert.Single(JsonSerializer.Deserialize<AttemptProvenanceDto>(json, JasonJson.Options)!.ExternalIdOrigins!).Key);
        Assert.Equal(
            "contactId",
            Assert.Single(JsonSerializer.Deserialize<AttemptProvenanceDto>(json, JasonJson.Options)!.ExternalIdsReturned!).Key);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AttemptProvenanceDto>(
            "{\"external_ids_returned\":{\"contactId\":1001}}",
            JasonJson.Options));
    }

    /// <summary>
    /// The keys of that map are a provider's words, and a refusal of the value under one says where it stopped without
    /// saying the word: what a provider answered is as much somebody else's text as what a person typed.
    /// </summary>
    [Fact]
    public void A_map_of_identifiers_that_refuses_a_value_does_not_quote_its_key()
    {
        const string sentinel = "SENTINEL_KEY_contactId";

        var refused = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AttemptProvenanceDto>(
            "{\"external_ids_returned\":{\"" + sentinel + "\":1001}}",
            JasonJson.Options));

        Assert.DoesNotContain(sentinel, refused.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_configures_foreign_options_identically()
    {
        var target = new JsonSerializerOptions();
        JasonJson.Apply(target);
        var expected = JsonSerializer.Serialize(new Envelope(Sample.AwaitingApproval, DateTimeOffset.UnixEpoch, "x"), JasonJson.Options);
        Assert.Equal(expected, JsonSerializer.Serialize(new Envelope(Sample.AwaitingApproval, DateTimeOffset.UnixEpoch, "x"), target));
    }
}
