namespace Jason.Runtime.Harness;

/// <summary>
/// The home directory this runtime resolved when it started, for the data directory it started on: where the harness
/// root's default is made, and the one folder the root may never be.
/// </summary>
public sealed record HarnessHome(string Directory);
