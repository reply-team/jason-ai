using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Manifest;

/// <summary>
/// Every rule a <c>plugin.yaml</c> is held to, applied to the plain JSON the document converted into. Each rule
/// appends its own problem and carries on, so an author who got three things wrong is told all three at once
/// rather than made to fix them one reload at a time.
/// </summary>
internal sealed partial class ManifestRules(JsonObject root, string directoryName, string packageRoot, ManifestBounds bounds)
{
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 1000;
    private const int MaxExecutables = 16;
    private const int MaxHosts = 32;
    private const int MaxVariables = 32;
    private const int MinTimeoutMs = 1000;
    private const int MinMemoryMb = 16;

    private static readonly string[] TopLevelFields =
    [
        "manifest_version", "id", "version", "kind", "name", "description", "homepage",
        "contracts", "operations", "entry", "capabilities", "limits", "binding",
    ];

    /// <summary>Where a schema problem is, spelled as the document and a pointer into it.</summary>
    private const string BindingField = "binding";

    private readonly List<ManifestProblem> _problems = [];

    public IReadOnlyList<ManifestProblem> Problems => _problems;

    /// <summary>
    /// Reads the whole document. The record it answers with is only meaningful when no problem was appended;
    /// the reader discards it otherwise, which is why the placeholders below never reach anyone.
    /// </summary>
    public PluginManifest Build()
    {
        UnknownKeys(root, string.Empty, TopLevelFields);
        ReadManifestVersion();
        var id = ReadId();
        var version = ReadVersion();
        var kind = ReadKind();

        return new PluginManifest(
            id ?? directoryName,
            version ?? "0.0.0",
            kind ?? PluginKind.Provider,
            ReadText("name", MaxNameLength),
            ReadText("description", MaxDescriptionLength),
            ReadHomepage(),
            ReadContracts(kind),
            ReadOperations(kind),
            ReadEntry(),
            ReadCapabilities(),
            ReadLimits(),
            ReadBinding());
    }

    /// <summary>
    /// The schema a route to this plugin must satisfy. It is optional, and adds nothing an older runtime would
    /// have to understand, so the manifest version stays where it is. The schema is held to the published dialect
    /// before anything will ever apply it: a keyword this runtime would silently ignore is a rule that does not
    /// run, and a pattern that cannot run in linear time is a stranger's chance to stall a reload.
    /// </summary>
    private JsonObject? ReadBinding()
    {
        if (!Has(root, BindingField))
        {
            return null;
        }

        if (root[BindingField] is not JsonObject binding)
        {
            Add(ProblemCodes.FieldInvalid, BindingField, "binding is a schema saying what a route to this plugin must carry.");
            return null;
        }

        var dialect = SchemaValidator.CheckDialect(binding);
        foreach (var problem in dialect)
        {
            Add(ProblemCodes.FieldInvalid, Locate(problem.Pointer), problem.Message);
        }

        // A route carries a mapping of named fields, so a schema of any other type could never describe one. The
        // check waits for the dialect, which would otherwise report the same malformed `type` twice — and it reads
        // the name the way every other rule here reads a value, because the dialect allows the list form of `type`
        // and a typed read of a list is a throw rather than an answer.
        if (dialect.Count == 0 && (!TryText(binding["type"], out var type) || type != "object"))
        {
            Add(ProblemCodes.FieldInvalid, BindingField, "binding is a schema of `type: object`, because a route carries a mapping of named fields.");
        }

        RefuseSecretLikeNames(binding, string.Empty, 0);
        return binding;
    }

    /// <summary>
    /// Refuses a declared field whose name reads like a credential, wherever in the schema it is declared: under
    /// <c>properties</c> at any depth, or named by <c>required</c> with no schema of its own. Both say the same
    /// thing to whoever writes the route — put a value of this name here — and that is the thing being refused.
    /// </summary>
    private void RefuseSecretLikeNames(JsonNode? node, string pointer, int depth)
    {
        if (depth > SchemaValidator.MaxDepth)
        {
            return;
        }

        switch (node)
        {
            case JsonObject map:
                foreach (var (key, value) in map)
                {
                    var at = Pointer(pointer, key);
                    if (key == "properties" && value is JsonObject declared)
                    {
                        foreach (var (name, _) in declared)
                        {
                            RefuseSecretLikeName(name, Pointer(at, name));
                        }
                    }
                    else if (key == "required" && value is JsonArray names)
                    {
                        for (var index = 0; index < names.Count; index++)
                        {
                            if (TryText(names[index], out var name))
                            {
                                RefuseSecretLikeName(name, Pointer(at, index.ToString(CultureInfo.InvariantCulture)));
                            }
                        }
                    }

                    RefuseSecretLikeNames(value, at, depth + 1);
                }

                break;

            case JsonArray branches:
                for (var index = 0; index < branches.Count; index++)
                {
                    RefuseSecretLikeNames(branches[index], Pointer(pointer, index.ToString(CultureInfo.InvariantCulture)), depth + 1);
                }

                break;

            default:
                break;
        }
    }

