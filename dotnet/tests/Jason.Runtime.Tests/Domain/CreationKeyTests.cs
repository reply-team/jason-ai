using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Json;

namespace Jason.Runtime.Tests.Domain;

/// <summary>
/// The key a caller gives a create, and the one name of the request it came with. A key is the caller's text, so it is
/// taken as typed apart from the white space around it, and refused rather than quoted when it cannot be one. The
/// digest is what "the same request" means: how a request was spelled never makes two of it, and what it asks for
/// always does.
/// </summary>
public class CreationKeyTests
{
    private const string Sentinel = "SENTINEL_KEY_7f3c";

    // -------------------------------------------------------------------------------------------------------
    // The key
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void No_key_is_no_key()
    {
        var errors = new ValidationErrors();
        Assert.Null(CreationKey.Read(null, errors));
        Assert.False(errors.Any);
    }

    [Fact]
    public void A_key_is_taken_as_typed_apart_from_the_white_space_around_it()
    {
        var errors = new ValidationErrors();
        Assert.Equal("launch q3 / 1", CreationKey.Read("  launch q3 / 1\t", errors));
        Assert.False(errors.Any);
    }

    [Fact]
    public void A_key_may_be_128_characters_long_and_no_longer()
    {
        var accepted = new ValidationErrors();
        Assert.Equal(new string('k', 128), CreationKey.Read(new string('k', 128), accepted));
        Assert.False(accepted.Any);

        var refused = new ValidationErrors();
        Assert.Null(CreationKey.Read(Sentinel + new string('k', 129 - Sentinel.Length), refused));
        Assert.Equal(("idempotency_key", "too_long"), Single(refused));
    }

