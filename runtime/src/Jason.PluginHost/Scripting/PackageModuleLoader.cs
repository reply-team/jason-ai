using System.Globalization;
using System.Text.Json.Nodes;
using Jason.PluginHost.Sdk;
using Jint;
using Jint.Runtime.Modules;

namespace Jason.PluginHost.Scripting;

/// <summary>
/// Modules come from the package and from nowhere else. Jint's own loader already refuses a path that leaves the
/// root; this one adds the rules that make the refusal legible — a specifier must be a relative path to a script
/// inside the package — and the caps that keep one invocation from reading a disk full of files.
/// </summary>
public sealed class PackageModuleLoader : IModuleLoader
{
    public const int MaxModuleBytes = 1_048_576;
    public const int MaxModules = 256;

    private readonly DefaultModuleLoader _inner;
    private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);

    public PackageModuleLoader(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        _inner = new DefaultModuleLoader(packageRoot, restrictToBasePath: true);
    }

    public ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest)
    {
        var specifier = moduleRequest.Specifier ?? string.Empty;
        if (!specifier.StartsWith("./", StringComparison.Ordinal) && !specifier.StartsWith("../", StringComparison.Ordinal))
        {
            throw Refuse(specifier, "a module specifier must be a relative path inside the package, such as './modules/helper.js'");
        }

        if (!specifier.EndsWith(".js", StringComparison.Ordinal) && !specifier.EndsWith(".mjs", StringComparison.Ordinal))
        {
            throw Refuse(specifier, "a module must be a .js or .mjs file");
        }

        return _inner.Resolve(referencingModuleLocation, moduleRequest);
    }

    public Module LoadModule(Engine engine, ResolvedSpecifier resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var specifier = resolved.ModuleRequest.Specifier ?? resolved.Key;
        if (_loaded.Add(resolved.Key) && _loaded.Count > MaxModules)
        {
            throw Refuse(specifier, string.Create(CultureInfo.InvariantCulture, $"an invocation may load at most {MaxModules} modules"));
        }

        var path = resolved.Uri?.IsFile == true ? resolved.Uri.LocalPath : null;
        if (path is not null && File.Exists(path) && new FileInfo(path).Length > MaxModuleBytes)
        {
            throw Refuse(specifier, string.Create(CultureInfo.InvariantCulture, $"a module file is at most {MaxModuleBytes} bytes"));
        }

        return _inner.LoadModule(engine, resolved);
    }

    private static HostRuleException Refuse(string specifier, string why) =>
        new(OutcomeCodes.ModuleNotAllowed, $"'{specifier}' cannot be imported: {why}.", new JsonObject { ["specifier"] = specifier });
}
