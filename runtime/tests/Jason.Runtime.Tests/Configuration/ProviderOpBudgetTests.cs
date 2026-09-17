using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The lease a provider attempt is claimed under has to outlast the child it will start. The child's own budget
/// is the operation's, exactly — never what happens to be left of the lease — so the two are held apart by
/// checking their relationship where a wrong answer is still cheap: before the runtime is listening.
/// </summary>
public class ProviderOpBudgetTests
{
    private static readonly RuntimeHostOptions Quiet = new(ShippedSettingsDirectory: null, ConsoleLogging: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The slowest published operation, which is what sets the floor without anyone editing a validator.</summary>
    private static OperationContract Slowest => OperationCatalog.All.OrderByDescending(contract => contract.TimeoutMs).First();

    [Fact]
    public async Task A_provider_budget_shorter_than_an_operation_it_must_hold_is_refused_at_start()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(
            dir.Paths.UserSettingsFile,
            """{"Dispatcher":{"ProviderOp":{"TimeoutSeconds":120}},"Plugins":{"Invoker":{"KillGraceMs":5000}}}""");

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => RuntimeHost.StartAsync(dir.Paths, Quiet, Ct));

        // An operator who raised a timeout has to be told which contract forces the number, not only the number.
        Assert.Contains("campaign.enroll", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Dispatcher:ProviderOp:TimeoutSeconds", failure.Message, StringComparison.Ordinal);
        Assert.Contains("305", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped defaults hold the slowest operation this build publishes. This fails the day somebody
    /// publishes a slower one, which is exactly the day a person should decide what the default becomes.
    /// </summary>
    [Fact]
    public void The_defaults_this_runtime_ships_hold_the_slowest_operation_it_publishes()
    {
        var result = Validator(new PluginsOptions()).Validate(null, new DispatcherOptions());

        Assert.True(result.Succeeded, string.Join(" ", result.Failures ?? []));
        Assert.Equal(600, new DispatcherOptions().ProviderOp.TimeoutSeconds);
    }

    /// <summary>
    /// The floor is the operation's own budget plus the grace a child is given to stop, rounded up to whole
    /// seconds: a lease that ends while the runtime is still waiting for a process to die is the same mistake
    /// as one that ends while the process is working.
    /// </summary>
    [Fact]
    public void The_grace_a_child_is_given_to_stop_is_part_of_the_floor()
    {
        var exactly = Slowest.TimeoutMs / 1000;

        Assert.True(Validator(Grace(0)).Validate(null, Budget(exactly)).Succeeded);
        Assert.True(Validator(Grace(1)).Validate(null, Budget(exactly)).Failed);
        Assert.True(Validator(Grace(1)).Validate(null, Budget(exactly + 1)).Succeeded);
    }

    /// <summary>The failure says the whole rule: the setting, the operation that forces it, and the floor.</summary>
    [Fact]
    public void The_refusal_names_the_setting_the_operation_and_the_floor()
    {
        var result = Validator(Grace(5_000)).Validate(null, Budget(120));

        var failure = Assert.Single(result.Failures!);
        Assert.Equal(
            "Dispatcher:ProviderOp:TimeoutSeconds must be at least 305 to hold 'campaign.enroll', which declares 300000 ms, "
                + "and the 5000 ms a child is given to stop; got 120.",
            failure);
    }

    private static ProviderOpBudgetValidator Validator(PluginsOptions plugins) =>
        new(TestOptions.PluginSettings(plugins));

    private static PluginsOptions Grace(int killGraceMs) =>
        new() { Invoker = new PluginInvokerOptions { KillGraceMs = killGraceMs } };

    private static DispatcherOptions Budget(int timeoutSeconds) =>
        new() { ProviderOp = new KindDefaults { TimeoutSeconds = timeoutSeconds, HeartbeatSeconds = 0, MaxAttempts = 3 } };
}