    /// <summary>A character outside the Basic Multilingual Plane is two of the 128, as it is two of a report key's 200.</summary>
    [Fact]
    public void A_character_written_as_a_surrogate_pair_counts_as_two()
    {
        var pair = char.ConvertFromUtf32(0x1F600);
        var accepted = new ValidationErrors();
        Assert.NotNull(CreationKey.Read(string.Concat(Enumerable.Repeat(pair, 64)), accepted));
        Assert.False(accepted.Any);

        var refused = new ValidationErrors();
        Assert.Null(CreationKey.Read(string.Concat(Enumerable.Repeat(pair, 64)) + "k", refused));
        Assert.Equal(("idempotency_key", "too_long"), Single(refused));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void A_key_of_nothing_but_white_space_is_refused(string key)
    {
        var errors = new ValidationErrors();
        Assert.Null(CreationKey.Read(key, errors));
        Assert.Equal(("idempotency_key", "invalid"), Single(errors));
    }

    /// <summary>
    /// A control character is not something a person chose to type, and half of a surrogate pair cannot be stored in the
    /// database's UTF-8 at all: a key holding one would be written as something else and never found again.
    /// </summary>
    [Theory]
    [InlineData(Sentinel + "\u0000")]
    [InlineData(Sentinel + "\u0007tail")]
    [InlineData(Sentinel + "\u001btail")]
    [InlineData(Sentinel + "\u007ftail")]
    public void A_key_with_a_control_character_is_refused(string key)
    {
        var errors = new ValidationErrors();
        Assert.Null(CreationKey.Read(key, errors));
        Assert.Equal(("idempotency_key", "invalid"), Single(errors));
    }

    /// <summary>Built here rather than handed in as theory data, which is written out as UTF-8 and so cannot carry one.</summary>
    [Fact]
    public void A_key_with_half_a_surrogate_pair_is_refused()
    {
        foreach (var key in new[] { Sentinel + '\ud800' + "tail", Sentinel + '\udc00' + "tail", "tail" + Sentinel + '\ud83d', '\ude00' + Sentinel })
        {
            var errors = new ValidationErrors();
            Assert.Null(CreationKey.Read(key, errors));
            Assert.Equal(("idempotency_key", "invalid"), Single(errors));
        }
    }

    [Fact]
    [Critical(Family.B3, "a key is never echoed")]
    public void No_refusal_quotes_the_key()
    {
        foreach (var key in new[] { Sentinel + new string('k', 200), Sentinel + "\u0007", " " })
        {
            var errors = new ValidationErrors();
            CreationKey.Read(key, errors);
            var refusal = Assert.Throws<ValidationException>(errors.ThrowIfAny);
            Assert.DoesNotContain("SENTINEL", refusal.Message, StringComparison.Ordinal);
            Assert.All(refusal.Details!, detail => Assert.DoesNotContain("SENTINEL", detail.Message, StringComparison.Ordinal));
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // One request: spelling never makes two of it
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_work_item_request_spelled_another_way_is_the_same_request()
    {
        var first = Item("""
            {"campaign_id":"cmp_1","kind":"ai_role","role":"researcher","priority":5,
             "not_before":"2026-10-05T10:00:00Z","context":{"a":1,"b":{"c":"x","d":[1,2]}},"result_format":{"type":"object"},
             "actor":{"type":"human","id":"ada"},"reason":"first try","idempotency_key":"k1"}
            """);
        var second = Item("""
            {  "idempotency_key" : "k2", "reason":"the retry",
               "actor":{"type":"attempt","id":"att_1"},
               "result_format":{"type":"object"}, "context":{"b":{"d":[1.0,2e0],"c":"x"},"a":1.00},
               "not_before":"2026-10-05T03:00:00-07:00",
               "priority":5,"role":" researcher ","kind":"ai_role","campaign_id":" cmp_1 " }
            """);

        Assert.Equal(CreationKey.Digest(first), CreationKey.Digest(second));
    }

    /// <summary>
    /// The request door already reads every instant into UTC, so this is the digest's own guard, held here on its own:
    /// an instant built in another offset names the same moment, and a moment that differs is a different request.
    /// </summary>
    [Fact]
    public void An_instant_in_another_offset_is_the_same_moment_to_the_digest()
    {
        var utc = Item("""{"campaign_id":"cmp_1","kind":"ai_role","role":"researcher"}""") with { NotBefore = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero) };
        var pacific = utc with { NotBefore = new DateTimeOffset(2026, 10, 5, 3, 0, 0, TimeSpan.FromHours(-7)) };
        var later = utc with { NotBefore = new DateTimeOffset(2026, 10, 5, 3, 0, 0, TimeSpan.Zero) };

        Assert.Equal(CreationKey.Digest(utc), CreationKey.Digest(pacific));
        Assert.NotEqual(CreationKey.Digest(utc), CreationKey.Digest(later));
    }

    [Fact]
    public void An_absent_field_and_a_null_one_are_the_same_request()
    {
        Assert.Equal(
            CreationKey.Digest(Item("""{"campaign_id":"cmp_1","kind":"ai_role","role":"researcher"}""")),
            CreationKey.Digest(Item("""{"campaign_id":"cmp_1","kind":"ai_role","role":"researcher","priority":null,"context":null,"due_at":null,"execution_profile":null}""")));
    }

    [Fact]
    [Critical(Family.W8, "the request digest is canonical")]
    public void A_campaign_request_spelled_another_way_is_the_same_request()
    {
        Assert.Equal(
            CreationKey.Digest(Campaign("""{"name":"Q3 LatAm","context":{"region":"latam","size":10},"actor":{"type":"human","id":"ada"},"reason":"start"}""")),
            CreationKey.Digest(Campaign("""{"context":{"size":1e1,"region":"latam"},"name":"  Q3 LatAm ","reason":"again"}""")));
    }

    /// <summary>Each spelling JSON allows for one number — a leading zero before the point, either case of the exponent's
    /// letter, a sign on it, padding in it, a negative zero — is one value.</summary>
    [Theory]
    // Critical: number canonicalisation.
    [InlineData("0.5", "5e-1", "0.50", "50E-2", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.W8)])]
    [InlineData("100", "1E+2", "1e+2", "1e0002")]
    [InlineData("0", "-0", "0.0", "-0.0e5")]
    [InlineData("-1.25", "-125e-2", "-0.125E1", "-12.50e-1")]
    public void Every_spelling_of_one_number_is_one_request(string first, string second, string third, string fourth)
    {
        var digests = new[] { first, second, third, fourth }
            .Select(number => CreationKey.Digest(Campaign(WithNumber(number))))
            .Distinct()
            .ToList();

        Assert.Single(digests);
    }

