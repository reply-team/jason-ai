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
}
