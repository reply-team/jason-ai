namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// Where the stand-in vendor CLI is, as the test projects see it: referenced, so its assembly and its apphost sit
/// next to the tests. The apphost is what an executable resolver finds on a search path; the dll is what a test
/// starts through <c>dotnet</c> when it wants no apphost in the way.
/// </summary>
public static class FakeProviderCli
{
    public static string Dll => Path.Combine(AppContext.BaseDirectory, "Jason.FakeProviderCli.dll");

    public static string Directory => AppContext.BaseDirectory;

    /// <summary>The apphost's name without its extension, which is how a manifest declares an executable.</summary>
    public static string ExecutableName => "Jason.FakeProviderCli";

    public static string ExecutablePath => Path.Combine(Directory, ExecutableName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

    /// <summary>
    /// The text whose code points <c>stdin-codepoints</c> answered with: what its standard input carried, decoded as
    /// UTF-8 by the program itself, so the console's code page of neither side has a say in what is read here.
    /// </summary>
    public static string FromCodepoints(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return string.Concat(
            answer
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(point => char.ConvertFromUtf32(int.Parse(point, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture))));
    }
}
