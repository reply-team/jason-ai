namespace Jason.Runtime.Plugins.Manifest;

/// <summary>
/// One thing wrong with a candidate, named where it is wrong. <see cref="Path"/> is the place inside the
/// manifest — <c>kind</c>, <c>capabilities.exec.executables[0].name</c> — or <c>line:column</c> when the YAML
/// itself did not parse; the <c>plugin.yaml#</c> prefix and the directory are added once, where it is rendered.
/// </summary>
public sealed record ManifestProblem(string Code, string Path, string Message)
{
    /// <summary>A problem about the machine rather than the package: it holds its own plugin back, nothing else.</summary>
    public bool Environmental => ProblemCodes.IsEnvironmental(Code);

    /// <summary>
    /// A binding pattern that could not finish its load-time probe, twice. It is a package problem like any other at a
    /// reload — a pattern that cannot finish on a warm engine is at fault — but it is also a measure of this machine at
    /// this moment, so the start, where nobody is there to reload, loads once more when it is all that held a load back.
    /// It is not part of what an operator reads: the code they see is <see cref="Code"/>.
    /// </summary>
    public bool Unmeasured { get; init; }
}
