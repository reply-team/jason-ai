namespace Jason.Contracts.Execution;

/// <summary>
/// The variables the launcher sets in a child executor's environment. Both are non-secret values the launch
/// envelope already carries, and they exist so that an agent reporting through the CLI is its attempt without
/// having to remember to say so: the CLI reads them when no actor is given on the command line.
/// <para>
/// This is a documented default set by the launcher, not an inference from whatever process happens to be
/// calling: a claim is still verified against the attempt the runtime knows, so a variable set by hand buys
/// nothing that was not already true.
/// </para>
/// </summary>
public static class ExecutionEnvironment
{
    /// <summary>The attempt the child was launched for; the fencing token every executor operation carries.</summary>
    public const string AttemptIdVariable = "JASON_ATTEMPT_ID";

    /// <summary>The work item that attempt belongs to.</summary>
    public const string WorkItemIdVariable = "JASON_WORK_ITEM_ID";
}
