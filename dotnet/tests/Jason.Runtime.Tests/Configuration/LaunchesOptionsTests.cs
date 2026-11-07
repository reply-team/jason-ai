using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Configuration;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The daily limit of launches: one map from a role's name to how many runs one campaign may start in a UTC day. What the
/// file writes joins what the runtime ships, so the responder keeps its limit whatever else is added, a role the map does
/// not name has no limit, and the key 0.30.0 kept this number under stops a start rather than being passed over.
/// </summary>
public class LaunchesOptionsTests
{
    private const string PlantedValue = "731731731";

    private const string Retired = "Responder:MaxLaunchesPerDay was renamed to Launches:MaxPerDay:responder; "
        + "carry the value over by hand and take the old key out.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_default_limits_the_responder_to_twenty_five_and_no_other_role()
    {
        var options = new LaunchesOptions();

        Assert.Equal(new KeyValuePair<string, int>("responder", 25), Assert.Single(options.MaxPerDay));
        Assert.Same(StringComparer.Ordinal, options.MaxPerDay.Comparer);
        Assert.True(Validator().Validate(null, options).Succeeded);
    }

    /// <summary>The binder adds to the map the runtime ships: naming one role keeps the responder's limit.</summary>
    [Fact]
    public void Adding_a_role_keeps_the_responder_s_limit()
    {
        using var provider = Limits("""{"manager":5}""");

        var limits = Current(provider).MaxPerDay;

        Assert.Equal(
            [new KeyValuePair<string, int>("manager", 5), new KeyValuePair<string, int>("responder", 25)],
            limits.OrderBy(limit => limit.Key, StringComparer.Ordinal));
        Assert.Same(StringComparer.Ordinal, limits.Comparer);
    }

    [Fact]
    public void Writing_the_responder_replaces_its_number()
    {
        using var provider = Limits("""{"responder":3}""");

        Assert.Equal(3, Current(provider).MaxPerDay["responder"]);
    }

    [Theory]
    [InlineData("""{"responder":0}""", "Launches:MaxPerDay:responder must be between 1 and 1000; got 0.")]
    [InlineData("""{"responder":1001}""", "Launches:MaxPerDay:responder must be between 1 and 1000; got 1001.")]
    [InlineData("""{"deliverability-specialist":-1}""", "Launches:MaxPerDay:deliverability-specialist must be between 1 and 1000; got -1.")]
    [InlineData("""{"a-role-of-their-own":1001}""", "Launches:MaxPerDay:<key> must be between 1 and 1000; got 1001.")]
    [InlineData("""{"responder":null}""", "Launches:MaxPerDay:responder must be between 1 and 1000; got 0.")]
    [InlineData("""{"manager":null}""", "Launches:MaxPerDay:manager must be between 1 and 1000; got 0.")]
    public void A_limit_out_of_range_is_refused_by_its_path(string map, string failure)
    {
        using var provider = Limits(map);

        var refused = Assert.Throws<OptionsValidationException>(() => Current(provider));

        Assert.Equal([failure], refused.Failures);
    }

    [Theory]
    [InlineData("""{"responder":1}""")]
    [InlineData("""{"responder":1000}""")]
    [InlineData("""{"a":1000}""")]
    public void The_bound_itself_is_allowed(string map)
    {
        using var provider = Limits(map);

        Assert.NotEmpty(Current(provider).MaxPerDay);
    }

