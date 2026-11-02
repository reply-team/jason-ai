using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Configuration;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The respond step's five settings: whether a reply wakes the responder at all, where the responder sits in the queue,
/// what one run may cost, and how old a reply may be and still wake one.
/// </summary>
public class ResponderOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_defaults_wake_the_responder_once_ahead_of_ordinary_work_for_replies_up_to_three_days_old()
    {
        var options = new ResponderOptions();

        Assert.True(options.OnReply);
        Assert.Equal(50, options.Priority);
        Assert.Equal(900, options.TimeoutSeconds);
        Assert.Equal(1, options.MaxAttempts);
        Assert.Equal(259_200, options.MaxAgeSeconds);
        Assert.True(new ResponderOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("Responder:Priority", -1_001, "Responder:Priority must be between -1000 and 1000; got -1001.")]
    [InlineData("Responder:Priority", 1_001, "Responder:Priority must be between -1000 and 1000; got 1001.")]
    [InlineData("Responder:TimeoutSeconds", 29, "Responder:TimeoutSeconds must be between 30 and 86400; got 29.")]
    [InlineData("Responder:TimeoutSeconds", 86_401, "Responder:TimeoutSeconds must be between 30 and 86400; got 86401.")]
    [InlineData("Responder:MaxAttempts", 0, "Responder:MaxAttempts must be between 1 and 10; got 0.")]
    [InlineData("Responder:MaxAttempts", 11, "Responder:MaxAttempts must be between 1 and 10; got 11.")]
    [InlineData("Responder:MaxAgeSeconds", 3_599, "Responder:MaxAgeSeconds must be between 3600 and 2592000; got 3599.")]
    [InlineData("Responder:MaxAgeSeconds", 2_592_001, "Responder:MaxAgeSeconds must be between 3600 and 2592000; got 2592001.")]
    public void A_number_out_of_range_is_refused_with_its_rule(string setting, int value, string failure)
    {
        var result = new ResponderOptionsValidator().Validate(null, With(setting, value));

        Assert.True(result.Failed);
        Assert.Equal([failure], result.Failures);
    }

    [Theory]
    [InlineData("Responder:Priority", -1_000)]
    [InlineData("Responder:Priority", 1_000)]
    [InlineData("Responder:TimeoutSeconds", 30)]
    [InlineData("Responder:TimeoutSeconds", 86_400)]
    [InlineData("Responder:MaxAttempts", 1)]
    [InlineData("Responder:MaxAttempts", 10)]
    [InlineData("Responder:MaxAgeSeconds", 3_600)]
    [InlineData("Responder:MaxAgeSeconds", 2_592_000)]
    public void The_bound_itself_is_allowed(string setting, int value) =>
        Assert.True(new ResponderOptionsValidator().Validate(null, With(setting, value)).Succeeded);

    /// <summary>The section is bound and validated with the others: a number out of range stops the start, naming it.</summary>
    [Fact]
    public async Task A_number_out_of_range_stops_the_start_naming_the_setting()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Responder":{"MaxAttempts":0}}""");

        var refused = await Assert.ThrowsAsync<InvalidSettingsException>(async () =>
        {
            var started = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
            await started.DisposeAsync();
        });

        Assert.Equal("Invalid Jason configuration: Responder:MaxAttempts must be between 1 and 10; got 0.", refused.Message);
    }

    /// <summary>A switch written as text is refused by its path and the type it has to be, never by what was typed.</summary>
    [Fact]
    public void A_switch_written_as_text_is_refused_by_its_path_and_its_type()
    {
        using var provider = Provider("""{"Responder":{"OnReply":"SENTINEL_RESPONDER_YES"}}""");

        var refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<ResponderOptions>>().CurrentValue);

        Assert.Equal(["Responder:OnReply could not be read as System.Boolean."], refused.Failures);
        Assert.DoesNotContain("SENTINEL", refused.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The scan reads the section through its seam each tick, so an edit applies live; an edit the validator refuses
    /// leaves the last settings that validated in force, and the log names the section and the setting.
    /// </summary>
    [Fact]
    public async Task An_edit_applies_live_and_a_refused_one_keeps_the_last_good_settings()
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Responder":{"Priority":70}}"""));
        await using (fixture)
        {
            var responder = fixture.Resolve<LiveSettings<ResponderOptions>>();
            Assert.Equal(70, responder.Current.Priority);
            Assert.True(responder.Current.OnReply);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Responder":{"Priority":70,"OnReply":false}}""");
            Assert.True(await Dispatch.DispatchHarness.EventuallyAsync(() => !responder.Current.OnReply, Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Responder":{"Priority":5000,"OnReply":false}}""");
            Assert.True(await TestOptions.RefusedAsync(responder, Ct));

            Assert.Equal(70, responder.Current.Priority);
            Assert.False(responder.Current.OnReply);
            await fixture.Runtime.StopAsync();

            var refused = string.Concat(Directory.GetFiles(fixture.Paths.LogsDirectory).Select(File.ReadAllText))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@l"] == "Error" && (string?)line["Section"] == ResponderOptions.Section)
                .Select(line => (string?)line["Failure"])
                .ToList();
            Assert.Equal(["Responder:Priority must be between -1000 and 1000; got 5000."], refused);
        }
    }

    /// <summary>
    /// The names a refused setting may print as typed. The doctor's allowlist is held to the option classes by
    /// <c>SettingsAllowlistAgreementTests</c>, which walks this section with the others.
    /// </summary>
    [Fact]
    public void The_vocabulary_names_the_section_and_its_settings()
    {
        Assert.Contains("Responder", SettingsVocabulary.Names);
        foreach (var name in new[] { "OnReply", "Priority", "TimeoutSeconds", "MaxAttempts", "MaxAgeSeconds" })
        {
            Assert.Contains(name, SettingsVocabulary.Names);
        }
    }

    private static ResponderOptions With(string setting, int value) => setting switch
    {
        "Responder:Priority" => new ResponderOptions { Priority = value },
        "Responder:TimeoutSeconds" => new ResponderOptions { TimeoutSeconds = value },
        "Responder:MaxAttempts" => new ResponderOptions { MaxAttempts = value },
        _ => new ResponderOptions { MaxAgeSeconds = value },
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
