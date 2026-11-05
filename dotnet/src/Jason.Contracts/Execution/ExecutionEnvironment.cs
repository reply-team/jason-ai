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
/// <para>
/// The attempt's variable has a second purpose: a CLI that finds it sends its value with every request as
/// <see cref="AttemptHeader"/>, and the verbs that are a person's own word refuse a request that carries it.
/// </para>
/// </summary>
public static class ExecutionEnvironment
{
    /// <summary>The attempt the child was launched for; the fencing token every executor operation carries.</summary>
    public const string AttemptIdVariable = "JASON_ATTEMPT_ID";

    /// <summary>The work item that attempt belongs to.</summary>
    public const string WorkItemIdVariable = "JASON_WORK_ITEM_ID";

    /// <summary>
    /// The header a CLI running inside an attempt sends on every request, reads included, with the attempt's id: its
    /// statement of where it runs, beside the token's statement of what it may do. It is never a fencing token.
    /// </summary>
    public const string AttemptHeader = "Jason-Attempt";
}
