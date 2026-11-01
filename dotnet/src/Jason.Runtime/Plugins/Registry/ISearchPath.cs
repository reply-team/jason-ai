namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// Where the runtime looks for a declared executable. It is a seam rather than a direct read of the environment
/// because an autostarted runtime sees a different search path than a shell does, and a test has to be able to
/// say exactly what the machine looks like without changing the machine.
/// </summary>
public interface ISearchPath
{
    string? Path { get; }

    /// <summary>The extensions a bare name may resolve through on Windows; null everywhere else.</summary>
    string? PathExt { get; }
}

/// <summary>The real thing: the search path of the runtime process itself.</summary>
public sealed class EnvironmentSearchPath : ISearchPath
{
    public string? Path => Environment.GetEnvironmentVariable("PATH");

    public string? PathExt => OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATHEXT") : null;
}
