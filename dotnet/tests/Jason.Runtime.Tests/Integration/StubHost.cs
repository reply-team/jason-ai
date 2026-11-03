using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The stand-in host installed under a host's own command word, in a directory of its own that is the whole search path
/// a runtime is given: a profile naming the word resolves to the stand-in exactly as it would to the installed host,
/// and no program of the machine's can answer for it. The stand-in ignores the host's own flags and takes its behaviour
/// from the brief, so the command a host composes runs as it is.
/// </summary>
internal sealed class StubHost : IDisposable
{
    /// <summary>What the stand-in is built as, and what each copy brings along so it starts from its new name.</summary>
    private static readonly string[] Companions =
        ["Jason.FakeAgentHost.dll", "Jason.FakeAgentHost.deps.json", "Jason.FakeAgentHost.runtimeconfig.json", "Jason.Contracts.dll"];

    private StubHost(string directory, string program)
    {
        Directory = directory;
        Program = program;
        SearchPath = new TestSearchPath { Path = directory };
    }

    /// <summary>The directory the stand-in was copied into, which nothing else is in.</summary>
    public string Directory { get; }

    /// <summary>The copied stand-in, named as the host's program is.</summary>
    public string Program { get; }

    /// <summary>A search path holding the stand-in's directory and nothing else, for the runtime to resolve programs on.</summary>
    public ISearchPath SearchPath { get; }

    /// <summary>The stand-in, copied under the command word of <paramref name="kind"/>.</summary>
    public static StubHost OnPath(AgentHostKind kind)
    {
        var directory = Path.Combine(Path.GetTempPath(), "jason-tests", "hosts", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);

        var word = AgentHosts.Of(kind).CommandWord;
        var program = Path.Combine(directory, OperatingSystem.IsWindows() ? word + ".exe" : word);
        File.Copy(ProfileFixture.Program, program);
        foreach (var companion in Companions)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, companion), Path.Combine(directory, companion));
        }

        return new StubHost(directory, program);
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A child that has not quite let go of its image on Windows; the temporary tree is the machine's to clear.
        }
    }
}
