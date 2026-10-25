using System.Reflection;

namespace Jason.Cli.Update;

/// <summary>
/// Every assembly this CLI can reach, loaded before an update or a rollback moves the file this process is running as.
/// </summary>
/// <remarks>
/// <para>
/// A single-file executable reads its managed assemblies out of its own file, and lazily: an assembly is read from the
/// file at the path the process started from the first time a method naming one of its types is compiled, and once it
/// is loaded it stays loaded whatever becomes of that path. The apply and the rollback rename the running image aside
/// and put another executable at its path, so a first load after that reads the other file at an offset that belongs
/// to this one. With the same .NET patch on both sides every offset happened to match; with another patch they do not,
/// and on Linux the apply failed as it started the new runtime, unable to find <c>System.Diagnostics.Process</c>.
/// </para>
/// <para>
/// So the closure is walked from this assembly's references to a fixed point rather than written down as a list of
/// types: which assembly the steps after a rename will touch first cannot be made complete by reading them, and a list
/// would be right only until the next change to those steps.
/// </para>
/// </remarks>
public static class AssemblyWarmUp
{
    /// <summary>Loads this CLI's whole referenced-assembly closure into this process.</summary>
    public static void Run() => Walk(typeof(AssemblyWarmUp).Assembly, Assembly.Load);

    /// <summary>
    /// The walk itself, over the loader it is given: every assembly reachable from <paramref name="root"/> through
    /// references, each loaded once, and the names of those that could not be.
    /// </summary>
    /// <remarks>
    /// A reference that cannot be loaded is passed over rather than raised. The walk exists to have loaded, before the
    /// rename, whatever would otherwise be loaded after it; a reference this build does not carry would fail the same
    /// way at any moment, and a warm-up that failed an update for it would fail one that never needed it.
    /// </remarks>
    internal static (IReadOnlyCollection<Assembly> Loaded, IReadOnlyCollection<string> Missing) Walk(Assembly root, Func<AssemblyName, Assembly> load)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(load);

        var loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase) { [root.GetName().Name!] = root };
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Assembly>([root]);
        while (pending.TryDequeue(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is not { } name || loaded.ContainsKey(name) || missing.Contains(name))
                {
                    continue;
                }

                try
                {
                    var found = load(reference);
                    loaded[name] = found;
                    pending.Enqueue(found);
                }
                catch (Exception error) when (error is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    missing.Add(name);
                }
            }
        }

        return (loaded.Values, missing);
    }
}
