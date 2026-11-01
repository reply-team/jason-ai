namespace Jason.Runtime.Tests;

/// <summary>
/// The collection every test class that writes a process-wide environment variable belongs to. Classes in one
/// collection never run at the same time, so a value one test sets for its child to read back cannot be seen,
/// or overwritten, by another class's test that happens to be running alongside it.
/// </summary>
public static class ProcessEnvironmentCollection
{
    public const string Name = "process environment";
}
