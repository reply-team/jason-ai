using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Jason.Cli.Doctor;
using Jason.Contracts.Api;
using Jason.Contracts.Configuration;
using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The list of settings whose values travel, held to the settings the runtime actually has. The list lives in the
/// command-line client, which cannot reference the runtime, so it names settings by spelling; a setting renamed
/// here and not there would quietly stop being carried, and one misspelled there was never carried at all. This walks
/// the option classes the way the settings vocabulary's test does, and asks the list about every setting it finds.
/// </summary>
public sealed class SettingsAllowlistAgreementTests
{
    private const string Plugin = "fake-provider";

    private static readonly IReadOnlySet<string> Installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Plugin };

    /// <summary>One setting a settings file can hold: its path with a placeholder for a plugin's key, and what kind of value it is.</summary>
    private sealed record Setting(string Path, Type Type);

    [Fact]
    public void Every_path_on_the_list_is_a_setting_of_the_kind_the_list_says()
    {
        var settings = Settings().ToDictionary(setting => setting.Path, setting => setting.Type, StringComparer.OrdinalIgnoreCase);

        foreach (var line in SettingsAllowlist.Default.Published)
        {
            var path = line[..line.IndexOf(" (", StringComparison.Ordinal)];
            var kind = line[(path.Length + 2)..];

            Assert.True(settings.TryGetValue(path, out var type), $"{path} is on the list and is no setting the runtime reads.");
            var expected = kind switch
            {
                "true or false)" => typeof(bool),
                "a 32-bit integer)" => typeof(int),
                "a 64-bit integer)" => typeof(long),
                _ when kind.StartsWith("one of ", StringComparison.Ordinal) => typeof(string),
                _ when kind.StartsWith("a kind ", StringComparison.Ordinal) => typeof(string),
                _ => throw new InvalidOperationException($"No test knows what '{kind}' is."),
            };

            // Each kind is exactly one type: a number the list calls 32-bit is bound to an int, and a number it calls
            // 64-bit to a long, so a number carried is a number the runtime itself would have taken.
            Assert.True(type == expected, $"{path} is {type} and the list says it carries {kind}.");
        }
    }

    /// <summary>
    /// A document with a number written at every number the runtime reads, each its own: the only ones that appear in
    /// the description are the ones the list names, and every one the list names appears.
    /// </summary>
    [Fact]
    [Critical(Family.S4, "the doctor allowlist and the runtime agree")]
    public void Of_a_number_at_every_setting_the_runtime_reads_exactly_the_listed_ones_are_carried()
    {
        var settings = Settings().ToList();
        var listed = SettingsAllowlist.Default.Published
            .Where(line => line.EndsWith("-bit integer)", StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf(" (", StringComparison.Ordinal)])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var root = new JsonObject();
        var sentinels = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < settings.Count; index++)
        {
            var setting = settings[index];
            var sentinel = 424_200_100L + index;
            JsonNode value = setting.Type == typeof(bool)
                ? JsonValue.Create(true)
                : setting.Type == typeof(string) ? JsonValue.Create($"SENTINEL-{index}")! : JsonValue.Create(sentinel);
            if (setting.Type == typeof(int) || setting.Type == typeof(long))
            {
                sentinels[setting.Path] = sentinel;
            }

            Place(root, setting.Path, value);
        }

        var shape = ConfigShape.Describe(root.ToJsonString(), SettingsAllowlist.Default, Installed).ToJsonString();

        Assert.NotEmpty(sentinels);
        foreach (var (path, sentinel) in sentinels)
        {
            var found = shape.Contains(sentinel.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.True(found == listed.Contains(path), $"{path}: carried {found}, listed {listed.Contains(path)}");
        }

        Assert.DoesNotContain("SENTINEL", shape, StringComparison.Ordinal);
    }

    /// <summary>
    /// A word at every text and every list item the runtime reads: no word leaves a leaf the list does not name. Twice over.
    /// A word nobody would choose, which no leaf may carry whatever the list says, since the list holds a text only to
    /// the closed set its setting reads. And, at every such leaf at once, a word each kind of the closed sets accepts: of
    /// the leaves that carry it, none is one the list names for another kind, so a widening that reaches a leaf the
    /// runtime reads, or one kind passed off as another, is seen.
    /// </summary>
    [Fact]
    public void Of_a_word_at_every_text_and_list_item_the_runtime_reads_only_the_leaves_the_list_names_are_carried()
    {
        var leaves = Settings().Where(setting => setting.Type == typeof(string)).Select(setting => setting.Path).ToList();
        Assert.NotEmpty(leaves);

        // The leaves the list names for each closed set, from the list's own published lines.
        HashSet<string> NamedFor(string kind) => SettingsAllowlist.Default.Published
            .Where(line => line.Contains($"({kind}", StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf(" (", StringComparison.Ordinal)])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var namedFor = new Dictionary<string, HashSet<string>>
        {
            ["debug"] = NamedFor("one of "),
            [ManagerTriggerKinds.All.First()] = NamedFor("a kind "),
        };
        Assert.All(namedFor.Values, Assert.NotEmpty);

        var unchosen = new JsonObject();
        for (var index = 0; index < leaves.Count; index++)
        {
            Place(unchosen, leaves[index], JsonValue.Create($"SENTINEL-{index}")!);
        }

        var unchosenShape = ConfigShape.Describe(unchosen.ToJsonString(), SettingsAllowlist.Default, Installed, out var carriedUnchosen);
        Assert.Empty(carriedUnchosen);
        Assert.DoesNotContain("SENTINEL", unchosenShape.ToJsonString(), StringComparison.Ordinal);

        foreach (var (word, named) in namedFor)
        {
            var chosen = new JsonObject();
            foreach (var leaf in leaves)
            {
                Place(chosen, leaf, JsonValue.Create(word)!);
            }

            ConfigShape.Describe(chosen.ToJsonString(), SettingsAllowlist.Default, Installed, out var carried);
            var strays = carried.Where(path => !named.Contains(path)).ToList();
            Assert.True(strays.Count == 0, $"'{word}' was carried at {string.Join(", ", strays)}, which the list does not name for it.");
        }
    }

    /// <summary>Every setting there is, by walking the option classes: a section's settings, down through the classes inside it.</summary>
    private static IEnumerable<Setting> Settings()
    {
        var sections = typeof(RuntimeOptions).Assembly.GetTypes()
            .Where(type => type.GetField("Section", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true } field && field.FieldType == typeof(string));

        foreach (var section in sections)
        {
            foreach (var setting in Walk(section, (string)section.GetField("Section")!.GetRawConstantValue()!, []))
            {
                yield return setting;
            }
        }
    }

    private sealed class MapOfText
    {
        public Dictionary<string, string> Map { get; set; } = [];
    }

    private sealed class MapOfMapsOfNumbers
    {
        public Dictionary<string, Dictionary<string, int>> Map { get; set; } = [];
    }

    /// <summary>A map the walk has no branch for goes red here rather than passing with nothing to judge.</summary>
    [Theory]
    [InlineData(typeof(MapOfText))]
    [InlineData(typeof(MapOfMapsOfNumbers))]
    public void A_map_with_no_branch_in_the_walk_is_refused_rather_than_passed_over(Type options)
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Walk(options, "Section", []).ToList());
    }

    private static IEnumerable<Setting> Walk(Type type, string path, IReadOnlyList<Type> chain)
    {
        if (chain.Contains(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is null)
            {
                continue;
            }

            var child = $"{path}:{property.Name}";
            var propertyType = property.PropertyType;

            if (propertyType == typeof(JsonNode))
            {
                // A binding is whatever its plugin asks for. These are names the settings know that a listed path ends in.
                foreach (var name in new[] { "Port", "Enabled", "MaxCalls", "TickSeconds", "QuietSeconds" })
                {
                    yield return new Setting($"{child}:{name}", typeof(int));
                }
            }
            else if (propertyType == typeof(List<string>))
            {
                yield return new Setting($"{child}[]", typeof(string));
            }
            else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                && propertyType.GetGenericArguments()[1] == typeof(List<string>))
            {
                // The one map of lists is keyed by a journal kind: the order of that kind's subscribers.
                yield return new Setting($"{child}:workitem_failed[]", typeof(string));
            }
            else if (propertyType == typeof(Dictionary<string, Dictionary<string, string>>))
            {
                // The one map of maps of text is a plugin's configuration, and its keys are the plugin's own.
                yield return new Setting($"{child}:<id>:url", typeof(string));
            }
            else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                && propertyType.GetGenericArguments()[1] == typeof(int))
            {
                // The one map of numbers is keyed by a role. A shipped role's number travels; a person's own role is not on
                // the list, so the sentinel written under one has to stay behind.
                // A second map of numbers would be read as keyed by a role too, and the guard would not see it: it yields leaves.
                foreach (var role in ShippedRoles.Names)
                {
                    yield return new Setting($"{child}:{role}", typeof(int));
                }

                yield return new Setting($"{child}:a-role-of-their-own", typeof(int));
            }
            else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                // The one map keyed by an operation is the routes'; the others are keyed by a plugin's id.
                var key = property.Name == nameof(RoutesOptions.Operations) ? OperationCatalog.All[0].Id : "<id>";
                var leaves = Walk(propertyType.GetGenericArguments()[1], $"{child}:{key}", [.. chain, type]).ToList();
                Assert.True(
                    leaves.Count > 0 && !leaves.Any(leaf => leaf.Path.EndsWith(":Item", StringComparison.Ordinal)),
                    $"{child} is a map this walk has no branch for: it yields no setting a test could reach.");
                foreach (var setting in leaves)
                {
                    yield return setting;
                }
            }
            else if (propertyType.IsClass && propertyType != typeof(string) && propertyType.Assembly == typeof(RuntimeOptions).Assembly)
            {
                foreach (var setting in Walk(propertyType, child, [.. chain, type]))
                {
                    yield return setting;
                }
            }
            else
            {
                yield return new Setting(child, Nullable.GetUnderlyingType(propertyType) ?? propertyType);
            }
        }
    }

    /// <summary>Puts a value at a path in a document, as an item of a list where the path ends in <c>[]</c>.</summary>
    private static void Place(JsonObject root, string path, JsonNode value)
    {
        var isList = path.EndsWith("[]", StringComparison.Ordinal);
        var keys = (isList ? path[..^2] : path).Split(':').Select(key => key == "<id>" ? Plugin : key).ToArray();

        var parent = root;
        foreach (var key in keys[..^1])
        {
            parent = (JsonObject)(parent[key] ??= new JsonObject());
        }

        if (isList)
        {
            ((JsonArray)(parent[keys[^1]] ??= new JsonArray())).Add(value);
        }
        else
        {
            parent[keys[^1]] = value;
        }
    }
}