    private void RefuseSecretLikeName(string name, string pointer)
    {
        if (!SecretLikeNames.Matches(name))
        {
            return;
        }

        Add(
            ProblemCodes.BindingSecretLike,
            Locate(pointer),
            $"binding declares '{name}'. A binding selects an identity the plugin's own credential store already holds; it never carries the credential itself, so no field of a binding may be named like one.");
    }

    /// <summary>The manifest field and a pointer into the schema under it, the way a YAML error names line and column.</summary>
    private static string Locate(string pointer) => pointer.Length == 0 ? BindingField : BindingField + "#" + pointer;

    private static string Pointer(string parent, string segment) =>
        parent + "/" + segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private void ReadManifestVersion()
    {
        if (!Has(root, "manifest_version"))
        {
            Add(ProblemCodes.FieldRequired, "manifest_version", "manifest_version is required.");
            return;
        }

        if (!TryInteger(root["manifest_version"], out var declared) || declared != PluginProtocol.ManifestVersion)
        {
            Add(
                ProblemCodes.ManifestVersionUnsupported,
                "manifest_version",
                string.Create(CultureInfo.InvariantCulture, $"This runtime reads manifest_version {PluginProtocol.ManifestVersion}."));
        }
    }

    private string? ReadId()
    {
        if (!Has(root, "id"))
        {
            Add(ProblemCodes.FieldRequired, "id", "id is required.");
            return null;
        }

        if (!TryText(root["id"], out var id) || !Identifier().IsMatch(id))
        {
            Add(ProblemCodes.FieldInvalid, "id", "id is 2 to 64 characters of lowercase letters, digits and hyphens, starting with a letter.");
            return null;
        }

        // Only a well-formed id can be compared to the directory: two problems about one field help nobody.
        if (!string.Equals(id, directoryName, StringComparison.Ordinal))
        {
            Add(ProblemCodes.IdMismatch, "id", $"id must equal the package directory name '{directoryName}'.");
            return null;
        }

        return id;
    }

    private string? ReadVersion()
    {
        if (!Has(root, "version"))
        {
            Add(ProblemCodes.FieldRequired, "version", "version is required.");
            return null;
        }

        if (!TryText(root["version"], out var version) || !SemanticVersion().IsMatch(version))
        {
            Add(ProblemCodes.VersionInvalid, "version", "version is SemVer 2 (major.minor.patch with an optional prerelease), without build metadata.");
            return null;
        }

        return version;
    }

    private PluginKind? ReadKind()
    {
        if (!Has(root, "kind"))
        {
            Add(ProblemCodes.FieldRequired, "kind", "kind is required.");
            return null;
        }

        if (TryText(root["kind"], out var kind))
        {
            switch (kind)
            {
                case "provider":
                    return PluginKind.Provider;
                case "notification":
                    return PluginKind.Notification;
                default:
                    break;
            }
        }

        Add(ProblemCodes.KindInvalid, "kind", "kind is 'provider' or 'notification'.");
        return null;
    }

    private string? ReadText(string name, int maxLength)
    {
        if (!Has(root, name))
        {
            return null;
        }

        if (!TryText(root[name], out var text) || text.Length > maxLength)
        {
            Add(ProblemCodes.FieldInvalid, name, string.Create(CultureInfo.InvariantCulture, $"{name} is text of at most {maxLength} characters."));
            return null;
        }

        return text;
    }

