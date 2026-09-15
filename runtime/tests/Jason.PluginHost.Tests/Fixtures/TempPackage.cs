namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>A package written into a temporary directory of its own and removed with the test.</summary>
public sealed class TempPackage : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "jason-plugin-host-tests", Guid.NewGuid().ToString("N"));

    public TempPackage(string mainJs = "export function invoke() { return { result: {} }; }", IReadOnlyDictionary<string, string>? modules = null)
    {
        Root = InvocationFactory.WritePackage(Path.Combine(_parent, "package"), mainJs, modules);
    }

    /// <summary>The package directory: what the envelope names and what the module loader is confined to.</summary>
    public string Root { get; }

    /// <summary>One level above the package, where a test puts the file a plugin must not be able to import.</summary>
    public string Outside => _parent;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_parent, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
