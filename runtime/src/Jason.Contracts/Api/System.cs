namespace Jason.Contracts.Api;

/// <summary><c>system.shutdown</c> takes no arguments; the body exists so every operation reads the same way.</summary>
public sealed record ShutdownRequest();

/// <summary>Answered before the host stops, so the caller knows which instance is going down and can wait for the pid.</summary>
public sealed record ShutdownResponse(string InstanceId, int Pid, bool Stopping);
