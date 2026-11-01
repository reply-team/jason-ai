using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Configuration;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The inbound read's three settings: whether the scan reads conversations at all, how often it reads a campaign's,
/// and how far behind the last read the next one starts.
/// </summary>
public class InboundOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_defaults_read_every_fifteen_minutes_four_hours_behind_the_last_read()
    {
        var options = new InboundOptions();

        Assert.True(options.Enabled);
        Assert.Equal(900, options.SyncSeconds);
        Assert.Equal(14_400, options.LookbackSeconds);
        Assert.True(new InboundOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("Inbound:SyncSeconds", 59, "Inbound:SyncSeconds must be between 60 and 86400; got 59.")]
    [InlineData("Inbound:SyncSeconds", 86_401, "Inbound:SyncSeconds must be between 60 and 86400; got 86401.")]
    [InlineData("Inbound:LookbackSeconds", -1, "Inbound:LookbackSeconds must be between 0 and 86400; got -1.")]
    [InlineData("Inbound:LookbackSeconds", 86_401, "Inbound:LookbackSeconds must be between 0 and 86400; got 86401.")]
    public void A_number_out_of_range_is_refused_with_its_rule(string setting, int value, string failure)
    {
        var result = new InboundOptionsValidator().Validate(null, With(setting, value));

        Assert.True(result.Failed);
        Assert.Equal([failure], result.Failures);
    }

    /// <summary>
    /// The bounds themselves are allowed, a look-back of nothing included: it is a choice with a cost the page states —
    /// a reply the provider lists after a later one has been read is not said — not a mistake.
    /// </summary>
    [Theory]
    [InlineData("Inbound:SyncSeconds", 60)]
    [InlineData("Inbound:SyncSeconds", 86_400)]
    [InlineData("Inbound:LookbackSeconds", 0)]
    [InlineData("Inbound:LookbackSeconds", 86_400)]
    public void The_bound_itself_is_allowed(string setting, int value) =>
        Assert.True(new InboundOptionsValidator().Validate(null, With(setting, value)).Succeeded);

    /// <summary>The section is bound and validated with the others: a number out of range stops the start, naming it.</summary>
    [Fact]
    public async Task A_cadence_out_of_range_stops_the_start_naming_the_setting()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Inbound":{"SyncSeconds":10}}""");

        var refused = await Assert.ThrowsAsync<InvalidSettingsException>(async () =>
        {
            var started = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
            await started.DisposeAsync();
        });

        Assert.Equal("Invalid Jason configuration: Inbound:SyncSeconds must be between 60 and 86400; got 10.", refused.Message);
    }

    /// <summary>A switch written as text is refused by its path and the type it has to be, never by what was typed.</summary>
    [Fact]
    public void A_switch_written_as_text_is_refused_by_its_path_and_its_type()
    {
        using var provider = Provider("""{"Inbound":{"Enabled":"SENTINEL_INBOUND_YES"}}""");

        var refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<InboundOptions>>().CurrentValue);

        Assert.Equal(["Inbound:Enabled could not be read as System.Boolean."], refused.Failures);
        Assert.DoesNotContain("SENTINEL", refused.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The scan reads the section through its seam each tick, so an edit applies live; an edit the validator refuses
    /// leaves the last settings that validated in force, and the log names the section and the setting.
    /// </summary>
    [Fact]
    public async Task A_refused_live_edit_keeps_the_last_good_settings()
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Inbound":{"SyncSeconds":1200}}"""));
        await using (fixture)
        {
            var inbound = fixture.Resolve<LiveSettings<InboundOptions>>();
            Assert.Equal(1_200, inbound.Current.SyncSeconds);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Inbound":{"SyncSeconds":30}}""");
            Assert.True(await TestOptions.RefusedAsync(inbound, Ct));

            Assert.Equal(1_200, inbound.Current.SyncSeconds);
            await fixture.Runtime.StopAsync();

            var refused = string.Concat(Directory.GetFiles(fixture.Paths.LogsDirectory).Select(File.ReadAllText))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@l"] == "Error" && (string?)line["Section"] == InboundOptions.Section)
                .Select(line => (string?)line["Failure"])
                .ToList();
            Assert.Equal(["Inbound:SyncSeconds must be between 60 and 86400; got 30."], refused);
        }
    }

    [Fact]
    public void The_vocabulary_names_the_section_and_its_settings()
    {
        Assert.Contains("Inbound", SettingsVocabulary.Names);
        Assert.Contains("SyncSeconds", SettingsVocabulary.Names);
        Assert.Contains("LookbackSeconds", SettingsVocabulary.Names);
    }

    private static InboundOptions With(string setting, int value) => setting switch
    {
        "Inbound:SyncSeconds" => new InboundOptions { SyncSeconds = value },
        _ => new InboundOptions { LookbackSeconds = value },
    };

    private static ServiceProvider Provider(string settings)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(
            new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build(),
            TestOptions.NothingInstalled());
        return services.BuildServiceProvider();
    }
}
