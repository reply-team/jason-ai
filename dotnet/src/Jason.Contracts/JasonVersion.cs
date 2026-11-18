using System.Reflection;

namespace Jason.Contracts;

/// <summary>The build's informational version, e.g. <c>0.1.0-dev+1a2b3c4</c>. Stamped by MSBuild for every assembly.</summary>
public static class JasonVersion
{
    /// <summary>
    /// The words a test build adds wherever it states its version to a person, after the version itself; a released build
    /// adds nothing.
    /// </summary>
    public const string TestBuildMark = "a test build, not a release";

    /// <summary>The line a test build's <c>jason --version</c> writes on standard error, the version alone staying on standard output.</summary>
    public const string TestBuildSentence = "This jason is " + TestBuildMark + ".";

    public static string Current { get; } =
        typeof(JasonVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(JasonVersion).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

#if JASON_TEST_BUILD
    /// <summary>Whether this is a test build, made with <c>-p:JasonTestBuild=true</c>: this one is.</summary>
    public static bool TestBuild => true;
#else
    /// <summary>Whether this is a test build, made with <c>-p:JasonTestBuild=true</c>: never a released one.</summary>
    public static bool TestBuild => false;
#endif

    /// <summary>
    /// <paramref name="version"/> as it is stated to a person: with <see cref="TestBuildMark"/> after it, in brackets, where
    /// <paramref name="testBuild"/> holds, and as it is otherwise.
    /// </summary>
    public static string Stated(string version, bool testBuild) => testBuild ? $"{version} ({TestBuildMark})" : version;

    /// <summary>The runtime report's <c>test_build</c>: true in a test build, and null, so absent, in any other.</summary>
    public static bool? ReportMember(bool testBuild) => testBuild ? true : null;
}