    private string? ReadHomepage()
    {
        if (!Has(root, "homepage"))
        {
            return null;
        }

        if (!TryText(root["homepage"], out var homepage)
            || !Uri.TryCreate(homepage, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            Add(ProblemCodes.FieldInvalid, "homepage", "homepage is an absolute https URL.");
            return null;
        }

        return homepage;
    }

    private ManifestContracts ReadContracts(PluginKind? kind)
    {
        if (!Has(root, "contracts"))
        {
            Add(ProblemCodes.FieldRequired, "contracts", "contracts is required: it says which protocol and operation contracts the plugin speaks.");
            return new ManifestContracts([], []);
        }

        if (root["contracts"] is not JsonObject contracts)
        {
            Add(ProblemCodes.FieldInvalid, "contracts", "contracts is a mapping of version lists.");
            return new ManifestContracts([], []);
        }

        UnknownKeys(contracts, "contracts", ["protocol", "operations"]);
        var protocol = ReadVersions(contracts, "contracts", "protocol", required: true, PluginProtocol.CurrentVersion);

        // A notification plugin implements no canonical operation, so it declares no contract for them either.
        var operationsRequired = kind != PluginKind.Notification;
        var operations = ReadVersions(contracts, "contracts", "operations", operationsRequired, PluginProtocol.OperationContractVersion);
        return new ManifestContracts(protocol, operations);
    }

    private IReadOnlyList<int> ReadVersions(JsonObject parent, string parentPath, string name, bool required, int supported)
    {
        var path = Join(parentPath, name);
        if (!Has(parent, name))
        {
            if (required)
            {
                Add(ProblemCodes.FieldRequired, path, $"{path} is required.");
            }

            return [];
        }

        if (parent[name] is not JsonArray array || array.Count == 0)
        {
            Add(ProblemCodes.FieldInvalid, path, $"{path} is a non-empty list of version numbers.");
            return [];
        }

        var versions = new List<int>();
        var wellFormed = true;
        for (var index = 0; index < array.Count; index++)
        {
            if (TryInteger(array[index], out var version))
            {
                versions.Add(version);
            }
            else
            {
                Add(ProblemCodes.FieldInvalid, $"{path}[{index}]", "a contract version is a whole number.");
                wellFormed = false;
            }
        }

        if (wellFormed && !versions.Contains(supported))
        {
            Add(
                ProblemCodes.ContractUnsupported,
                path,
                string.Create(CultureInfo.InvariantCulture, $"This runtime speaks {name} version {supported}; the plugin declares none the runtime supports."));
        }

        return versions;
    }

    private IReadOnlyList<string> ReadOperations(PluginKind? kind)
    {
        if (kind == PluginKind.Notification)
        {
            if (root["operations"] is JsonArray { Count: > 0 })
            {
                Add(ProblemCodes.OperationsNotAllowed, "operations", "a notification plugin implements no canonical operations.");
            }

            return [];
        }

        if (!Has(root, "operations"))
        {
            Add(ProblemCodes.FieldRequired, "operations", "operations is required: a provider names the canonical operations it implements.");
            return [];
        }

        if (root["operations"] is not JsonArray array || array.Count == 0)
        {
            Add(ProblemCodes.FieldInvalid, "operations", "operations is a non-empty list of canonical operation names.");
            return [];
        }

        var operations = new List<string>();
        for (var index = 0; index < array.Count; index++)
        {
            var path = $"operations[{index}]";
            if (!TryText(array[index], out var operation) || !OperationName().IsMatch(operation))
            {
                Add(ProblemCodes.OperationInvalid, path, "an operation name is lowercase dotted, such as 'campaign.get'.");
            }
            else if (operations.Contains(operation, StringComparer.Ordinal))
            {
                Add(ProblemCodes.OperationDuplicate, path, $"operation '{operation}' is named twice.");
            }
            else
            {
                operations.Add(operation);
            }
        }

        return operations;
    }

    private PluginEntry ReadEntry()
    {
        var module = "main.js";
        var function = "invoke";

        if (Has(root, "entry"))
        {
            if (root["entry"] is not JsonObject entry)
            {
                Add(ProblemCodes.FieldInvalid, "entry", "entry is a mapping of module and function.");
                return new PluginEntry(module, function);
            }

            UnknownKeys(entry, "entry", ["module", "function"]);
            if (Has(entry, "module"))
            {
                if (TryText(entry["module"], out var declared))
                {
                    module = declared;
                }
                else
                {
                    Add(ProblemCodes.FieldInvalid, "entry.module", "entry.module is a path inside the package.");
                    return new PluginEntry(module, function);
                }
            }

            if (Has(entry, "function"))
            {
                if (TryText(entry["function"], out var declared))
                {
                    function = declared;
                }
                else
                {
                    Add(ProblemCodes.EntryFunctionInvalid, "entry.function", "entry.function is the name of an exported function.");
                    return new PluginEntry(module, function);
                }
            }
        }

        ValidateEntryModule(module);
        if (!FunctionName().IsMatch(function))
        {
            Add(ProblemCodes.EntryFunctionInvalid, "entry.function", "entry.function is a JavaScript identifier.");
        }

        return new PluginEntry(module, function);
    }

    private void ValidateEntryModule(string module)
    {
        if (!module.EndsWith(".js", StringComparison.Ordinal) && !module.EndsWith(".mjs", StringComparison.Ordinal))
        {
            Add(ProblemCodes.FieldInvalid, "entry.module", "entry.module is a .js or .mjs file inside the package.");
            return;
        }

        if (module.Split('/', '\\').Contains("..", StringComparer.Ordinal))
        {
            Add(ProblemCodes.FieldInvalid, "entry.module", "entry.module is a path inside the package, without '..' segments.");
            return;
        }

        string full;
        var enclosing = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot)) + Path.DirectorySeparatorChar;
        try
        {
            full = Path.GetFullPath(Path.Combine(packageRoot, module.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Add(ProblemCodes.FieldInvalid, "entry.module", "entry.module is not a usable path.");
            return;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(enclosing, comparison))
        {
            Add(ProblemCodes.EntryModuleOutsidePackage, "entry.module", "entry.module resolves outside the package.");
            return;
        }

        if (!File.Exists(full))
        {
            Add(ProblemCodes.EntryModuleMissing, "entry.module", $"entry.module '{module}' is not in the package.");
        }
    }

    private CapabilityRequests ReadCapabilities()
    {
        if (!Has(root, "capabilities"))
        {
            return new CapabilityRequests(null, null, null);
        }

        if (root["capabilities"] is not JsonObject capabilities)
        {
            Add(ProblemCodes.FieldInvalid, "capabilities", "capabilities is a mapping of exec, http and env.");
            return new CapabilityRequests(null, null, null);
        }

        UnknownKeys(capabilities, "capabilities", ["exec", "http", "env"]);
        return new CapabilityRequests(ReadExec(capabilities), ReadHttp(capabilities), ReadEnv(capabilities));
    }

    private ExecRequestSpec? ReadExec(JsonObject capabilities)
    {
        if (!Has(capabilities, "exec"))
        {
            return null;
        }

        if (capabilities["exec"] is not JsonObject exec)
        {
            Add(ProblemCodes.FieldInvalid, "capabilities.exec", "capabilities.exec is a mapping holding executables.");
            return null;
        }

        UnknownKeys(exec, "capabilities.exec", ["executables"]);
        const string path = "capabilities.exec.executables";
        if (!Has(exec, "executables"))
        {
            Add(ProblemCodes.FieldRequired, path, $"{path} is required inside capabilities.exec.");
            return null;
        }

        if (exec["executables"] is not JsonArray array || array.Count == 0 || array.Count > MaxExecutables)
        {
            Add(ProblemCodes.FieldInvalid, path, string.Create(CultureInfo.InvariantCulture, $"{path} names 1 to {MaxExecutables} programs."));
            return null;
        }

        var requests = new List<ExecutableRequest>();
        for (var index = 0; index < array.Count; index++)
        {
            var itemPath = $"{path}[{index}]";
            if (array[index] is not JsonObject item)
            {
                Add(ProblemCodes.FieldInvalid, itemPath, "an executable is a mapping with a name.");
                continue;
            }

            UnknownKeys(item, itemPath, ["name", "min_version", "version_command"]);
            var name = ReadExecutableName(item, itemPath, requests);
            var command = ReadVersionCommand(item, itemPath);
            var minimum = ReadMinVersion(item, itemPath, command);
            if (name is not null)
            {
                requests.Add(new ExecutableRequest(name, minimum, command));
            }
        }

        return new ExecRequestSpec(requests);
    }

    private string? ReadExecutableName(JsonObject item, string itemPath, List<ExecutableRequest> declared)
    {
        var path = itemPath + ".name";
        if (!Has(item, "name"))
        {
            Add(ProblemCodes.FieldRequired, path, "an executable needs a name.");
            return null;
        }

        if (!TryText(item["name"], out var name) || !ExecutableName().IsMatch(name))
        {
            Add(ProblemCodes.FieldInvalid, path, "an executable name is a bare file name without separators or an extension.");
            return null;
        }

        if (declared.Any(request => string.Equals(request.Name, name, StringComparison.Ordinal)))
        {
            Add(ProblemCodes.FieldInvalid, path, $"executable '{name}' is declared twice.");
            return null;
        }

        return name;
    }

    /// <summary>
    /// A reload runs this on the user's machine, so the manifest chooses from a fixed list rather than naming
    /// arguments of its own: anything else would make a version check a way to run anything.
    /// </summary>
    private IReadOnlyList<string>? ReadVersionCommand(JsonObject item, string itemPath)
    {
        var path = itemPath + ".version_command";
        if (!Has(item, "version_command"))
        {
            return null;
        }

        if (item["version_command"] is JsonArray { Count: 1 } array
            && TryText(array[0], out var command)
            && PluginProtocol.AllowedVersionCommands.Contains(command, StringComparer.Ordinal))
        {
            return [command];
        }

        Add(
            ProblemCodes.FieldInvalid,
            path,
            $"version_command is one of {string.Join(", ", PluginProtocol.AllowedVersionCommands.Select(allowed => $"[\"{allowed}\"]"))}.");
        return null;
    }

