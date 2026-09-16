using Jason.Contracts.Discovery;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// What a plugin-host child is actually started with. The plugin host is this same program in another mode, and
/// the one way that goes wrong is the muxer: a runtime started as <c>dotnet jason.dll</c> has <c>dotnet</c> for
/// its process path and the assembly is nowhere in it, so a locator that answers the process path alone starts
/// <c>dotnet plugin-host</c> — which is not a program — and every invocation dies before a plugin runs. The
/// decision is made from the two values it depends on rather than read from the ambient process, so these are
/// tests of the answer and never of a live child.
/// </summary>
public class ProcessPathLocatorTests
{
    [Fact]
    public void A_published_executable_starts_itself()
    {
        var locator = new ProcessPathLocator("/opt/jason/jason", "/opt/jason/jason.dll");

        Assert.Equal(["/opt/jason/jason"], locator.Command);
    }

    [Fact]
    public void A_runtime_started_through_the_muxer_names_the_assembly_the_muxer_has_to_run()
    {
        var locator = new ProcessPathLocator("/usr/share/dotnet/dotnet", "/build/jason.dll");

        Assert.Equal(["dotnet", "/build/jason.dll"], locator.Command);
    }

    /// <summary>The same host, under the name the platform gives it.</summary>
    [Fact]
    public void The_muxer_is_recognised_whatever_extension_it_carries()
    {
        var locator = new ProcessPathLocator("/Program Files/dotnet/DOTNET.exe", "/build/jason.dll");

        Assert.Equal(["dotnet", "/build/jason.dll"], locator.Command);
    }

    /// <summary>
    /// A host that publishes no process path at all leaves the muxer as the only answer that can still run, and
    /// it is the answer the CLI has always given when it starts a runtime of its own.
    /// </summary>
    [Fact]
    public void A_process_with_no_path_is_still_started_through_the_muxer()
    {
        var locator = new ProcessPathLocator(processPath: null, "/build/jason.dll");

        Assert.Equal(["dotnet", "/build/jason.dll"], locator.Command);
    }

    /// <summary>
    /// One rule and one place for it: starting the plugin host and starting a detached runtime are the same
    /// question — how do I run this program again — and the CLI has answered it correctly all along.
    /// </summary>
    [Fact]
    public void The_locator_answers_with_what_this_program_was_started_as()
    {
        Assert.Equal(SelfExecutable.Command, new ProcessPathLocator().Command);
    }
}
