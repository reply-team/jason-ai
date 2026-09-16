using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// Every rule a <c>plugin.yaml</c> is held to, and the code each broken rule answers with. The vocabulary is the
/// contract: it is what an author reads in <c>plugin.list</c> and what the documentation describes, so a rule
/// without its own code would be a rule nobody can act on.
/// </summary>
public class ManifestReaderTests
{
    private static readonly ManifestBounds Bounds = new(3_600_000, 512);

    [Fact]
    public void The_smallest_manifest_that_says_everything_required_is_valid()
    {
        var result = Read(TestPlugins.Manifest("fake"));

        Assert.True(result.IsValid);
        var manifest = result.Manifest!;
        Assert.Equal("fake", manifest.Id);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal(PluginKind.Provider, manifest.Kind);
        Assert.Equal([1], manifest.Contracts.Protocol);
        Assert.Equal([1], manifest.Contracts.Operations);
        Assert.Equal(["echo.run"], manifest.Operations);
        Assert.Equal(new PluginEntry("main.js", "invoke"), manifest.Entry);
        Assert.Null(manifest.Capabilities.Exec);
        Assert.Null(manifest.Capabilities.Http);
        Assert.Null(manifest.Capabilities.Env);
        Assert.Null(manifest.Limits.TimeoutMs);
        Assert.Null(manifest.Limits.MemoryMb);
    }

