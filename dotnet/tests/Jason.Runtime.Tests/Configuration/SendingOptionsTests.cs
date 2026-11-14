using System.Text;
using Jason.Contracts.Configuration;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The daily ceiling of automatic sends: one number, how many sends the automatic mode may release from one campaign in a
/// UTC day. It has a floor of one and a ceiling of a thousand, and no zero or unlimited value: a person who wants no
/// automatic sends sets the mode to every message.
/// </summary>
public class SendingOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_default_is_a_hundred_a_day_and_validates()
    {
        var options = new SendingOptions();

        Assert.Equal(100, options.AutomaticMaxPerDay);
        Assert.Equal((1, 1000), (SendingOptions.MinimumPerDay, SendingOptions.MaximumPerDay));
        Assert.True(new SendingOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("0", "Sending:AutomaticMaxPerDay must be between 1 and 1000; got 0.")]
    [InlineData("1001", "Sending:AutomaticMaxPerDay must be between 1 and 1000; got 1001.")]
    [InlineData("-1", "Sending:AutomaticMaxPerDay must be between 1 and 1000; got -1.")]
    [InlineData("null", "Sending:AutomaticMaxPerDay must be between 1 and 1000; got 0.")]
    [InlineData("\"SENTINEL\"", "Sending:AutomaticMaxPerDay could not be read as System.Int32.")]
    [InlineData("\"\"", "Sending:AutomaticMaxPerDay could not be read as System.Int32.")]
    public void A_ceiling_out_of_range_or_of_the_wrong_type_is_refused_by_its_path(string value, string failure)
    {
        using var provider = Provider("""{"Sending":{"AutomaticMaxPerDay":""" + value + "}}");

        var refused = Assert.Throws<OptionsValidationException>(() => Current(provider));

        Assert.Equal([failure], refused.Failures);
        Assert.DoesNotContain("SENTINEL", refused.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void The_bounds_themselves_are_allowed(int value)
    {
        using var provider = Provider("""{"Sending":{"AutomaticMaxPerDay":""" + value + "}}");

        Assert.Equal(value, Current(provider).AutomaticMaxPerDay);
    }

    /// <summary>A start with a ceiling outside its range stops, saying the setting's path and the range.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task A_start_with_a_ceiling_outside_its_range_stops(int value)
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Sending":{"AutomaticMaxPerDay":""" + value + "}}");

        var refused = await Assert.ThrowsAsync<InvalidSettingsException>(async () =>
        {
            var started = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
            await started.DisposeAsync();
        });

        Assert.Equal($"Invalid Jason configuration: Sending:AutomaticMaxPerDay must be between 1 and 1000; got {value}.", refused.Message);
    }

    /// <summary>
    /// The claim reads the ceiling through its seam, so an edit applies live; an edit outside the range is refused by the
    /// same mechanism as any other, and the last good ceiling stands.
    /// </summary>
    [Fact]
    public async Task An_edit_applies_live_and_one_outside_the_range_keeps_the_last_good_ceiling()
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Sending":{"AutomaticMaxPerDay":5}}"""));
        await using (fixture)
        {
            var sending = fixture.Resolve<LiveSettings<SendingOptions>>();
            Assert.Equal(5, sending.Current.AutomaticMaxPerDay);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Sending":{"AutomaticMaxPerDay":6}}""");
            Assert.True(await Dispatch.DispatchHarness.EventuallyAsync(() => sending.Current.AutomaticMaxPerDay == 6, Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Sending":{"AutomaticMaxPerDay":0}}""");
            Assert.True(await TestOptions.RefusedAsync(sending, Ct));

            Assert.Equal(6, sending.Current.AutomaticMaxPerDay);
        }
    }

    /// <summary>The names a refused setting may print as typed: the section's and its one setting's.</summary>
    [Fact]
    public void The_vocabulary_names_the_section_and_its_setting()
    {
        Assert.Contains("Sending", SettingsVocabulary.Names);
        Assert.Contains("AutomaticMaxPerDay", SettingsVocabulary.Names);
    }

    private static SendingOptions Current(ServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<SendingOptions>>().CurrentValue;

    private static ServiceProvider Provider(string settings)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(
            new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build(),
            TestOptions.NothingInstalled());
        return services.BuildServiceProvider();
    }
}
