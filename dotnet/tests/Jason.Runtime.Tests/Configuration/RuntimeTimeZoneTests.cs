using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Configuration;
using Jason.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The installation's time zone, <c>Runtime:TimeZone</c>: an IANA id the day of the daily limits is counted in, absent by
/// default, and absent means the system's own zone. An id this system does not know is refused at start, before the host
/// exists and through the options on start, and refused on a live edit, which keeps the last good settings. The text is
/// described, never said back.
/// </summary>
public class RuntimeTimeZoneTests
{
    /// <summary>Nine digits, a run of three repeated, as the other settings' tests plant: no field of a log line carries it by chance.</summary>
    private const string Planted = "Zone/731731731";

    private const string Refusal = "Runtime:TimeZone must be an IANA time zone id such as Asia/Tokyo, or left out for the system's own zone; "
        + "the value written is 14 characters long and is not one this system knows.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_zone_is_absent_by_default_and_the_default_validates()
    {
        var options = new RuntimeOptions();

        Assert.Null(options.TimeZone);
        Assert.True(new RuntimeOptionsValidator().Validate(null, options).Succeeded);
    }

    /// <summary>
    /// An IANA id the system resolves is taken, UTC under both of its names among them, and so is nothing written there,
    /// which is the system's own zone.
    /// </summary>
    [Theory]
    [InlineData("Asia/Tokyo")]
    [InlineData("Europe/Berlin")]
    [InlineData("UTC")]
    [InlineData("Etc/UTC")]
    [InlineData(" Asia/Tokyo ")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_known_iana_id_or_nothing_is_taken(string zone)
    {
        Assert.True(new RuntimeOptionsValidator().Validate(null, new RuntimeOptions { TimeZone = zone }).Succeeded);
    }

    /// <summary>An id no system knows is refused, and so is a Windows id, a vocabulary of one system's own.</summary>
    [Theory]
    [InlineData(Planted, Refusal)]
    [InlineData("Tokyo Standard Time", "Runtime:TimeZone must be an IANA time zone id such as Asia/Tokyo, or left out for the system's own zone; "
        + "the value written is 19 characters long and is not one this system knows.")]
    public void An_unknown_or_a_windows_id_is_refused_by_its_length(string zone, string expected)
    {
        var result = new RuntimeOptionsValidator().Validate(null, new RuntimeOptions { TimeZone = zone });

        Assert.Equal([expected], result.Failures);
    }

    /// <summary>At start, through the options the host validates on start: the setting's path, and never what is written there.</summary>
    [Fact]
    public void An_unknown_id_stops_the_options_on_start()
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(
            new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(Settings(Planted)))).Build(),
            TestOptions.NothingInstalled());
        using var provider = services.BuildServiceProvider();

        var refused = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptionsMonitor<RuntimeOptions>>().CurrentValue);

        Assert.Equal([Refusal], refused.Failures);
    }

    /// <summary>And before the host exists: the pre-host load reads the same section through the same validator.</summary>
    [Fact]
    public void An_unknown_id_stops_the_pre_host_load()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, Settings(Planted));

        using var configuration = JasonConfiguration.Build(dir.Paths, null);
        var error = Assert.Throws<InvalidSettingsException>(() => JasonConfiguration.Load(configuration));

        Assert.EndsWith(Refusal, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("731731731", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scan reads the zone through its seam, so an edit applies live; an edit to an id the system does not know is
    /// refused, the last good zone stands, and the log says the path and not the id.
    /// </summary>
    [Fact]
    public async Task An_edit_applies_live_and_an_unknown_id_keeps_the_last_good_zone()
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Runtime":{"TimeZone":"Asia/Tokyo"}}"""));
        await using (fixture)
        {
            var runtime = fixture.Resolve<LiveSettings<RuntimeOptions>>();
            Assert.Equal("Asia/Tokyo", runtime.Current.TimeZone);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Runtime":{"TimeZone":"Europe/Berlin"}}""");
            Assert.True(await Dispatch.DispatchHarness.EventuallyAsync(() => runtime.Current.TimeZone == "Europe/Berlin", Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},""" + Settings(Planted)[1..]);
            Assert.True(await TestOptions.RefusedAsync(runtime, Ct));

            Assert.Equal("Europe/Berlin", runtime.Current.TimeZone);
            Assert.Equal("Europe/Berlin", fixture.Resolve<InstallationDay>().Of(DateTime.UtcNow).TimeZone);
            await fixture.Runtime.StopAsync();

            var log = string.Concat(Directory.GetFiles(fixture.Paths.LogsDirectory).Select(File.ReadAllText));
            var refused = log
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@l"] == "Error" && (string?)line["Section"] == RuntimeOptions.Section)
                .Select(line => (string?)line["Failure"])
                .ToList();
            Assert.Equal([Refusal], refused);
            Assert.DoesNotContain("731731731", log, StringComparison.Ordinal);
        }
    }

    /// <summary>A settings file that names <paramref name="zone"/> and nothing else.</summary>
    private static string Settings(string zone) => """{"Runtime":{"TimeZone":""" + "\"" + zone + "\"}}";

    /// <summary>The names a refused setting may print as typed: the section's and the setting's own.</summary>
    [Fact]
    public void The_vocabulary_names_the_setting()
    {
        Assert.Contains("Runtime", SettingsVocabulary.Names);
        Assert.Contains("TimeZone", SettingsVocabulary.Names);
    }
}
