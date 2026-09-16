using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// Plugin packages written into an isolated data directory on the fly, and the grants that go with them. Every
/// plugin test installs what it needs here rather than depending on anything the machine happens to have; the
/// one checked-in package is the canonical shape the documentation describes.
/// </summary>
public static class TestPlugins
{
    public const string FakeProviderId = "fake-provider";

    /// <summary>The second checked-in package: a provider that implements only some of what the first does.</summary>
    public const string OtherProviderId = "other-provider";

    /// <summary>The checked-in fixture package, as it is copied next to the tests.</summary>
    public static string FakeProviderSource => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", "fake-provider");

    public static string OtherProviderSource => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", "other-provider");

    /// <summary>Where the stand-in vendor CLI's apphost lives, for a search path that has to find something real.</summary>
    public static string FakeCliDirectory => FakeProviderCli.Directory;

    /// <summary>
    /// The machine as a test that loads the checked-in package needs to see it: the stand-in vendor CLI's own
    /// directory, then the real search path. The package declares that program by its own name — the way a
    /// plugin declares a vendor CLI — and it is installed beside the tests rather than onto the machine, so the
    /// runtime has to be told where to look. Everything else on the machine is still findable, because the real
    /// path follows.
    /// </summary>
    public static ISearchPath SearchPath { get; } = new TestSearchPath
    {
        Path = FakeProviderCli.Directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        PathExt = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATHEXT") : null,
    };

    /// <summary>Copies the checked-in package into the data directory and answers with its root.</summary>
    public static string InstallFakeProvider(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var root = paths.PluginPackageDirectory(FakeProviderId);
        Copy(FakeProviderSource, root);
        return root;
    }

    /// <summary>
    /// Copies the second checked-in package in, for a test that needs two providers to choose between: it
    /// implements fewer operations than the first, asks for no capability at all, and answers from its input.
    /// </summary>
    public static string InstallOtherProvider(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var root = paths.PluginPackageDirectory(OtherProviderId);
        Copy(OtherProviderSource, root);
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

    /// <summary>
    /// Manifest fragments a reader once threw over rather than reported. Both are a stranger's package doing
    /// something the rules have an answer for, and both reached a typed read that was not prepared for the shape
    /// in front of it: a bound too large for any JSON writer, and a `type` written as the list the dialect allows.
    /// They are kept together because what must be true of them is one thing — no manifest, however malformed,
    /// makes the reader throw — and the tests that hold the reload and the startup load to it share them.
    /// </summary>
    public const string BindingBoundTooLarge =
        "binding:\n  type: object\n  properties:\n    workspace:\n      type: number\n      maximum: 1e400\n";

    public const string BindingTypeAsAList =
        "binding:\n  type: [object, \"null\"]\n  properties:\n    workspace:\n      type: string\n";

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
