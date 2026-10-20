using Jason.Cli.Process;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// The sentence a runtime that will not start says, heard whole by the CLI's start of it, with this process's console on
/// a legacy code page the collection sets. Beside <see cref="StartRefusalTests"/>, which hear the sentence through two
/// shipped processes: there the CLI is a child with a console of its own, on the system's page, which no row can set.
/// </summary>
[Collection(LegacyConsolePageCollection.Name)]
public class StartRefusalPageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The data directory is a file whose name carries a letter outside ASCII, so the runtime refuses and says the path,
    /// and the path comes back as it was. The runtime writes UTF-8; read in the console's page, ó came back as two other
    /// characters.
    /// </summary>
    /// <remarks>
    /// The start is the CLI's own, made in this process, and the runtime is the shipped executable, given nothing but its
    /// data directory. Starting it makes this process's own standard handles uninheritable, as every start of a runtime
    /// does; no row here depends on them.
    /// </remarks>
    [Fact]
    public async Task The_sentence_a_runtime_that_will_not_start_says_keeps_every_letter_of_its_path()
    {
        using var tree = new TempTree();
        var root = Path.Combine(tree.Root, "jasón");
        await File.WriteAllTextAsync(root, "a file where the data directory goes", Ct);

        using var runtime = RuntimeProcessControl.Instance.Launch(new JasonPaths(root), ["dotnet", GoldenPath.Assembly]);
        using (var bound = CancellationTokenSource.CreateLinkedTokenSource(Ct))
        {
            bound.CancelAfter(TimeSpan.FromMinutes(2));
            while (!runtime.HasExited)
            {
                await Task.Delay(50, bound.Token);
            }
        }

        var said = runtime.Said;
        Assert.True(runtime.ExitCode != 0, $"A runtime over a data directory that is a file exited {runtime.ExitCode} and said: {said}");
        Assert.True(said.Contains($"The data directory '{Path.GetFullPath(root)}", StringComparison.Ordinal), $"The runtime was heard to say: {said}");
    }
}