    /// <summary>
    /// A key is a role's name or it is refused: what is written there may be a secret, so the sentence gives its length
    /// and never its text, and a name of the code's own is shown as it was typed.
    /// </summary>
    [Theory]
    [InlineData("SENTINEL_ROLE", "<key>", "13 characters")]
    [InlineData("Responder", "Responder", "9 characters")]
    [InlineData("1st", "<key>", "3 characters")]
    public void A_key_that_is_no_role_s_name_is_refused_and_never_said_back(string key, string shown, string length)
    {
        using var provider = Limits("{\"" + key + "\":5}");

        var refused = Assert.Throws<OptionsValidationException>(() => Current(provider));

        Assert.Equal(
            [$"Launches:MaxPerDay:{shown} must be a role's name, lowercase letters, digits and hyphens starting with a letter, at most 64 characters; the key written is {length} long and does not have that shape."],
            refused.Failures);
        Assert.DoesNotContain("SENTINEL", refused.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The longest name a role may have is a key that passes, and one character more is not.</summary>
    [Fact]
    public void A_key_of_sixty_four_characters_passes_and_one_of_sixty_five_does_not()
    {
        var longest = "a" + new string('b', 63);

        Assert.True(Validator().Validate(null, Map(longest, 5)).Succeeded);
        Assert.True(Validator().Validate(null, Map(longest + "b", 5)).Failed);
    }

    /// <summary>
    /// A number written as text or an object is refused by its path and the type, whether or not the map already holds the
    /// key: the responder's default must not stand in for a limit the file got wrong.
    /// </summary>
    [Theory]
    [InlineData("""{"responder":"SENTINEL_LIMIT"}""", "Launches:MaxPerDay:responder could not be read as System.Int32.")]
    [InlineData("""{"responder":""}""", "Launches:MaxPerDay:responder could not be read as System.Int32.")]
    [InlineData("""{"responder":{"a":1}}""", "Launches:MaxPerDay:responder could not be read as System.Int32.")]
    [InlineData("""{"manager":"SENTINEL_LIMIT"}""", "Launches:MaxPerDay:manager could not be read as System.Int32.")]
    [InlineData("""{"manager":""}""", "Launches:MaxPerDay:manager could not be read as System.Int32.")]
    public void A_limit_of_the_wrong_type_is_refused_by_its_path_and_its_type(string map, string failure)
    {
        using var provider = Limits(map);

        var refused = Assert.Throws<OptionsValidationException>(() => Current(provider));

        Assert.Equal([failure], refused.Failures);
        Assert.DoesNotContain("SENTINEL", refused.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A person who lowered the limit under the old key must not return to the default without knowing: a file that still
    /// names it stops the start, saying where the number now lives, and says nothing of the number.
    /// </summary>
    [Fact]
    public async Task A_file_naming_the_retired_key_stops_the_start_and_says_where_it_went()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Responder":{"MaxLaunchesPerDay":731}}""");

        var refused = await Assert.ThrowsAsync<InvalidSettingsException>(async () =>
        {
            var started = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
            await started.DisposeAsync();
        });

        Assert.Equal($"Invalid Jason configuration: {Retired}", refused.Message);
    }

    /// <summary>The key is found by its path in the configuration, so any spelling of it and any value of it is the same refusal.</summary>
    [Theory]
    [InlineData("""{"Responder":{"MaxLaunchesPerDay":731}}""")]
    [InlineData("""{"responder":{"maxlaunchesperday":731}}""")]
    [InlineData("""{"Responder":{"MaxLaunchesPerDay":"SENTINEL_OLD"}}""")]
    [InlineData("""{"Responder":{"MaxLaunchesPerDay":null}}""")]
    [InlineData("""{"Responder":{"MaxLaunchesPerDay":{}}}""")]
    [InlineData("""{"Responder":{"MaxLaunchesPerDay":5},"Launches":{"MaxPerDay":{"responder":5}}}""")]
    public void The_retired_key_is_refused_whatever_is_written_there(string settings)
    {
        using var provider = Provider(settings);

        var refused = Assert.Throws<OptionsValidationException>(() => Current(provider));

        Assert.Equal([Retired], refused.Failures);
        Assert.DoesNotContain("SENTINEL", refused.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The check reads the configuration, not the file, so the variable that sets the key is met the same way. Any other
    /// layer proves the same read; a real variable would reach every runtime another test starts in this process.
    /// </summary>
    [Fact]
    public void The_retired_key_is_refused_from_any_layer_of_the_configuration()
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(
            new ConfigurationBuilder().AddInMemoryCollection([new("Responder:MaxLaunchesPerDay", "731")]).Build(),
            TestOptions.NothingInstalled());
        using var provider = services.BuildServiceProvider();

        var refused = Assert.Throws<OptionsValidationException>(() => Current(provider));

        Assert.Equal([Retired], refused.Failures);
    }

    /// <summary>The other settings of the responder's section are no reason to refuse anything.</summary>
    [Fact]
    public void The_section_it_stood_in_is_not_the_retired_key()
    {
        using var provider = Provider("""{"Responder":{"Priority":70}}""");

        Assert.Equal(25, Current(provider).MaxPerDay["responder"]);
    }

    /// <summary>
    /// The scan reads the section through its seam, so an edit applies live; an edit that names the retired key is refused
    /// by the same mechanism as any other, the last good limits stand, and the log says the path and not the number.
    /// </summary>
    [Fact]
    public async Task An_edit_applies_live_and_one_naming_the_retired_key_keeps_the_last_good_limits()
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Launches":{"MaxPerDay":{"manager":5}}}"""));
        await using (fixture)
        {
            var launches = fixture.Resolve<LiveSettings<LaunchesOptions>>();
            Assert.Equal(5, launches.Current.MaxPerDay["manager"]);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Launches":{"MaxPerDay":{"manager":6}}}""");
            Assert.True(await Dispatch.DispatchHarness.EventuallyAsync(() => launches.Current.MaxPerDay["manager"] == 6, Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Launches":{"MaxPerDay":{"manager":7}},"Responder":{"MaxLaunchesPerDay":731731731}}""");
            Assert.True(await TestOptions.RefusedAsync(launches, Ct));

            Assert.Equal(6, launches.Current.MaxPerDay["manager"]);
            await fixture.Runtime.StopAsync();

            var log = string.Concat(Directory.GetFiles(fixture.Paths.LogsDirectory).Select(File.ReadAllText));
            var lines = log
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .ToList();
            var refused = lines
                .Where(line => (string?)line["@l"] == "Error" && (string?)line["Section"] == LaunchesOptions.Section)
                .Select(line => (string?)line["Failure"])
                .ToList();
            Assert.Equal([Retired], refused);

            // The planted value is nine digits, a run of three repeated: no time fraction, process id, port, instance
            // id or event id the log writes carries it by chance, so its presence in any field of any raw line, "@t"
            // included, means the setting's value was written. A short value such as 731 is carried by a time stamp now and then.
            foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.False(
                    line.Contains(PlantedValue, StringComparison.Ordinal),
                    $"The retired setting's value reached the log: {line}");
            }
        }
    }

    /// <summary>The names a refused setting may print as typed: the section's, and each role Jason ships.</summary>
    [Fact]
    public void The_vocabulary_names_the_section_its_setting_and_every_shipped_role()
    {
        Assert.Contains("Launches", SettingsVocabulary.Names);
        Assert.Contains("MaxPerDay", SettingsVocabulary.Names);
        Assert.All(ShippedRoles.Names, role => Assert.Contains(role, SettingsVocabulary.Names));
        Assert.DoesNotContain("a-role-of-their-own", SettingsVocabulary.Names);
    }

    [Fact]
    public void The_old_key_is_no_longer_a_setting_the_runtime_knows()
    {
        Assert.DoesNotContain("MaxLaunchesPerDay", SettingsVocabulary.Names);
        Assert.Null(typeof(ResponderOptions).GetProperty("MaxLaunchesPerDay"));
    }

    private static LaunchesOptions Current(ServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<LaunchesOptions>>().CurrentValue;

    private static LaunchesOptions Map(string role, int limit)
    {
        var options = new LaunchesOptions();
        options.MaxPerDay[role] = limit;
        return options;
    }

    private static LaunchesOptionsValidator Validator() => new(new ConfigurationBuilder().Build());

    private static ServiceProvider Limits(string map) => Provider("""{"Launches":{"MaxPerDay":""" + map + "}}");

    private static ServiceProvider Provider(string settings)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(
            new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build(),
            TestOptions.NothingInstalled());
        return services.BuildServiceProvider();
    }
}
