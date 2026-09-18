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
/// attempt of the same work item is a different session, because it is a different run.
/// </param>
public sealed record HostLaunch(IReadOnlyList<string> Command, string SessionId);