    /// <summary>
    /// An exponent too long for a 64-bit integer is not read: the number is compared as written. Reading one whole costs
    /// time that grows with the square of its length, and a number like that names nothing anybody spells two ways.
    /// </summary>
    [Fact]
    public void A_number_whose_exponent_cannot_be_a_64_bit_integer_is_compared_as_written()
    {
        var exponent = "1" + new string('0', 18);
        var same = CreationKey.Digest(Campaign(WithNumber("1e" + exponent)));

        Assert.Equal(same, CreationKey.Digest(Campaign(WithNumber("1e" + exponent))));
        Assert.NotEqual(same, CreationKey.Digest(Campaign(WithNumber("1E" + exponent))));
    }

    /// <summary>
    /// A field left out contributes nothing to the digest — not even its name — so a digest stored now is still the
    /// digest of the same request after a later version adds a field nobody sends.
    /// </summary>
    [Fact]
    public void A_field_left_out_contributes_nothing_to_the_digest()
    {
        Assert.Equal(
            CanonicalJson.Hash(new JsonObject { ["name"] = "Q3" }),
            CreationKey.Digest(Campaign("""{"name":"Q3"}""")));
        Assert.Equal(
            CanonicalJson.Hash(new JsonObject { ["campaign_id"] = "cmp_1", ["kind"] = "ai_role", ["role"] = "researcher" }),
            CreationKey.Digest(Item("""{"campaign_id":"cmp_1","kind":"ai_role","role":"researcher"}""")));
    }

    [Fact]
    public void A_digest_is_named_the_way_the_runtime_names_every_document()
    {
        var digest = CreationKey.Digest(Campaign("""{"name":"Q3"}"""));
        Assert.Matches("^sha256:[0-9a-f]{64}$", digest);
        Assert.Equal(71, digest.Length);
    }

    // -------------------------------------------------------------------------------------------------------
    // Two requests: what it asks for always makes two
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every member of the request is either in the digest — so a different value makes a different request — or one of
    /// the three that are not: who asks, why, and the key itself. A member added later fails here until somebody decides.
    /// </summary>
    [Fact]
    public void Every_member_of_a_work_item_request_but_the_actor_the_reason_and_the_key_makes_a_different_request()
    {
        AssertEveryMemberDecides<WorkItemCreateRequest>(CreationKey.Digest);
    }

    [Fact]
    public void Every_member_of_a_campaign_request_but_the_actor_the_reason_and_the_key_makes_a_different_request()
    {
        AssertEveryMemberDecides<CampaignCreateRequest>(CreationKey.Digest);
    }

    [Fact]
    public void A_number_written_as_another_number_is_a_different_request()
    {
        Assert.NotEqual(
            CreationKey.Digest(Campaign("""{"name":"Q3","context":{"size":10}}""")),
            CreationKey.Digest(Campaign("""{"name":"Q3","context":{"size":10.5}}""")));
        Assert.NotEqual(
            CreationKey.Digest(Campaign("""{"name":"Q3","context":{"size":1}}""")),
            CreationKey.Digest(Campaign("""{"name":"Q3","context":{"size":"1"}}""")));
        Assert.NotEqual(
            CreationKey.Digest(Campaign("""{"name":"Q3","context":{"big":123456789012345678901234567890123}}""")),
            CreationKey.Digest(Campaign("""{"name":"Q3","context":{"big":123456789012345678901234567890124}}""")));
    }

