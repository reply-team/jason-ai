using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// One claim handed from the scan to the handler pool. Both the internal keys and the public ids travel: the
/// handler reloads by key in its own scope, and everything it says about the work uses the public ids.
/// </summary>
/// <param name="CampaignPublicId">
/// The campaign the work belongs to. The log scope an attempt runs under has to be open before the handler loads
/// anything, and again around the pool's own line when a handler gives out — so the id arrives with the claim
/// rather than being read back from a row the scope is meant to cover.
/// </param>
/// <param name="Plan">
/// For a provider operation, everything the claim resolved — the package, the route and the composed input —
/// carried in memory rather than looked up again, so a reload between the claim and the run cannot change what
/// was already decided. Null for agent work, which resolves its command at claim the same way.
/// </param>
public sealed record ClaimedWork(
    int WorkItemId,
    int AttemptId,
    string WorkItemPublicId,
    string AttemptPublicId,
    string CampaignPublicId,
    ProviderOpPlan? Plan = null,

    /// <summary>
    /// What an agent attempt needs beyond its command line: the deny rules and the callback word the launcher
    /// writes into the per-attempt work directory. Null for provider work and for a role run by its own entry
    /// command, neither of which has a profile to say it.
    /// </summary>
    AgentLaunch? Agent = null,

    /// <summary>
    /// The person's memory files as the claim found them, for every role launch — profile or entry command — and the
    /// envelope's to name as they are: decided once, where the command that may read them was composed and recorded,
    /// so the files the envelope names are the files the rules were composed from. A file that got no rule, and every
    /// file of an entry-command launch, which is composed none, is named all the same. Null for provider work, which
    /// has no envelope.
    /// </summary>
    MemoryFileLocations? MemoryFiles = null,

    /// <summary>
    /// The public id of the campaign's project as the claim found it, for every role launch: the project the role is told
    /// its notes are kept under, read where the memory files were decided, so a campaign moved between the claim and the
    /// start of the attempt gives the role one project's files and not another's notes. Null for provider work only.
    /// </summary>
    string? ProjectPublicId = null,

    /// <summary>
    /// For a role's run that follows a wait, what the wait ended with, composed at the claim inside the claim's own
    /// transaction; null for every other run, provider work included.
    /// </summary>
    Resumption? Resumption = null);

/// <summary>The per-item half of an agent launch, as the claim resolved it.</summary>
/// <param name="Allow">What the host allows through the work directory; empty for a host whose allow rules travel on its command line.</param>
/// <param name="Host">The host the profile names, so the work directory is laid out where that host reads it.</param>
public sealed record AgentLaunch(IReadOnlyList<string> Deny, IReadOnlyList<string> Allow, string CliCommand, AgentHostKind Host);