    private string? ReadMinVersion(JsonObject item, string itemPath, IReadOnlyList<string>? command)
    {
        var path = itemPath + ".min_version";
        if (!Has(item, "min_version"))
        {
            return null;
        }

        if (!TryText(item["min_version"], out var minimum) || !VersionCore().IsMatch(minimum))
        {
            Add(ProblemCodes.FieldInvalid, path, "min_version is major.minor.patch.");
            return null;
        }

        if (command is null)
        {
            Add(ProblemCodes.FieldInvalid, path, "min_version needs a version_command: without one there is nothing to compare.");
            return null;
        }

        return minimum;
    }

    private HttpRequestSpec? ReadHttp(JsonObject capabilities)
    {
        if (!Has(capabilities, "http"))
        {
            return null;
        }

        if (capabilities["http"] is not JsonObject http)
        {
            Add(ProblemCodes.FieldInvalid, "capabilities.http", "capabilities.http is a mapping holding hosts.");
            return null;
        }

        UnknownKeys(http, "capabilities.http", ["hosts"]);
        const string path = "capabilities.http.hosts";
        if (!Has(http, "hosts"))
        {
            Add(ProblemCodes.FieldRequired, path, $"{path} is required inside capabilities.http.");
            return null;
        }

        if (http["hosts"] is not JsonArray array || array.Count == 0 || array.Count > MaxHosts)
        {
            Add(ProblemCodes.FieldInvalid, path, string.Create(CultureInfo.InvariantCulture, $"{path} names 1 to {MaxHosts} hosts."));
            return null;
        }

        var hosts = new List<string>();
        for (var index = 0; index < array.Count; index++)
        {
            if (!TryText(array[index], out var host) || !IsAllowedHost(host))
            {
                Add(ProblemCodes.HostInvalid, $"{path}[{index}]", "a host is a lowercase name with an optional port, matched exactly: no scheme, no path, no wildcards.");
            }
            else if (hosts.Contains(host, StringComparer.Ordinal))
            {
                Add(ProblemCodes.HostInvalid, $"{path}[{index}]", $"host '{host}' is named twice.");
            }
            else
            {
                hosts.Add(host);
            }
        }

        return new HttpRequestSpec(hosts);
    }

