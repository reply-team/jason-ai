using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// Plugin packages written into an isolated data directory on the fly, and the grants that go with them. Every
/// plugin test installs what it needs here rather than depending on anything the machine happens to have; the
/// one checked-in package is the canonical shape the documentation describes.
/// </summary>
public static class TestPlugins
{
    public const string FakeProviderId = "fake-provider";

    /// <summary>The checked-in fixture package, as it is copied next to the tests.</summary>
    public static string FakeProviderSource => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", "fake-provider");

    /// <summary>Where the stand-in vendor CLI's apphost lives, for a search path that has to find something real.</summary>
    public static string FakeCliDirectory => FakeProviderCli.Directory;

    /// <summary>Copies the checked-in package into the data directory and answers with its root.</summary>
    public static string InstallFakeProvider(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var root = paths.PluginPackageDirectory(FakeProviderId);
        Copy(FakeProviderSource, root);
        return root;
    }

    /// <summary>
    /// Writes a package: the manifest verbatim, <c>main.js</c>, and any local modules by relative path. Verbatim
    /// matters — a test about a manifest rule has to be able to write a manifest that breaks it.
    /// </summary>
    public static string Write(JasonPaths paths, string id, string manifest, string mainJs, IReadOnlyDictionary<string, string>? modules = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var root = paths.PluginPackageDirectory(id);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "plugin.yaml"), manifest, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "main.js"), mainJs, new UTF8Encoding(false));

        foreach (var (relativePath, source) in modules ?? new Dictionary<string, string>())
        {
            var file = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, source, new UTF8Encoding(false));
        }

        return root;
    }

    /// <summary>The smallest manifest that is valid, with room for the lines a test wants to add.</summary>
    public static string Manifest(string id, string operations = "[echo.run]", string extra = "")
    {
        var manifest = $"""
            manifest_version: 1
            id: {id}
            version: 1.0.0
            kind: provider
            contracts:
              protocol: [1]
              operations: [1]
            operations: {operations}

            """;
        return extra.Length == 0 ? manifest : manifest + extra.TrimEnd('\n') + "\n";
    }

    /// <summary>
    /// Grants a plugin what a test says it may use, merged into the settings file rather than written over it,
    /// so the dispatcher stays off and an earlier grant survives.
    /// </summary>
    public static void Grant(JasonPaths paths, string id, string[]? exec = null, string[]? http = null, string[]? env = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.ConfigDirectory);

        var settings = File.Exists(paths.UserSettingsFile)
            ? JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject()
            : JsonNode.Parse(RuntimeApiFixture.DispatcherOff)!.AsObject();

        var plugins = Child(settings, "Plugins");
        var grants = Child(plugins, "Grants");
        var grant = Child(grants, id);
        grant["Exec"] = Array(exec);
        grant["Http"] = Array(http);
        grant["Env"] = Array(env);

        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));

        static JsonObject Child(JsonObject parent, string name)
        {
            if (parent[name] is JsonObject existing)
            {
                return existing;
            }

            var created = new JsonObject();
            parent[name] = created;
            return created;
        }

        static JsonArray Array(string[]? values) => [.. (values ?? []).Select(value => (JsonNode)JsonValue.Create(value))];
    }

    /// <summary>The settings file as the configuration system will read it, for a test that wants to assert on it.</summary>
    public static JsonDocument ReadSettings(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return JsonDocument.Parse(File.ReadAllText(paths.UserSettingsFile));
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
