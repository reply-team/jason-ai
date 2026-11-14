using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Preferences;

/// <summary>
/// The three preference operations over the API, each exchange recorded under <c>docs/api/fixtures/</c> for the consumers
/// that render them, and the one promise that no request can show: a value never reaches the runtime's log.
/// </summary>
public class PreferenceEndpointTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly object Marta = new { type = "human", id = "Marta" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preferences_are_listed_set_refused_and_unset_over_the_api()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));

        var none = await api.RecordAsync<Page<PreferenceDto>>(Operations.PreferenceList, "none", new { }, Ct);
        Assert.Equal(["agent.interactive_host", "send_mode.cold", "send_mode.warm", "sender.identity", "tone_of_voice"], none.Items.Select(item => item.Key));
        Assert.All(none.Items, item => Assert.True(item.Value is null && item.UpdatedAt is null, $"{item.Key} is set on a new installation: {item}"));

        var sender = new
        {
            senders = new object[]
            {
                new { name = "Marta Lind", addressed_as = "Marta", gender_forms = "feminine", title = "Head of Partnerships", company = "Example Analytics" },
            },
        };
        var set = await api.RecordAsync<PreferenceDto>(Operations.PreferenceSet, "sender", new { key = "sender.identity", value = sender, actor = Marta, reason = "the person's answer" }, Ct);
        Assert.Equal(("sender.identity", "Marta"), (set.Key, set.UpdatedBy!.Id));
        Assert.Equal("feminine", (string?)set.Value!["senders"]![0]!["gender_forms"]);

        var tone = await api.RecordAsync<PreferenceDto>(
            Operations.PreferenceSet, "tone", new { key = "tone_of_voice", value = new { summary = "Plain and warm; no exclamation marks.", formality = "informal" }, actor = Marta }, Ct);
        Assert.Equal("informal", (string?)tone.Value!["formality"]);

        var cold = await api.RecordAsync<PreferenceDto>(
            Operations.PreferenceSet, "send-mode-cold", new { key = "send_mode.cold", value = new { mode = "automatic" }, actor = Marta, reason = "the person's answer" }, Ct);
        Assert.Equal(("send_mode.cold", "automatic"), (cold.Key, (string?)cold.Value!["mode"]));

        var unknown = await api.RecordErrorAsync(
            Operations.PreferenceSet, "unknown-key", new { key = "tone", value = new { summary = "Plain." }, actor = Marta }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal(["key/unknown"], unknown.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));

        var invalid = await api.RecordErrorAsync(
            Operations.PreferenceSet, "invalid-value", new { key = "sender.identity", value = new { senders = new object[] { new { name = "Marta Lind", gender_forms = "female" } } }, actor = Marta }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal(["value/senders/0/gender_forms/invalid"], invalid.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));

        var role = await api.RecordErrorAsync(
            Operations.PreferenceSet, "not-human", new { key = "tone_of_voice", value = new { summary = "Loud." }, actor = new { type = "role", id = "copywriter" } }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal("preference_not_human", role.Code);

        var anonymous = await api.RecordErrorAsync(
            Operations.PreferenceSet, "no-actor", new { key = "tone_of_voice", value = new { summary = "Plain." } }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal("actor_required", anonymous.Code);

        var both = await api.RecordAsync<Page<PreferenceDto>>(Operations.PreferenceList, "set", new { }, Ct);
        Assert.Equal([null, cold.ValueHash, null, set.ValueHash, tone.ValueHash], both.Items.Select(item => item.ValueHash));

        var warm = await api.RecordAsync<PreferenceDto>(
            Operations.PreferenceSet, "send-mode-warm", new { key = "send_mode.warm", value = new { mode = "every_message" }, actor = Marta, reason = "the person's answer" }, Ct);
        Assert.Equal(("send_mode.warm", "every_message"), (warm.Key, (string?)warm.Value!["mode"]));

        var unset = await api.RecordAsync<PreferenceDto>(Operations.PreferenceUnset, "removed", new { key = "sender.identity", actor = Marta, reason = "taken back" }, Ct);
        Assert.Equal(new PreferenceDto("sender.identity", null, null, null, null, null, null), unset);
    }

    /// <summary>
    /// The send modes as the page's Settings section reads and changes them while the name and tone are unset: one kind
    /// going automatically, both kinds answered "every message", and an answer given back, which leaves the mode unset.
    /// </summary>
    [Fact]
    public async Task The_send_modes_are_listed_turned_off_and_given_back_over_the_api()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        const string answer = "the person's answer";

        await api.PostOkAsync<PreferenceDto>(Operations.PreferenceSet, new { key = "send_mode.cold", value = new { mode = "automatic" }, actor = Marta, reason = answer }, Ct);
        var automatic = await api.RecordAsync<Page<PreferenceDto>>(Operations.PreferenceList, "mode-automatic-voice-unset", new { }, Ct);
        Assert.Equal([null, "automatic", null, null, null], automatic.Items.Select(item => (string?)item.Value?["mode"]));

        var off = await api.RecordAsync<PreferenceDto>(
            Operations.PreferenceSet, "send-mode-cold-every-message", new { key = "send_mode.cold", value = new { mode = "every_message" }, actor = Marta, reason = answer }, Ct);
        Assert.Equal(("send_mode.cold", "every_message"), (off.Key, (string?)off.Value!["mode"]));

        await api.PostOkAsync<PreferenceDto>(Operations.PreferenceSet, new { key = "send_mode.warm", value = new { mode = "every_message" }, actor = Marta, reason = answer }, Ct);
        var waits = await api.RecordAsync<Page<PreferenceDto>>(Operations.PreferenceList, "modes-every-message", new { }, Ct);
        Assert.Equal([null, "every_message", "every_message", null, null], waits.Items.Select(item => (string?)item.Value?["mode"]));

        var back = await api.RecordAsync<PreferenceDto>(Operations.PreferenceUnset, "send-mode-cold", new { key = "send_mode.cold", actor = Marta, reason = answer }, Ct);
        Assert.Equal(new PreferenceDto("send_mode.cold", null, null, null, null, null, null), back);
    }

    /// <summary>
    /// A request line names the operation and never the body, and no template the runtime writes names a value; this reads
    /// the log a real start wrote back, after a value was set and unset, and finds none of it.
    /// </summary>
    [Fact]
    public async Task A_preference_s_value_never_reaches_the_log()
    {
        const string Name = "Quillfeather-Ostrowska";
        const string Detail = "signs every message with a sketch of a heron";
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var value = new JsonObject
        {
            ["senders"] = new JsonArray(new JsonObject { ["name"] = Name, ["gender_forms"] = "neutral", ["details"] = Detail }),
        };

        await api.PostOkAsync<PreferenceDto>(Operations.PreferenceSet, new { key = "sender.identity", value, actor = Marta }, Ct);
        await api.PostOkAsync<Page<PreferenceDto>>(Operations.PreferenceList, new { }, Ct);
        await api.PostErrorAsync(Operations.PreferenceSet, new { key = "sender.identity", value = new { senders = new object[] { new { name = Name } } }, actor = Marta }, HttpStatusCode.BadRequest, Ct);
        await api.PostOkAsync<PreferenceDto>(Operations.PreferenceUnset, new { key = "sender.identity", actor = Marta }, Ct);

        // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
        await api.Runtime.StopAsync();
        var log = string.Join('\n', Directory.GetFiles(api.Paths.LogsDirectory, "*", SearchOption.AllDirectories).SelectMany(File.ReadAllLines));

        Assert.Contains(Operations.Route(Operations.PreferenceSet), log, StringComparison.Ordinal);
        Assert.DoesNotContain(Name, log, StringComparison.Ordinal);
        Assert.DoesNotContain(Detail, log, StringComparison.Ordinal);
    }
}