    [Fact]
    public void The_checked_in_package_is_valid_as_it_is_shipped()
    {
        using var dir = new TempDataDir();
        var root = TestPlugins.InstallFakeProvider(dir.Paths);

        var result = ManifestReader.Read(File.ReadAllText(Path.Combine(root, "plugin.yaml")), TestPlugins.FakeProviderId, root, Bounds);

        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => $"{p.Path}: {p.Code}")));
        Assert.Equal("Fake provider", result.Manifest!.Name);
        Assert.Equal(["dotnet"], result.Manifest.Capabilities.Exec!.Executables.Select(e => e.Name));
        Assert.Equal(["localhost:5555", "127.0.0.1:5555"], result.Manifest.Capabilities.Http!.Hosts);
        Assert.Equal(["FAKE_TOKEN", "FAKE_OTHER", "FAKE_CLI_DLL"], result.Manifest.Capabilities.Env!.Variables);
        Assert.Equal(20_000, result.Manifest.Limits.TimeoutMs);
        Assert.Equal(64, result.Manifest.Limits.MemoryMb);
    }

    [Theory]

    // The document itself.
    [InlineData("manifest_version: 1\nid: fake\n", "field_required", "version")]
    [InlineData("id: fake\nversion: 1.0.0\nkind: provider\n", "field_required", "manifest_version")]
    [InlineData("manifest_version: 2\nid: fake\nversion: 1.0.0\nkind: provider\n", "manifest_version_unsupported", "manifest_version")]
    [InlineData("manifest_version: one\nid: fake\nversion: 1.0.0\nkind: provider\n", "manifest_version_unsupported", "manifest_version")]

    // Identity.
    [InlineData("id: Fake\n", "field_invalid", "id")]
    [InlineData("id: a\n", "field_invalid", "id")]
    [InlineData("version: 1.0\n", "version_invalid", "version")]
    [InlineData("version: 1.0.0+build\n", "version_invalid", "version")]
    [InlineData("version: 1.0.0-rc.1\n", null, null)]
    [InlineData("kind: bogus\n", "kind_invalid", "kind")]
    [InlineData("kind:\n", "field_required", "kind")]

    // Free text and links.
    [InlineData("homepage: http://example.test\n", "field_invalid", "homepage")]
    [InlineData("homepage: not-a-url\n", "field_invalid", "homepage")]
    [InlineData("homepage: https://example.test/plugin\n", null, null)]

    // Contracts.
    [InlineData("contracts:\n  protocol: [2]\n  operations: [1]\n", "contract_unsupported", "contracts.protocol")]
    [InlineData("contracts:\n  protocol: [1]\n  operations: [2]\n", "contract_unsupported", "contracts.operations")]
    [InlineData("contracts:\n  protocol: []\n  operations: [1]\n", "field_invalid", "contracts.protocol")]
    [InlineData("contracts:\n  protocol: [one]\n  operations: [1]\n", "field_invalid", "contracts.protocol[0]")]
    [InlineData("contracts:\n  protocol: [1]\n", "field_required", "contracts.operations")]

    // Operations.
    [InlineData("operations: [Echo.Run]\n", "operation_invalid", "operations[0]")]
    [InlineData("operations: [echo]\n", "operation_invalid", "operations[0]")]
    [InlineData("operations: [echo.run, echo.run]\n", "operation_duplicate", "operations[1]")]
    [InlineData("operations: []\n", "field_invalid", "operations")]

    // Entry.
    [InlineData("entry:\n  module: ../main.js\n", "field_invalid", "entry.module")]
    [InlineData("entry:\n  module: main.txt\n", "field_invalid", "entry.module")]
    [InlineData("entry:\n  module: /main.js\n", "entry_module_outside_package", "entry.module")]
    [InlineData("entry:\n  module: missing.js\n", "entry_module_missing", "entry.module")]
    [InlineData("entry:\n  function: 1bad\n", "entry_function_invalid", "entry.function")]
    [InlineData("entry:\n  module: main.js\n  function: run\n", null, null)]

    // Executables (amendment 2 lives here).
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: bad/name\n", "field_invalid", "capabilities.exec.executables[0].name")]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n      - name: provider-cli\n", "field_invalid", "capabilities.exec.executables[1].name")]
    [InlineData("capabilities:\n  exec:\n    executables: []\n", "field_invalid", "capabilities.exec.executables")]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        min_version: 0.4.0\n", "field_invalid", "capabilities.exec.executables[0].min_version")]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        min_version: 0.4\n        version_command: [\"--version\"]\n", "field_invalid", "capabilities.exec.executables[0].min_version")]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        version_command: [\"--version\", \"--force\"]\n", "field_invalid", "capabilities.exec.executables[0].version_command")]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        version_command: [\"rm\", \"-rf\"]\n", "field_invalid", "capabilities.exec.executables[0].version_command")]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        version_command: [\"--help\"]\n", "field_invalid", "capabilities.exec.executables[0].version_command")]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        min_version: 0.4.0\n        version_command: [\"--version\"]\n", null, null)]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        version_command: [\"-v\"]\n", null, null)]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        version_command: [\"-V\"]\n", null, null)]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        version_command: [\"version\"]\n", null, null)]

    // Hosts.
    [InlineData("capabilities:\n  http:\n    hosts: [\"*.example.test\"]\n", "host_invalid", "capabilities.http.hosts[0]")]
    [InlineData("capabilities:\n  http:\n    hosts: [\"API.example.test\"]\n", "host_invalid", "capabilities.http.hosts[0]")]
    [InlineData("capabilities:\n  http:\n    hosts: [\"localhost:99999\"]\n", "host_invalid", "capabilities.http.hosts[0]")]
    [InlineData("capabilities:\n  http:\n    hosts: [\"https://api.example.test\"]\n", "host_invalid", "capabilities.http.hosts[0]")]
    [InlineData("capabilities:\n  http:\n    hosts: [\"api.example.test\", \"api.example.test\"]\n", "host_invalid", "capabilities.http.hosts[1]")]
    [InlineData("capabilities:\n  http:\n    hosts: [\"api.example.test\", \"localhost:8080\"]\n", null, null)]

    // Variables.
    [InlineData("capabilities:\n  env:\n    variables: [lower]\n", "variable_name_invalid", "capabilities.env.variables[0]")]
    [InlineData("capabilities:\n  env:\n    variables: [JASON_DATA_DIR]\n", "variable_reserved", "capabilities.env.variables[0]")]
    [InlineData("capabilities:\n  env:\n    variables: [TOKEN, TOKEN]\n", "variable_name_invalid", "capabilities.env.variables[1]")]
    [InlineData("capabilities:\n  env:\n    variables: [TOKEN]\n", null, null)]

    // Limits.
    [InlineData("limits:\n  timeout_ms: 100\n", "field_invalid", "limits.timeout_ms")]
    [InlineData("limits:\n  timeout_ms: 99999999\n", "field_invalid", "limits.timeout_ms")]
    [InlineData("limits:\n  memory_mb: 2\n", "field_invalid", "limits.memory_mb")]
    [InlineData("limits:\n  memory_mb: 2048\n", "field_invalid", "limits.memory_mb")]
    [InlineData("limits:\n  timeout_ms: 5000\n  memory_mb: 128\n", null, null)]

    // The binding schema: optional, additive, and held to the published dialect.
    [InlineData("binding: workspace\n", "field_invalid", "binding")]
    [InlineData("binding:\n  type: string\n", "field_invalid", "binding")]
    [InlineData("binding:\n  type: object\n  unevaluatedProperties: false\n", "field_invalid", "binding#/unevaluatedProperties")]
    [InlineData("binding:\n  type: object\n  properties:\n    workspace:\n      type: string\n      pattern: \"(?<=a)b\"\n", "field_invalid", "binding#/properties/workspace/pattern")]
    [InlineData("binding:\n  type: object\n  properties:\n    api_token:\n      type: string\n", "binding_secret_like", "binding#/properties/api_token")]
    // A binding is a schema of `type: object` exactly. The list form is good JSON Schema and good dialect, so the
    // dialect check passes it through to this rule — which read the name with an accessor that throws on a list.
    [InlineData("binding:\n  type: [object, \"null\"]\n  properties:\n    workspace:\n      type: string\n", "field_invalid", "binding")]
    [InlineData("binding:\n  type: [object]\n", "field_invalid", "binding")]
    [InlineData("binding:\n  properties:\n    workspace:\n      type: string\n", "field_invalid", "binding")]
    [InlineData("binding:\n  type: object\n  properties:\n    workspace:\n      type: string\n", null, null)]

    // Typos are the common failure, so an unknown key is an error wherever it sits.
    [InlineData("capabilites:\n  env:\n    variables: [TOKEN]\n", "unknown_field", "capabilites")]
    [InlineData("entry:\n  modules: main.js\n", "unknown_field", "entry.modules")]
    [InlineData("capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        minversion: 1.0.0\n", "unknown_field", "capabilities.exec.executables[0].minversion")]
    [InlineData("capabilities:\n  net:\n    hosts: [a.test]\n", "unknown_field", "capabilities.net")]
    public void A_rule_answers_with_its_own_code_at_its_own_path(string manifest, string? code, string? path)
    {
        // A fragment that redefines a required field replaces it; anything else is appended to the base manifest.
        var result = Read(Merge(manifest));

        if (code is null)
        {
            Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => $"{p.Path}: {p.Code}")));
            return;
        }

        Assert.Null(result.Manifest);
        var problem = Assert.Single(result.Problems, p => p.Code == code && p.Path == path);
        Assert.NotEmpty(problem.Message);
    }

    [Fact]
    public void A_binding_schema_says_what_a_route_to_this_plugin_must_carry()
    {
        var result = Read(Merge("""
            binding:
              type: object
              additionalProperties: false
              required: [workspace]
              properties:
                workspace:
                  type: string
                  minLength: 1

            """));

        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => $"{p.Path}: {p.Code}")));
        var binding = result.Manifest!.Binding;
        Assert.NotNull(binding);
        Assert.Equal("object", binding["type"]!.GetValue<string>());
        Assert.Equal("string", binding["properties"]!["workspace"]!["type"]!.GetValue<string>());

        // The key is optional and adds nothing an older runtime has to understand, so the version does not move.
        Assert.Equal(1, PluginProtocol.ManifestVersion);
    }

    [Fact]
    public void A_manifest_without_a_binding_asks_a_route_for_nothing()
    {
        var result = Read(TestPlugins.Manifest("fake"));

        Assert.True(result.IsValid);
        Assert.Null(result.Manifest!.Binding);
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("client_secret")]
    [InlineData("Password")]
    [InlineData("passwd")]
    [InlineData("api_key")]
    [InlineData("apikey")]
    [InlineData("credentials")]
    [InlineData("private_key")]
    [InlineData("authorization")]
    [InlineData("bearer_jwt")]
    [InlineData("cookie")]
    public void A_binding_may_not_declare_a_property_whose_name_reads_like_a_credential(string property)
    {
        var result = Read(Merge($"binding:\n  type: object\n  properties:\n    {property}:\n      type: string\n"));

        var problem = Assert.Single(result.Problems);
        Assert.Equal("binding_secret_like", problem.Code);
        Assert.Equal($"binding#/properties/{property}", problem.Path);
        Assert.Contains(property, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_credential_rule_reaches_every_depth_and_a_name_that_is_only_required()
    {
        // A nested definition and a bare `required` entry are both ways of saying a route must carry a field,
        // so neither is a place the rule can be walked around.
        var result = Read(Merge("""
            binding:
              type: object
              properties:
                account:
                  $ref: "#/$defs/account"
              required: [api_key]
              $defs:
                account:
                  type: object
                  properties:
                    secret_name:
                      type: string

            """));

        Assert.Equal(
            ["binding#/$defs/account/properties/secret_name", "binding#/required/0"],
            result.Problems.Where(p => p.Code == "binding_secret_like").Select(p => p.Path).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The grant and the denylist are one rule. A plugin may not <c>set</c> a loader or interpreter hook for a
    /// program it starts; being <c>handed</c> one reaches the same child through a different door, because a
    /// granted name is copied out of the runtime's own environment into every child that plugin starts.
    /// </summary>
    [Theory]
    [InlineData("LD_PRELOAD")]
    [InlineData("LD_LIBRARY_PATH")]
    [InlineData("DYLD_INSERT_LIBRARIES")]
    [InlineData("NODE_OPTIONS")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("PYTHONPATH")]
    [InlineData("JAVA_TOOL_OPTIONS")]
    [InlineData("PATH")]
    public void A_plugin_may_not_be_granted_a_variable_it_may_not_set(string name)
    {
        var result = Read(Merge($"capabilities:\n  env:\n    variables: [{name}]\n"));

        var problem = Assert.Single(result.Problems);
        Assert.Equal("field_invalid", problem.Code);
        Assert.Equal("capabilities.env.variables[0]", problem.Path);
        Assert.Contains(name, problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name ending in a line break. `$` matches before a trailing one, so the name rule accepted it and the
    /// denylist — which compares whole names — did not recognise it: the anchor is `\z`. The escape is written
    /// out so that the string the rule is handed really ends in a line break.
    /// </summary>
    [Theory]
    [InlineData("TOKEN")]
    [InlineData("NODE_OPTIONS")]
    public void A_variable_name_with_a_line_break_after_it_is_not_a_name(string stem)
    {
        var result = Read(Merge($"capabilities:\n  env:\n    variables: [\"{stem}\\n\"]\n"));

        var problem = Assert.Single(result.Problems);
        Assert.Equal("variable_name_invalid", problem.Code);
        Assert.Equal("capabilities.env.variables[0]", problem.Path);
    }

    [Fact]
    public void A_variable_of_a_runtime_that_has_hooks_is_still_a_plugin_s_to_be_granted()
    {
        var result = Read(Merge("capabilities:\n  env:\n    variables: [NODE_ENV, DOTNET_NOLOGO]\n"));

        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => $"{p.Path}: {p.Code}")));
        Assert.Equal(["NODE_ENV", "DOTNET_NOLOGO"], result.Manifest!.Capabilities.Env!.Variables);
    }

    /// <summary>
    /// The credential rule walks the schema's own nodes, so its budget has to be the budget of the document it
    /// walks. It was measured against the dialect's nesting limit instead, which counts schema levels — about
    /// two nodes each — so the scan would have stopped around halfway down a schema the dialect accepts. Nothing
    /// could reach that today only because the manifest reader stops a deeper document first: one cap covering a
    /// different cap's mistake. The schema is built here rather than written out, and it is as deep as a manifest
    /// can carry, so a later change to the nesting limit widens the scan with it rather than opening a gap.
    /// </summary>
    [Fact]
    public void The_credential_rule_reaches_the_bottom_of_the_deepest_binding_a_manifest_can_carry()
    {
        var (fragment, pointer) = DeepBinding("client_secret");

        var result = Read(Merge(fragment));

        var problem = Assert.Single(result.Problems);
        Assert.Equal("binding_secret_like", problem.Code);
        Assert.Equal(pointer, problem.Path);
    }

    /// <summary>
    /// A binding nested as deep as the reader will take it, with a credential-shaped property at the bottom. The
    /// document node is level 1, `binding` is 2 and its `properties` is 3; every schema level after that costs
    /// two nodes — `properties`, then the name under it — and the `type` scalar at the bottom costs one more. So
    /// the levels are counted from the reader's own limit rather than guessed at.
    /// </summary>
    private static (string Fragment, string Pointer) DeepBinding(string leaf)
    {
        var levels = (YamlToJson.MaxDepth - 3) / 2;
        var yaml = new System.Text.StringBuilder("binding:\n  type: object\n");
        var pointer = new System.Text.StringBuilder("binding#");
        var indent = 2;

        for (var level = 0; level < levels; level++)
        {
            var name = level == levels - 1 ? leaf : "step" + level.ToString(System.Globalization.CultureInfo.InvariantCulture);
            yaml.Append(' ', indent).Append("properties:\n");
            yaml.Append(' ', indent + 2).Append(name).Append(":\n");
            yaml.Append(' ', indent + 4).Append("type: object\n");
            pointer.Append("/properties/").Append(name);
            indent += 4;
        }

        return (yaml.ToString(), pointer.ToString());
    }

    [Fact]
    public void A_manifest_in_the_wrong_directory_is_not_that_plugin()
    {
        var result = Read(TestPlugins.Manifest("other"), directoryName: "other");
        Assert.True(result.IsValid);

        var mismatch = ManifestReader.Read(TestPlugins.Manifest("other"), "fake", NewPackage(TestPlugins.Manifest("other"), "fake"), Bounds);

        Assert.Equal("id_mismatch", Assert.Single(mismatch.Problems).Code);
        Assert.Equal("id", Assert.Single(mismatch.Problems).Path);
    }

    [Fact]
    public void Every_problem_is_reported_at_once_rather_than_the_first()
    {
        var result = Read("""
            manifest_version: 1
            id: Bad
            version: 1.0
            kind: bogus
            contracts:
              protocol: [1]
              operations: [1]
            operations: [echo.run]
            """);

        Assert.Null(result.Manifest);
        Assert.Equal(
            ["field_invalid", "kind_invalid", "version_invalid"],
            result.Problems.Select(p => p.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_notification_plugin_declares_no_operations()
    {
        var valid = Read("""
            manifest_version: 1
            id: fake
            version: 1.0.0
            kind: notification
            contracts:
              protocol: [1]
            """);

        Assert.True(valid.IsValid, string.Join("; ", valid.Problems.Select(p => $"{p.Path}: {p.Code}: {p.Message}")));
        Assert.Equal(PluginKind.Notification, valid.Manifest!.Kind);
        Assert.Empty(valid.Manifest.Operations);
        Assert.Empty(valid.Manifest.Contracts.Operations);

        var refused = Read("""
            manifest_version: 1
            id: fake
            version: 1.0.0
            kind: notification
            contracts:
              protocol: [1]
            operations: [notify.send]
            """);

        Assert.Equal("operations_not_allowed", Assert.Single(refused.Problems).Code);
        Assert.Equal("operations", Assert.Single(refused.Problems).Path);
    }

    [Fact]
    public void A_document_that_is_not_a_mapping_of_fields_is_not_a_manifest()
    {
        var result = Read("- one\n- two\n");

        Assert.Equal("yaml_invalid", Assert.Single(result.Problems).Code);
    }

    [Fact]
    public void Broken_yaml_is_reported_where_it_broke()
    {
        var result = Read("manifest_version: 1\nid: [x\n");

        var problem = Assert.Single(result.Problems);
        Assert.Equal("yaml_invalid", problem.Code);
        Assert.Matches(@"^plugin\.yaml#\d+:\d+$", problem.Path);
    }

    [Fact]
    public void A_free_text_field_is_capped_rather_than_carried()
    {
        var tooLong = Read(Merge("name: " + new string('a', 101) + "\n"));
        Assert.Equal("field_invalid", Assert.Single(tooLong.Problems).Code);
        Assert.Equal("name", Assert.Single(tooLong.Problems).Path);

        var described = Read(Merge("name: Fake provider\ndescription: What it does.\n"));
        Assert.True(described.IsValid);
        Assert.Equal("Fake provider", described.Manifest!.Name);
        Assert.Equal("What it does.", described.Manifest.Description);
    }

    [Fact]
    public void The_bounds_of_the_installation_cap_what_a_manifest_may_ask_for()
    {
        var narrow = new ManifestBounds(10_000, 64);
        var manifest = Merge("limits:\n  timeout_ms: 30000\n");

        var result = ManifestReader.Read(manifest, "fake", NewPackage(manifest, "fake"), narrow);

        Assert.Equal("field_invalid", Assert.Single(result.Problems).Code);
        Assert.Equal("limits.timeout_ms", Assert.Single(result.Problems).Path);
    }

    /// <summary>Every place a rule reads a value, including the ones only reachable inside a list item.</summary>
    private static readonly string[] ReadableFields =
    [
        "manifest_version", "id", "version", "kind", "name", "description", "homepage",
        "contracts", "contracts.protocol", "contracts.operations", "operations",
        "entry", "entry.module", "entry.function",
        "capabilities", "capabilities.exec", "capabilities.exec.executables",
        "capabilities.http", "capabilities.http.hosts",
        "capabilities.env", "capabilities.env.variables",
        "limits", "limits.timeout_ms", "limits.memory_mb",
        "binding", "binding.type", "binding.properties", "binding.required",
    ];

    /// <summary>
    /// Shapes a rule might be handed instead of what it expects. Each one is written to look plausible rather than
    /// obviously wrong, because an implausible value is stopped by an earlier rule and never reaches the read that
    /// matters: `type: [object, "null"]` is good JSON Schema and good dialect, which is exactly why it got past
    /// everything and reached a typed read that could not take a list.
    /// </summary>
    private static readonly string[] WrongShapes = ["[object, \"null\"]", "{ type: object }", "7", "\"object\"", "true", "~"];

    /// <summary>The same question for the places a generated fragment cannot reach: inside a list.</summary>
    private static readonly string[] WrongShapesInLists =
    [
        "operations: [[a, b]]\n",
        "contracts:\n  protocol: [[1]]\n  operations: [1]\n",
        "capabilities:\n  exec:\n    executables:\n      - name: [provider-cli, other]\n",
        "capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        min_version: [1, 2]\n",
        "capabilities:\n  exec:\n    executables:\n      - name: provider-cli\n        version_command: { a: 1 }\n",
        "capabilities:\n  http:\n    hosts: [[api.example.test]]\n",
        "capabilities:\n  env:\n    variables: [{ a: 1 }]\n",
        "binding:\n  type: object\n  required: [[workspace]]\n",
        "binding:\n  type: object\n  properties:\n    workspace: [a, b]\n",
    ];

    /// <summary>
    /// The rule the whole reader is held to: no manifest, however malformed, makes it throw. Every bad value comes
    /// back as a code at a path. A reader that throws takes the whole load with it — the reload answers 500 and a
    /// package present when the runtime starts empties the registry with nothing naming the package at fault — so
    /// this holds every place a rule reads a value, not only the ones a case above happens to name.
    /// </summary>
    [Fact]
    public void No_manifest_however_malformed_makes_the_reader_throw()
    {
        var root = NewPackage(TestPlugins.Manifest("fake"), "fake");

        foreach (var manifest in Malformed())
        {
            var thrown = Record.Exception(() => ManifestReader.Read(manifest, "fake", root, Bounds));

            Assert.True(thrown is null, $"this manifest made the reader throw {thrown?.GetType().Name}: {thrown?.Message}\n\n{manifest}");
        }
    }

    private static IEnumerable<string> Malformed()
    {
        foreach (var field in ReadableFields)
        {
            foreach (var shape in WrongShapes)
            {
                yield return Merge(Nested(field, shape));
            }
        }

        foreach (var fragment in WrongShapesInLists)
        {
            yield return Merge(fragment);
        }
    }

    /// <summary>A dotted field path and a value, written back out as the nested YAML mapping it stands for.</summary>
    private static string Nested(string field, string shape)
    {
        var segments = field.Split('.');
        var fragment = new System.Text.StringBuilder();
        for (var level = 0; level < segments.Length - 1; level++)
        {
            fragment.Append(' ', level * 2).Append(segments[level]).Append(":\n");
        }

        return fragment.Append(' ', (segments.Length - 1) * 2).Append(segments[^1]).Append(": ").Append(shape).Append('\n').ToString();
    }

    /// <summary>Replaces the base manifest's lines that the fragment redefines, then appends the rest.</summary>
    private static string Merge(string fragment)
    {
        var baseText = TestPlugins.Manifest("fake");
        if (fragment.StartsWith("manifest_version:", StringComparison.Ordinal) || fragment.StartsWith("id: fake\nversion", StringComparison.Ordinal))
        {
            return fragment;
        }

        var key = fragment.Split(':')[0];
        var lines = baseText.ReplaceLineEndings("\n").Split('\n').ToList();
        var index = lines.FindIndex(line => line.StartsWith(key + ":", StringComparison.Ordinal));
        if (index >= 0)
        {
            var end = index + 1;
            while (end < lines.Count && lines[end].StartsWith(' '))
            {
                end++;
            }

            lines.RemoveRange(index, end - index);
        }

        return string.Join('\n', lines).TrimEnd('\n') + "\n" + fragment;
    }

    private static ManifestReadResult Read(string manifest, string directoryName = "fake") =>
        ManifestReader.Read(manifest, directoryName, NewPackage(manifest, directoryName), Bounds);

    private static string NewPackage(string manifest, string directoryName)
    {
        // The package outlives the read only as long as the test needs it; the reader touches the entry module
        // and nothing else, so a directory under the test's own temporary root is enough.
        var root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"), directoryName);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "plugin.yaml"), manifest);
        File.WriteAllText(Path.Combine(root, "main.js"), "export function invoke() { return { result: {} }; }");
        return root;
    }
}
