using System.Collections.Frozen;
using Jason.Contracts.Journal;

namespace Jason.Contracts.Configuration;

/// <summary>
/// The kinds a <c>Manager:Triggers</c> entry may name: the chronicle kinds the runtime writes itself.
/// </summary>
/// <remarks>
/// The list lives here rather than beside the runtime's own because the command-line client needs it, to tell a
/// setting that names one of these from a word somebody typed, and cannot reference the runtime. It is the catalog's
/// list of the kinds published under <c>docs/contracts/journal/</c>, never a copy kept by hand: a kind the runtime
/// starts writing arrives with its document, and so is a word a settings file may name wherever one is described.
/// </remarks>
public static class ManagerTriggerKinds
{
    public static IReadOnlySet<string> All { get; } = JournalKindCatalog.Kinds.ToFrozenSet(StringComparer.Ordinal);
}
