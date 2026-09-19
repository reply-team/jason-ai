using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The ceiling a package that declares no limit of its own runs under. It can only lower an operation's budget,
/// never raise it, so a ceiling below the slowest published operation means that operation is killed partway
/// through and answered as ambiguous — for a reason nobody looking at the attempt could see. The relationship is
/// checked where it is still cheap to be wrong about: before the runtime is listening.
/// </summary>
public class PluginCeilingTests
{

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The slowest published operation, which is what sets the floor without anyone editing a validator.</summary>
    private static OperationContract Slowest => OperationCatalog.Slowest!;

    [Fact]
    public async Task A_ceiling_below_an_operation_this_build_publishes_is_refused_at_start()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Plugins":{"Limits":{"TimeoutMs":60000}}}""");

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        // An operator who lowered a ceiling has to be told which contract forces the number, not only the number.
        Assert.Contains("Plugins:Limits:TimeoutMs", failure.Message, StringComparison.Ordinal);
        Assert.Contains("campaign.enroll", failure.Message, StringComparison.Ordinal);
        Assert.Contains("300000", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped defaults can run every operation this build publishes. This fails the day somebody publishes
    /// a slower one, which is exactly the day a person should decide what the default becomes — rather than the
    /// day an operator finds a killed child and no reason for it.
    /// </summary>
    [Fact]
    public void The_defaults_this_runtime_ships_hold_the_slowest_operation_it_publishes()
    {
        var result = new PluginsOptionsValidator().Validate(null, new PluginsOptions());

        Assert.True(result.Succeeded, string.Join(" ", result.Failures ?? []));
        Assert.Equal(Slowest.TimeoutMs, new PluginLimitsOptions().TimeoutMs);
    }

    /// <summary>
    /// The floor is the operation's own budget and nothing more: the ceiling caps what a child is given, and a
    /// child given exactly what its contract asks for is not capped at all.
    /// </summary>
    [Fact]
    public void The_floor_is_the_slowest_operations_own_budget()
    {
        Assert.True(Validate(Slowest.TimeoutMs).Succeeded);
        Assert.True(Validate(Slowest.TimeoutMs - 1).Failed);
    }

    /// <summary>The failure says the whole rule: the setting, the floor, and the operation that forces it.</summary>
    [Fact]
    public void The_refusal_names_the_setting_the_floor_and_the_operation()
    {
        var result = Validate(60_000);

        Assert.Contains(
            "Plugins:Limits:TimeoutMs must be at least 300000 to hold 'campaign.enroll', the slowest operation "
                + "this build publishes, for a package that declares no limit of its own; got 60000.",
            result.Failures!);
    }

    private static ValidateOptionsResult Validate(int timeoutMs) =>
        new PluginsOptionsValidator().Validate(null, new PluginsOptions
        {
            Limits = new PluginLimitsOptions { TimeoutMs = timeoutMs },
        });
}
