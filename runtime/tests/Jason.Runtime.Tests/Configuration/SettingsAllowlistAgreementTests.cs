using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Jason.Cli.Doctor;
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
            else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                // The one map keyed by an operation is the routes'; the others are keyed by a plugin's id.
                var key = property.Name == nameof(RoutesOptions.Operations) ? OperationCatalog.All[0].Id : "<id>";
                foreach (var setting in Walk(propertyType.GetGenericArguments()[1], $"{child}:{key}", [.. chain, type]))
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