    /// <summary>
    /// An absent field is the same as a null one, and neither is the default the service would apply: a caller repeating itself
    /// sends the same body, and a digest tied to defaults would change whenever a default did.
    /// </summary>
    [Fact]
    [Critical(Family.W8, "absent is not zero")]
    public void An_absent_priority_and_a_priority_of_zero_are_two_requests()
    {
        Assert.NotEqual(
            CreationKey.Digest(Item("""{"campaign_id":"cmp_1","kind":"ai_role","role":"researcher"}""")),
            CreationKey.Digest(Item("""{"campaign_id":"cmp_1","kind":"ai_role","role":"researcher","priority":0}""")));
    }

    // -------------------------------------------------------------------------------------------------------

    private static (string Field, string Code) Single(ValidationErrors errors)
    {
        var refusal = Assert.Throws<ValidationException>(errors.ThrowIfAny);
        var detail = Assert.Single(refusal.Details!);
        return (detail.Field, detail.Code);
    }

    private static WorkItemCreateRequest Item(string json) => JsonSerializer.Deserialize<WorkItemCreateRequest>(json, JasonJson.Options)!;

    private static string WithNumber(string number) => "{\"name\":\"Q3\",\"context\":{\"n\":" + number + "}}";

    private static CampaignCreateRequest Campaign(string json) => JsonSerializer.Deserialize<CampaignCreateRequest>(json, JasonJson.Options)!;

    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal) { "Actor", "Reason", "IdempotencyKey" };

    /// <summary>Two different values of each type a request member can have; a type missing here is a member nobody decided about.</summary>
    private static readonly Dictionary<Type, (object? First, object? Second)> Values = new()
    {
        [typeof(string)] = ("alpha", "beta"),
        [typeof(int?)] = (1, 2),
        [typeof(DateTimeOffset?)] = (new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero)),
        [typeof(WorkItemKind?)] = (WorkItemKind.AiRole, WorkItemKind.ProviderOp),
        [typeof(JsonObject)] = (null, null),
        [typeof(JsonNode)] = (null, null),
        [typeof(ActorRef)] = (new ActorRef(ActorType.Human, "ada"), new ActorRef(ActorType.Human, "grace")),
        [typeof(IReadOnlyList<string>)] = (new[] { "wi_01K000000000000000000000AA" }, new[] { "wi_01K000000000000000000000AB" }),
    };

    private static void AssertEveryMemberDecides<T>(Func<T, string> digest)
    {
        var constructor = typeof(T).GetConstructors().Single(c => c.GetParameters().Length > 1);
        var parameters = constructor.GetParameters();

        object? First(ParameterInfo parameter) => Pick(parameter, first: true);
        object? Pick(ParameterInfo parameter, bool first)
        {
            if (parameter.ParameterType == typeof(JsonObject) || parameter.ParameterType == typeof(JsonNode))
            {
                return JsonNode.Parse(first ? """{"value":1}""" : """{"value":2}""");
            }

            Assert.True(
                Values.TryGetValue(parameter.ParameterType, out var pair),
                $"{typeof(T).Name}.{parameter.Name} is a {parameter.ParameterType} this test has no values for: decide whether it is part of what a request asks for.");
            return first ? pair.First : pair.Second;
        }

        var baseline = digest((T)constructor.Invoke([.. parameters.Select(First)]));
        foreach (var parameter in parameters)
        {
            var arguments = parameters.Select(p => p == parameter ? Pick(p, first: false) : First(p)).ToArray();
            var changed = digest((T)constructor.Invoke(arguments));
            var name = char.ToUpperInvariant(parameter.Name![0]) + parameter.Name[1..];
            if (Excluded.Contains(name))
            {
                Assert.True(baseline == changed, $"{typeof(T).Name}.{name} changed the digest, and who asks, why and the key are not what a request asks for.");
            }
            else
            {
                Assert.True(baseline != changed, $"{typeof(T).Name}.{name} did not change the digest: a request asking for a different {name} would be answered as a repeat.");
            }
        }
    }
}
