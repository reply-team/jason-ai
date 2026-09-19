using Jason.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The loop's numbers, and the one list in them that decides whether anything happens at all.
/// </summary>
public class ManagerOptionsTests
{
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
    public void The_defaults_are_the_three_kinds_this_version_reacts_to()
    {
        var options = new ManagerOptions();

        Assert.Equal(
            [JournalKinds.WorkItemFailed, JournalKinds.ApprovalRejected, JournalKinds.ExternalEffectReported],
            options.Triggers);
        Assert.Equal(18_000, options.ReviewSeconds);
        Assert.Equal(900, options.TimeoutSeconds);
        Assert.Equal(1, options.MaxAttempts);
        Assert.Equal(0, options.Priority);
        Assert.Equal(500, options.MaxEntriesPerScan);

        // The default list is three kinds and not four: the fourth, a decision being answered, is not a kind
        // this version writes, and the validator above is what makes that a fact rather than an intention.
        Assert.DoesNotContain("decision_answered", options.Triggers);
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

    private static string Validate(Action<ManagerOptions> configure)
    {
        var options = new ManagerOptions();
        configure(options);
        var result = new ManagerOptionsValidator().Validate(null, options);
        Assert.False(result.Succeeded);
        return string.Join(" ", result.Failures!);
    }
}
