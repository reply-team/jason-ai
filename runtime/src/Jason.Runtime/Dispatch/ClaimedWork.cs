namespace Jason.Runtime.Dispatch;

/// <summary>
/// One claim handed from the scan to the handler pool. Both the internal keys and the public ids travel: the
/// handler reloads by key in its own scope, and everything it says about the work uses the public ids.
/// </summary>
public sealed record ClaimedWork(int WorkItemId, int AttemptId, string WorkItemPublicId, string AttemptPublicId);
