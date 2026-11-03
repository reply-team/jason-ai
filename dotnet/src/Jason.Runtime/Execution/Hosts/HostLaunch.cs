namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// What one attempt of an execution profile comes to: the program and every argument, ready to be started, and
/// the session the host was told to keep. Nothing is quoted and no shell sees any of it — each element is one
/// argument — so a value with a space in it is a value rather than two arguments.
/// </summary>
/// <param name="Command">The program first, then its arguments, in the order they are passed.</param>
/// <param name="SessionId">
/// The session the host is told to use, minted here rather than left to the host so that the runtime can name
/// the session an attempt ran in without asking anybody. It is recorded on the attempt's provenance; a later
/// attempt of the same work item is a different session, because it is a different run. Null for a host that mints
/// its own (<see cref="IAgentHost.TakesSessionId"/>).
/// </param>
/// <param name="Unreadable">
/// The levels — <c>client</c>, <c>project</c>, <c>campaign</c>, as the envelope names them — whose memory file the launch
/// named and the host could not write as a rule at all, so the session cannot read it: a host writes the narrowest rule its
/// form has, a file where it can and the folder of the level where it reads folders only, and none where the person's own
/// rules decide. A level that was null is not here: nothing was named.
/// </param>
/// <param name="Deny">
/// What the host denies on the revision's account, beside the profile's own deny rules and in the same place: each tool
/// of the vocabulary the revision does not grant, by the host's own name for it. Empty where it grants them all.
/// </param>
/// <param name="Allow">
/// What the host allows through the work directory, carried exactly where <paramref name="Deny"/> is carried: the
/// command word that calls the runtime back and the memory files' paths in the host's own form, never the capability
/// token or the envelope. Empty for a host whose allow rules travel on the command line.
/// </param>
public sealed record HostLaunch(
    IReadOnlyList<string> Command,
    string? SessionId,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<string> Deny,
    IReadOnlyList<string> Allow);
