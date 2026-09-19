using Jason.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Jason.Runtime.Hosting;
using Jason.Runtime.Journal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The loop's numbers, and the one list in them that decides whether anything happens at all.
/// </summary>
public class ManagerOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The guard that matters most, because what it prevents is silence. A kind the runtime never writes can
    /// never fire, so an installation configured that way believes it is being managed and is not — and a kind
    /// a caller may append through <c>journal.append</c> is worse than useless: anything that can write a line
    /// could summon a manager by naming it.
    /// </summary>
    [Fact]
    public void A_trigger_the_runtime_never_writes_refuses_the_start()
    {
        var failure = Validate(options => options.Triggers = ["inbound_reply"]);

        Assert.Contains("Manager:Triggers[0]", failure, StringComparison.Ordinal);
        Assert.Contains("inbound_reply", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the same for a kind a role writes itself. <c>plan_revision</c> is the sort of line a skill is told to
    /// append; that it reads like an event is exactly why it must not be one.
    /// </summary>
    [Fact]
    public void A_trigger_a_role_could_write_itself_refuses_the_start()
    {
        var failure = Validate(options => options.Triggers = ["plan_revision"]);

        Assert.Contains("the runtime writes itself", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trigger_listed_twice_refuses_the_start()
    {
        var failure = Validate(options => options.Triggers = [JournalKinds.WorkItemFailed, JournalKinds.WorkItemFailed]);

        Assert.Contains("repeats", failure, StringComparison.Ordinal);
    }

    /// <summary>Cadence and nothing else is a configuration somebody may want, not a mistake.</summary>
    [Fact]
    public void An_empty_trigger_list_is_allowed()
    {
        var result = new ManagerOptionsValidator().Validate(null, new ManagerOptions { Triggers = [] });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void The_defaults_are_the_four_kinds_this_version_reacts_to()
    {
        var options = new ManagerOptions();

        Assert.Equal(
            [
                JournalKinds.WorkItemFailed,
                JournalKinds.ApprovalRejected,
                JournalKinds.ExternalEffectReported,
                JournalKinds.DecisionAnswered,
            ],
            options.Triggers);
        Assert.Equal(18_000, options.ReviewSeconds);
        Assert.Equal(900, options.TimeoutSeconds);
        Assert.Equal(1, options.MaxAttempts);
        Assert.Equal(0, options.Priority);
        Assert.Equal(500, options.MaxEntriesPerScan);

        // And every one of them is still a kind the runtime writes itself, which is what the validator is for.
        Assert.All(options.Triggers, kind => Assert.Contains(kind, JournalKinds.Reserved));
    }

    /// <summary>
    /// The one kind whose absence breaks something rather than narrowing the loop. A question a person has
    /// answered releases the review that continues the work, and the role that asked has already ended — so an
    /// installation that narrows this list and drops this kind has questions answered into silence.
    /// </summary>
    [Fact]
    public void A_narrowed_list_that_drops_an_answered_decision_is_a_loop_that_never_continues()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Manager":{"Triggers":["workitem_failed"]}}""");

        var options = new ManagerOptions();
        ManagerOptions.Fill(JasonConfiguration.Build(dir.Paths, shippedSettingsDirectory: null).GetSection(ManagerOptions.Section), options);

        // Nothing refuses it — it is a legitimate configuration — so this is the test that records the cost,
        // and the contract page is where somebody is told before they type it.
        Assert.Equal([JournalKinds.WorkItemFailed], options.Triggers);
        Assert.DoesNotContain(JournalKinds.DecisionAnswered, options.Triggers);
    }

    [Theory]
    [InlineData("Manager:ReviewSeconds", 299)]
    [InlineData("Manager:ReviewSeconds", 604_801)]
    [InlineData("Manager:TimeoutSeconds", 29)]
    [InlineData("Manager:TimeoutSeconds", 86_401)]
    [InlineData("Manager:MaxAttempts", 0)]
    [InlineData("Manager:MaxAttempts", 11)]
    [InlineData("Manager:Priority", -1_001)]
    [InlineData("Manager:Priority", 1_001)]
    [InlineData("Manager:MaxEntriesPerScan", 49)]
    [InlineData("Manager:MaxEntriesPerScan", 10_001)]
    public void A_number_out_of_range_refuses_the_start(string setting, int value)
    {
        var failure = Validate(options =>
        {
            switch (setting)
            {
                case "Manager:ReviewSeconds": options.ReviewSeconds = value; break;
                case "Manager:TimeoutSeconds": options.TimeoutSeconds = value; break;
                case "Manager:MaxAttempts": options.MaxAttempts = value; break;
                case "Manager:Priority": options.Priority = value; break;
                default: options.MaxEntriesPerScan = value; break;
            }
        });

        Assert.Contains(setting, failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Manager:ReviewSeconds", 300)]
    [InlineData("Manager:ReviewSeconds", 604_800)]
    [InlineData("Manager:MaxEntriesPerScan", 50)]
    public void The_bound_itself_is_allowed(string setting, int value)
    {
        var options = new ManagerOptions();
        if (setting == "Manager:ReviewSeconds")
        {
            options.ReviewSeconds = value;
        }
        else
        {
            options.MaxEntriesPerScan = value;
        }

        Assert.True(new ManagerOptionsValidator().Validate(null, options).Succeeded);
    }

    /// <summary>A file that says nothing about triggers gets the three defaults, and no duplicates.</summary>
    [Fact]
    public void A_file_that_names_no_triggers_gets_the_defaults()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Manager":{"ReviewSeconds":600}}""");

        var options = new ManagerOptions();
        ManagerOptions.Fill(
            JasonConfiguration.Build(dir.Paths, shippedSettingsDirectory: null).GetSection(ManagerOptions.Section),
            options);

        Assert.Equal(ManagerOptions.Default, options.Triggers);
    }

    /// <summary>The section binds from the operator's own file like every other one.</summary>
    [Fact]
    public void The_manager_section_binds_from_the_user_file()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(
            dir.Paths.UserSettingsFile,
            """{"Manager":{"ReviewSeconds":600,"Triggers":["workitem_failed"],"MaxAttempts":2}}""");

        var options = new ManagerOptions();
        ManagerOptions.Fill(
            JasonConfiguration.Build(dir.Paths, shippedSettingsDirectory: null).GetSection(ManagerOptions.Section),
            options);

        Assert.Equal(600, options.ReviewSeconds);

        // Narrowed, not widened. The standard binder appends to a list that already holds values, so this line
        // read "workitem_failed, approval_rejected, external_effect_reported, workitem_failed" until the section
        // was filled by hand — an operator's narrowing turned into the opposite, silently.
        Assert.Equal([JournalKinds.WorkItemFailed], options.Triggers);
        Assert.Equal(2, options.MaxAttempts);
    }

    /// <summary>
    /// The narrowing, through the container that really composes it rather than through the helper alone: the
    /// binder appends to a list that already has values, so an installation asking for one trigger kind has to
    /// end up with one and not with four.
    /// </summary>
    [Fact]
    public async Task A_narrowed_list_narrows_in_a_running_runtime()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Manager":{"Triggers":["workitem_failed"]}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<ManagerOptions>>();

        Assert.Equal([JournalKinds.WorkItemFailed], monitor.CurrentValue.Triggers);
    }

    /// <summary>And a kind the runtime never writes stops the runtime from starting at all.</summary>
    [Fact]
    public async Task A_trigger_the_runtime_never_writes_refuses_the_start_for_real()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Manager":{"Triggers":["inbound_reply"]}}""");

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(
            () => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.Contains("Manager:Triggers[0]", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A live edit that is invalid keeps the last value that was good. The loop re-reads its settings every
    /// tick, so a half-typed file must not become a runtime that summons on nothing or on everything.
    /// </summary>
    [Fact]
    public async Task A_live_edit_that_is_invalid_keeps_the_last_good_value()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Manager":{"ReviewSeconds":600}}"""));

        var settings = fixture.Runtime.Services.GetRequiredService<LiveSettings<ManagerOptions>>();
        Assert.Equal(600, settings.Current.ReviewSeconds);

        File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Manager":{"ReviewSeconds":1}}""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && settings.Current.ReviewSeconds == 600)
        {
            await Task.Delay(25, Ct);
        }

        Assert.Equal(600, settings.Current.ReviewSeconds);
    }

    private static string Validate(Action<ManagerOptions> configure)
    {
        var options = new ManagerOptions();
        configure(options);
        var result = new ManagerOptionsValidator().Validate(null, options);
        Assert.False(result.Succeeded);
        return string.Join(" ", result.Failures!);
    }
}
