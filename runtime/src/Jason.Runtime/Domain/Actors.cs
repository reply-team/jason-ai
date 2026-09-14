using Jason.Contracts.Api;

namespace Jason.Runtime.Domain;

/// <summary>
/// The claimed actor of an operation. Claims are recorded, not verified: the runtime holds one capability token
/// and no caller identity, so the honest thing is to write down what the caller said it was.
/// </summary>
public static class Actors
{
    public const int MaxIdLength = 100;

    /// <summary>The runtime's own writes. Callers cannot claim this, which is what keeps it meaningful.</summary>
    public static ActorRef Runtime { get; } = new(ActorType.System, "runtime");

    /// <summary>Default is human. "system" is reserved for the runtime; a role must name itself.</summary>
    public static ActorRef Resolve(ActorRef? claimed)
    {
        if (claimed is null)
        {
            return new ActorRef(ActorType.Human);
        }

        var errors = new ValidationErrors();
        if (claimed.Type == ActorType.System)
        {
            errors.Add("actor.type", "not_allowed", "actor type 'system' is reserved for the runtime; callers claim 'human' or 'role'.");
        }

        if (claimed.Type == ActorType.Role && string.IsNullOrWhiteSpace(claimed.Id))
        {
            errors.Add("actor.id", "required", "a role actor must carry its role id.");
        }

        if (claimed.Id is { Length: > MaxIdLength })
        {
            errors.Add("actor.id", "too_long", $"actor.id must be at most {MaxIdLength} characters.");
        }

        errors.ThrowIfAny();
        return new ActorRef(claimed.Type, string.IsNullOrWhiteSpace(claimed.Id) ? null : claimed.Id.Trim());
    }
}
