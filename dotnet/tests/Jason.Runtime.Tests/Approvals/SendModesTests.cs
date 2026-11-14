using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Jason.Runtime.Preferences;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// The one resolution of a send mode: the campaign's own, else the installation's preference, else every message to a
/// person. Each temperature resolves on its own, and the answer names where the mode came from and who set it, read from
/// the rows.
/// </summary>
public class SendModesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ActorRef Marta = new(ActorType.Human, "Marta");

    private static readonly ActorRef Ines = new(ActorType.Human, "Ines");

    private static readonly InstallationModes Nothing = new(null, null);

    [Theory]
    [InlineData(Temperature.Cold)]
    [InlineData(Temperature.Warm)]
    public void With_nothing_set_every_send_waits_for_a_person_and_nobody_set_that(Temperature temperature)
    {
        Assert.Equal(new ModeInForce(SendMode.EveryMessage, SendModeSource.Default, null), SendModes.Resolve(Campaign(), Nothing, temperature));
        Assert.Equal(new ModeInForce(SendMode.EveryMessage, SendModeSource.Default, null), SendModes.Installation(Nothing, temperature));
    }

    /// <summary>One preference set is in force for its own temperature, and the other stays every message, by default.</summary>
    [Fact]
    public void Each_temperature_resolves_on_its_own()
    {
        var installation = new InstallationModes(Row(SendModes.ColdKey, "automatic", Marta), null);

        Assert.Equal(new ModeInForce(SendMode.Automatic, SendModeSource.Preference, Marta), SendModes.Resolve(Campaign(), installation, Temperature.Cold));
        Assert.Equal(new ModeInForce(SendMode.EveryMessage, SendModeSource.Default, null), SendModes.Resolve(Campaign(), installation, Temperature.Warm));
        Assert.Equal([SendModes.WarmKey], SendModes.Unset(installation));
    }

    /// <summary>
    /// The campaign's own outranks the preference, in either direction, and names the person who set it on the campaign;
    /// where the campaign sets none for a temperature, the preference stands there.
    /// </summary>
    [Fact]
    public void A_campaign_s_own_mode_outranks_the_preference_for_its_temperature_alone()
    {
        var installation = new InstallationModes(Row(SendModes.ColdKey, "every_message", Marta), Row(SendModes.WarmKey, "automatic", Marta));
        var campaign = Campaign(cold: (SendMode.Automatic, "Ines"));

        Assert.Equal(new ModeInForce(SendMode.Automatic, SendModeSource.Campaign, Ines), SendModes.Resolve(campaign, installation, Temperature.Cold));
        Assert.Equal(new ModeInForce(SendMode.Automatic, SendModeSource.Preference, Marta), SendModes.Resolve(campaign, installation, Temperature.Warm));

        var stricter = Campaign(warm: (SendMode.EveryMessage, "Ines"));
        Assert.Equal(new ModeInForce(SendMode.EveryMessage, SendModeSource.Campaign, Ines), SendModes.Resolve(stricter, installation, Temperature.Warm));
    }

    /// <summary>A campaign's own automatic releases sends while the installation's question is still open.</summary>
    [Fact]
    public void A_campaign_s_own_automatic_stands_while_the_installation_has_set_nothing()
    {
        var campaign = Campaign(cold: (SendMode.Automatic, "Ines"));

        Assert.Equal(new ModeInForce(SendMode.Automatic, SendModeSource.Campaign, Ines), SendModes.Resolve(campaign, Nothing, Temperature.Cold));
        Assert.Equal([SendModes.ColdKey, SendModes.WarmKey], SendModes.Unset(Nothing));
    }

    /// <summary>A campaign's mode with nobody's name beside it releases nothing in anybody's name: the installation's stands.</summary>
    [Fact]
    public void A_campaign_s_mode_without_its_setter_is_not_in_force()
    {
        var campaign = Campaign();
        campaign.SendModeCold = SendMode.Automatic;

        Assert.Equal(new ModeInForce(SendMode.EveryMessage, SendModeSource.Default, null), SendModes.Resolve(campaign, Nothing, Temperature.Cold));
    }

    /// <summary>A value a later release may write, or one that names no mode, releases nothing: it is every message, from the preference.</summary>
    [Theory]
    [InlineData("""{"mode":"ask_on_doubt"}""")]
    [InlineData("""{"mode":7}""")]
    [InlineData("""{}""")]
    public void A_preference_value_that_names_neither_word_is_every_message(string value)
    {
        var row = Row(SendModes.ColdKey, "automatic", Marta);
        row.Value = JsonNode.Parse(value)!.AsObject();

        Assert.Equal(
            new ModeInForce(SendMode.EveryMessage, SendModeSource.Preference, Marta),
            SendModes.Resolve(null, new InstallationModes(row, null), Temperature.Cold));
    }

    [Fact]
    public void The_modes_in_force_say_the_mode_and_its_source_and_never_who_set_it()
    {
        var installation = new InstallationModes(Row(SendModes.ColdKey, "automatic", Marta), null);

        Assert.Equal(
            new SendModesInForce(new SendModeInForce(SendMode.Automatic, SendModeSource.Preference), new SendModeInForce(SendMode.EveryMessage, SendModeSource.Default)),
            SendModes.InForce(null, installation));
        Assert.Equal(
            new SendModesInForce(new SendModeInForce(SendMode.Automatic, SendModeSource.Preference), new SendModeInForce(SendMode.Automatic, SendModeSource.Campaign)),
            SendModes.InForce(Campaign(warm: (SendMode.Automatic, "Ines")), installation));
    }

    /// <summary>
    /// The read takes both rows as the preference verbs leave them: set, the setter is the row's; unset again, the key is
    /// open once more and the mode is every message by default.
    /// </summary>
    [Fact]
    public async Task The_read_takes_both_rows_as_the_preference_verbs_leave_them()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero));
        var preferences = new PreferenceService(db, new JournalWriter(clock), clock);

        Assert.Equal([SendModes.ColdKey, SendModes.WarmKey], SendModes.Unset(await SendModes.ReadAsync(db, Ct)));

        await preferences.SetAsync(new PreferenceSetRequest(SendModes.WarmKey, new JsonObject { ["mode"] = "automatic" }, Marta, null), Ct);
        var set = await SendModes.ReadAsync(db, Ct);
        Assert.Equal([SendModes.ColdKey], SendModes.Unset(set));
        Assert.Equal(new ModeInForce(SendMode.Automatic, SendModeSource.Preference, Marta), SendModes.Installation(set, Temperature.Warm));

        await preferences.UnsetAsync(new PreferenceUnsetRequest(SendModes.WarmKey, Marta, null), Ct);
        var unset = await SendModes.ReadAsync(db, Ct);
        Assert.Equal([SendModes.ColdKey, SendModes.WarmKey], SendModes.Unset(unset));
        Assert.Equal(new ModeInForce(SendMode.EveryMessage, SendModeSource.Default, null), SendModes.Installation(unset, Temperature.Warm));
    }

    private static Campaign Campaign((SendMode Mode, string By)? cold = null, (SendMode Mode, string By)? warm = null)
    {
        var campaign = WorkItemFactory.NewCampaign("Spring outreach");
        campaign.SendModeCold = cold?.Mode;
        campaign.SendModeColdSetBy = cold?.By;
        campaign.SendModeWarm = warm?.Mode;
        campaign.SendModeWarmSetBy = warm?.By;
        return campaign;
    }

    private static Preference Row(string key, string mode, ActorRef by) => new()
    {
        Key = key,
        Value = new JsonObject { ["mode"] = mode },
        ValueHash = "sha256:" + new string('a', 64),
        UpdatedByType = by.Type,
        UpdatedById = by.Id,
    };
}
