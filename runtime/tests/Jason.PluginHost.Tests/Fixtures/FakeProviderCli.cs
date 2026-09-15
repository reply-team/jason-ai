namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>
/// Where the stand-in vendor CLI is, as the test projects see it: referenced, so its assembly and its apphost sit
/// next to the tests. The apphost is what an executable resolver finds on PATH; the dll is what a test starts
/// through <c>dotnet</c> when it wants no apphost in the way.
/// </summary>
public static class FakeProviderCli
{
    public static string Dll => Path.Combine(AppContext.BaseDirectory, "Jason.FakeProviderCli.dll");

    public static string Directory => AppContext.BaseDirectory;

    /// <summary>The apphost's name without its extension, which is how a manifest declares an executable.</summary>
    public static string ExecutableName => "Jason.FakeProviderCli";

    public static string ExecutablePath => Path.Combine(Directory, ExecutableName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
}
