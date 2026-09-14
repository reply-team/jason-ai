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
    }
}
