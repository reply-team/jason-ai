using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>What one load made of one candidate directory, whether or not the load ended in a swap.</summary>
public sealed record CandidateReport(string Directory, string? Id, CandidateStatus Status, IReadOnlyList<ManifestProblem> Problems);

/// <summary>
/// The diagnostics of the last load. A rejected load leaves the previous snapshot alone and this report behind,
/// which is the only way anyone finds out why nothing changed.
/// </summary>
public sealed record ReloadReport(DateTime At, SnapshotSource Source, bool Activated, IReadOnlyList<CandidateReport> Candidates);

/// <summary>A load: the snapshot it would activate, or none when some candidate's package is not valid.</summary>
public sealed record LoadResult(PluginSnapshot? Snapshot, ReloadReport Report);