    private static bool IsAllowedHost(string host)
    {
        if (!HostName().IsMatch(host))
        {
            return false;
        }

        var colon = host.LastIndexOf(':');
        return colon < 0 || (int.TryParse(host.AsSpan(colon + 1), CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535);
    }

    private EnvRequestSpec? ReadEnv(JsonObject capabilities)
    {
        if (!Has(capabilities, "env"))
        {
            return null;
        }

        if (capabilities["env"] is not JsonObject env)
        {
            Add(ProblemCodes.FieldInvalid, "capabilities.env", "capabilities.env is a mapping holding variables.");
            return null;
        }

        UnknownKeys(env, "capabilities.env", ["variables"]);
        const string path = "capabilities.env.variables";
        if (!Has(env, "variables"))
        {
            Add(ProblemCodes.FieldRequired, path, $"{path} is required inside capabilities.env.");
            return null;
        }

        if (env["variables"] is not JsonArray array || array.Count == 0 || array.Count > MaxVariables)
        {
            Add(ProblemCodes.FieldInvalid, path, string.Create(CultureInfo.InvariantCulture, $"{path} names 1 to {MaxVariables} variables."));
            return null;
        }

        var variables = new List<string>();
        for (var index = 0; index < array.Count; index++)
        {
            var itemPath = $"{path}[{index}]";
            if (!TryText(array[index], out var variable) || !VariableName().IsMatch(variable))
            {
                Add(ProblemCodes.VariableNameInvalid, itemPath, "a variable name is uppercase letters, digits and underscores, starting with a letter.");
            }
            else if (variable.StartsWith(BaseEnvironment.ReservedPrefix, StringComparison.Ordinal))
            {
                Add(ProblemCodes.VariableReserved, itemPath, $"variables starting with '{BaseEnvironment.ReservedPrefix}' belong to the runtime and never reach a plugin.");
            }
            else if (!BaseEnvironment.MayAPluginSet(variable))
            {
                // The same list `host.exec` holds a plugin to. Being granted a name and setting one end in the
                // same place: a granted variable is copied out of the runtime's own environment into every child
                // the plugin starts, so a loader or interpreter hook granted here chooses the code that runs
                // inside a program the user allowed — which is not the permission the user gave.
                Add(
                    ProblemCodes.FieldInvalid,
                    itemPath,
                    $"'{variable}' decides what a program loads before its own first line runs, so it is neither a "
                        + "variable a plugin may set nor one a plugin may be granted.");
            }
            else if (variables.Contains(variable, StringComparer.Ordinal))
            {
                Add(ProblemCodes.VariableNameInvalid, itemPath, $"variable '{variable}' is named twice.");
            }
            else
            {
                variables.Add(variable);
            }
        }

        return new EnvRequestSpec(variables);
    }

    private ManifestLimits ReadLimits()
    {
        if (!Has(root, "limits"))
        {
            return new ManifestLimits(null, null);
        }

        if (root["limits"] is not JsonObject limits)
        {
            Add(ProblemCodes.FieldInvalid, "limits", "limits is a mapping of timeout_ms and memory_mb.");
            return new ManifestLimits(null, null);
        }

        UnknownKeys(limits, "limits", ["timeout_ms", "memory_mb"]);
        return new ManifestLimits(
            ReadBounded(limits, "limits", "timeout_ms", MinTimeoutMs, bounds.MaxTimeoutMs),
            ReadBounded(limits, "limits", "memory_mb", MinMemoryMb, bounds.MaxMemoryMb));
    }

    private int? ReadBounded(JsonObject parent, string parentPath, string name, int minimum, int maximum)
    {
        var path = Join(parentPath, name);
        if (!Has(parent, name))
        {
            return null;
        }

        if (!TryInteger(parent[name], out var value) || value < minimum || value > maximum)
        {
            Add(ProblemCodes.FieldInvalid, path, string.Create(CultureInfo.InvariantCulture, $"{path} is a whole number between {minimum} and {maximum} on this installation."));
            return null;
        }

        return value;
    }

    private void UnknownKeys(JsonObject obj, string path, params string[] allowed)
    {
        foreach (var (key, _) in obj)
        {
            if (!allowed.Contains(key, StringComparer.Ordinal))
            {
                // A typo is the common failure, and forward compatibility arrives with a new manifest_version.
                Add(ProblemCodes.UnknownField, Join(path, key), $"'{key}' is not a field a manifest of this version declares.");
            }
        }
    }

    private void Add(string code, string path, string message) => _problems.Add(new ManifestProblem(code, path, message));

    private static string Join(string parent, string name) => parent.Length == 0 ? name : parent + "." + name;

    private static bool Has(JsonObject parent, string name) => parent.TryGetPropertyValue(name, out var node) && node is not null;

    private static bool TryText(JsonNode? node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryInteger(JsonNode? node, out int value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<long>(out var whole) && whole is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)whole;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Whether text could name a plugin — the rule a manifest's own <c>id</c> is held to, spelled once so that a
    /// route naming a plugin and a package declaring one are measured against the same sentence.
    /// </summary>
    internal static bool IsPluginId(string? text) => text is not null && Identifier().IsMatch(text);

    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemanticVersion();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex VersionCore();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex OperationName();

    [GeneratedRegex(@"^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex FunctionName();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex ExecutableName();

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*(:([1-9]\d{0,4}))?$")]
    private static partial Regex HostName();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$")]
    private static partial Regex VariableName();
}
