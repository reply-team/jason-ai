using Jason.Contracts.Api;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Tests.Domain;

public class ActorsTests
{
    [Fact]
    public void No_claim_means_a_human()
    {
        Assert.Equal(new ActorRef(ActorType.Human), Actors.Resolve(null));
    }

    [Fact]
    public void Claiming_system_is_refused_because_it_belongs_to_the_runtime()
    {
        var ex = Assert.Throws<ValidationException>(() => Actors.Resolve(new ActorRef(ActorType.System, "runtime")));

        Assert.Equal("validation_failed", ex.Code);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("actor.type", Assert.Single(ex.Details!).Field);
    }

    [Fact]
    public void A_role_must_name_itself()
    {
        var ex = Assert.Throws<ValidationException>(() => Actors.Resolve(new ActorRef(ActorType.Role, "  ")));

        Assert.Equal("actor.id", Assert.Single(ex.Details!).Field);
    }

    [Fact]
    public void A_named_role_is_accepted_and_trimmed()
    {
        Assert.Equal(new ActorRef(ActorType.Role, "planner"), Actors.Resolve(new ActorRef(ActorType.Role, " planner ")));
    }

    [Fact]
    public void A_human_may_stay_anonymous_but_not_carry_an_endless_id()
    {
        Assert.Equal(new ActorRef(ActorType.Human), Actors.Resolve(new ActorRef(ActorType.Human, "   ")));

        var ex = Assert.Throws<ValidationException>(() => Actors.Resolve(new ActorRef(ActorType.Human, new string('x', Actors.MaxIdLength + 1))));

        Assert.Equal("too_long", Assert.Single(ex.Details!).Code);
    }

    [Fact]
    public void The_runtime_writes_under_its_own_system_actor()
    {
        Assert.Equal(ActorType.System, Actors.Runtime.Type);
        Assert.Equal("runtime", Actors.Runtime.Id);
        Assert.Equal(ActorType.System, Actors.Dispatcher.Type);
        Assert.Equal("dispatcher", Actors.Dispatcher.Id);
    }

    [Fact]
    public void An_attempt_may_claim_to_be_the_actor_but_must_say_which_one()
    {
        Assert.Equal(new ActorRef(ActorType.Attempt, "att_A"), Actors.Resolve(new ActorRef(ActorType.Attempt, " att_A ")));

        var ex = Assert.Throws<ValidationException>(() => Actors.Resolve(new ActorRef(ActorType.Attempt)));

        Assert.Equal("actor.id", Assert.Single(ex.Details!).Field);
        Assert.Equal("required", ex.Details![0].Code);
    }

    [Fact]
    public void An_attempt_actor_is_derived_from_the_attempt_itself()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign());
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, DateTime.UtcNow);

        Assert.Equal(new ActorRef(ActorType.Attempt, attempt.PublicId), Actors.ForAttempt(attempt));
    }

    [Fact]
    public async Task An_attempt_claim_is_the_one_the_runtime_can_check()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var item = WorkItemFactory.NewAiRole(campaign);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, DateTime.UtcNow);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await ActorVerification.VerifyAsync(db, Actors.ForAttempt(attempt), TestContext.Current.CancellationToken);
        await ActorVerification.VerifyAsync(db, new ActorRef(ActorType.Role, "planner"), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => ActorVerification.VerifyAsync(db, new ActorRef(ActorType.Attempt, "att_NOBODY"), TestContext.Current.CancellationToken));

        Assert.Equal("validation_failed", ex.Code);
        Assert.Equal("actor.id", Assert.Single(ex.Details!).Field);
        Assert.Equal("unknown", ex.Details![0].Code);
    }
}
