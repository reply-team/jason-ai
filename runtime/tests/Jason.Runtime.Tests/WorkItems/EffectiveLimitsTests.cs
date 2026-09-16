using Jason.Runtime.Configuration;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.WorkItems;

public class EffectiveLimitsTests
{
    [Fact]
    public void An_item_with_no_overrides_gets_the_defaults_of_its_kind()
    {
        var options = new DispatcherOptions();
        var campaign = WorkItemFactory.NewCampaign();

        var role = EffectiveLimits.For(WorkItemFactory.NewAiRole(campaign), options);
        var provider = EffectiveLimits.For(WorkItemFactory.NewProviderOp(campaign), options);

        Assert.Equal(new EffectiveLimits(3600, 120, 3), role);
        Assert.Equal(new EffectiveLimits(600, 0, 3), provider);
    }

    [Fact]
    public void Every_override_wins_over_the_default_on_its_own()
    {
        var options = new DispatcherOptions();
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), configure: w => w.TimeoutSeconds = 90);

        Assert.Equal(new EffectiveLimits(90, 120, 3), EffectiveLimits.For(item, options));

        item.HeartbeatSeconds = 30;
        item.MaxAttempts = 1;
        Assert.Equal(new EffectiveLimits(90, 30, 1), EffectiveLimits.For(item, options));
    }

    [Fact]
    public void A_heartbeat_turned_off_on_the_item_stays_off()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), configure: w => w.HeartbeatSeconds = 0);

        Assert.Equal(0, EffectiveLimits.For(item, new DispatcherOptions()).HeartbeatSeconds);
    }

    [Fact]
    public void A_settings_change_reaches_work_that_is_already_queued()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign());
        var options = new DispatcherOptions { AiRole = new KindDefaults { TimeoutSeconds = 60, HeartbeatSeconds = 10, MaxAttempts = 1 } };

        Assert.Equal(new EffectiveLimits(60, 10, 1), EffectiveLimits.For(item, options));
    }
}
