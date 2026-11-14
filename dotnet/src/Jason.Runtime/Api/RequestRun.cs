namespace Jason.Runtime.Api;

/// <summary>
/// The attempt a request says it comes from, read once from its <c>Jason-Attempt</c> header by the door every operation
/// passes (<see cref="OperationEndpoints"/>) and held for the request's scope, so a service that must hold a request from
/// inside a run to speak as that run can read it. Null where the request names none, and in a service built outside a
/// request.
/// </summary>
public sealed class RequestRun
{
    public string? AttemptId { get; internal set; }
}
