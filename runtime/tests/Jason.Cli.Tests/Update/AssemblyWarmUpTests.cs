using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Jason.Cli.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// What the warm-up an update and a rollback take before their first rename loads: the CLI's whole referenced-assembly
/// closure, to the last level.
/// </summary>
/// <remarks>
/// The fault it exists for — a single-file executable reading an assembly out of the file now at its own path, another
/// build — needs a published executable and a second .NET patch, which only the update job has. What can be held here
/// is the mechanism: the walk reaches every level, and after it nothing of the closure is left to load.
/// </remarks>
public class AssemblyWarmUpTests
{
    /// <summary>
    /// Every assembly the walk loaded has every reference it can be given loaded beside it, so the walk reached a fixed
    /// point: a walk that stopped at the CLI's own references leaves theirs out.
    /// </summary>
    [Fact]
    public void The_walk_loads_the_references_of_every_assembly_it_loads()
    {
        var (loaded, missing) = AssemblyWarmUp.Walk(typeof(AssemblyWarmUp).Assembly, Assembly.Load);

        var names = loaded.Select(assembly => assembly.GetName().Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unreached = loaded
            .SelectMany(assembly => assembly.GetReferencedAssemblies().Select(reference => (From: assembly.GetName().Name, To: reference.Name!)))
            .Where(edge => !names.Contains(edge.To) && !missing.Contains(edge.To))
            .Select(edge => $"{edge.From} -> {edge.To}")
            .ToList();
        Assert.True(unreached.Count == 0, $"the walk stopped before these references: {string.Join(", ", unreached)}");

        // And it went past the first level: an assembly only a reference of a reference names is among those loaded.
        var first = typeof(AssemblyWarmUp).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(names, name => !first.Contains(name) && name != typeof(AssemblyWarmUp).Assembly.GetName().Name);
    }

    /// <summary>
    /// After the warm-up every assembly of the closure is in this process, where the closure is worked out from the files'
    /// metadata, without loading anything, so the answer does not come from the code it checks.
    /// </summary>
    [Fact]
    public void After_the_warm_up_every_assembly_of_the_closure_is_loaded()
    {
        var expected = ClosureFromMetadata(typeof(AssemblyWarmUp).Assembly);

        AssemblyWarmUp.Run();

        var present = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var absent = expected.Where(name => !present.Contains(name)).Order(StringComparer.Ordinal).ToList();
        Assert.True(absent.Count == 0, $"after the warm-up these assemblies of the closure are still not loaded: {string.Join(", ", absent)}");
    }

    /// <summary>
    /// A reference that cannot be loaded is passed over, and the walk goes on with the rest: what it is there to do is
    /// load what an update would otherwise load after its rename, not refuse an update over a reference it never uses.
    /// </summary>
    [Fact]
    public void A_reference_that_cannot_be_loaded_is_passed_over_and_the_walk_goes_on()
    {
        var root = typeof(AssemblyWarmUp).Assembly;
        var refused = root.GetReferencedAssemblies().First(reference => reference.Name == "System.CommandLine").Name!;

        var (loaded, missing) = AssemblyWarmUp.Walk(
            root,
            reference => reference.Name == refused ? throw new FileNotFoundException("not in this build", reference.Name) : Assembly.Load(reference));

        Assert.Equal([refused], missing);
        Assert.Contains(loaded, assembly => assembly.GetName().Name == "Jason.Contracts");
    }

    /// <summary>
    /// The names of every assembly reachable from <paramref name="root"/> by its references, read from each file's metadata
    /// and resolved the way this process resolves them: through its trusted platform assemblies.
    /// </summary>
    private static HashSet<string> ClosureFromMetadata(Assembly root)
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.GetName().Name! };
        var pending = new Queue<string>([root.Location]);
        while (pending.TryDequeue(out var path))
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            var metadata = image.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
                if (trusted.TryGetValue(name, out var file) && closure.Add(name))
                {
                    pending.Enqueue(file);
                }
            }
        }

        return closure;
    }
}
