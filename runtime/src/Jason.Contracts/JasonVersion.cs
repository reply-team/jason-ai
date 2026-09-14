using System.Reflection;

namespace Jason.Contracts;

/// <summary>The build's informational version, e.g. <c>0.1.0-dev+1a2b3c4</c>. Stamped by MSBuild for every assembly.</summary>
public static class JasonVersion
{
    public static string Current { get; } =
        typeof(JasonVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(JasonVersion).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
