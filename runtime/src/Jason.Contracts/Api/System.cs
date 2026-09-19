namespace Jason.Contracts.Api;

/// <summary><c>system.shutdown</c> takes no arguments; the body exists so every operation reads the same way.</summary>
public sealed record ShutdownRequest();

/// <summary>Answered before the host stops, so the caller knows which instance is going down and can wait for the pid.</summary>
public sealed record ShutdownResponse(string InstanceId, int Pid, bool Stopping);

/// <summary><c>system.drain</c> and <c>system.resume</c> take no arguments either.</summary>
public sealed record DrainRequest();

/// <summary>
/// What a drain or a resume answers: the state the dispatcher is now in, and how many attempts are still
/// running. The second number is what an applier polls — it waits for it to reach zero, or for its own bound
/// to run out — so the answer carries it rather than making the caller ask <c>system.info</c> next.
/// </summary>
public sealed record DrainResponse(DispatcherState State, int RunningAttempts);
