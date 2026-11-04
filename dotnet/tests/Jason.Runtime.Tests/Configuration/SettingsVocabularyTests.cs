using System.Reflection;
using Jason.Contracts.Configuration;
using Jason.Runtime.Configuration;
using Jason.Runtime.Notifications;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The names a refused setting may print, held to the option classes they come from. The list lives in the
/// contracts because the command-line client needs it and cannot reference the runtime, so it is a copy, and a
/// copy is only as good as the test that says it is still the same: a setting added to an option class and not
/// to the list would be written as an unknown key in every refusal that names it.
/// </summary>
public class SettingsVocabularyTests
{
    [Fact]
    public void The_vocabulary_is_every_section_and_every_setting_the_option_classes_declare()
    {
        var expected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var section in Sections())
        {
            expected.Add((string)section.GetField("Section")!.GetRawConstantValue()!);
            Walk(section, expected, []);
        }

        Assert.Equal(expected, SettingsVocabulary.Names.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The places a number may stand and still be shown as a key, held to the option classes the same way: the whole
    /// path of every setting that is a list, so an index into a new one is not written as a placeholder, and of nothing
    /// that is a list nowhere, so a number under any other path never passes for an index. A map's key is <c>*</c>.
    /// </summary>
    [Fact]
    public void The_list_paths_are_the_whole_path_of_every_setting_the_option_classes_declare_as_a_list()
    {
        var expected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var section in Sections())
        {
            ListPaths(section, (string)section.GetField("Section")!.GetRawConstantValue()!, [], expected);
        }

        Assert.Equal(expected, SettingsVocabulary.ListPaths.Order(StringComparer.Ordinal));
    }

    /// <summary>The walk above found the sections by reflection; this says it did not find too few of them.</summary>
    [Fact]
    public void Every_section_the_runtime_reads_is_one_the_walk_finds()
    {
        // The sections the runtime reads are the subset; what the walk found has to hold every one of them.
        Assert.Superset(
            new HashSet<Type>
            {
                typeof(RuntimeOptions),
                typeof(LoggingOptions),
                typeof(DispatcherOptions),
                typeof(ManagerOptions),
                typeof(RolesOptions),
                typeof(PluginsOptions),
                typeof(RoutesOptions),
                typeof(UpdateOptions),
                typeof(NotificationsOptions),
                typeof(InboundOptions),
                typeof(HarnessOptions),
                typeof(SubscriptionOrderOptions),
            },
            new HashSet<Type>(Sections()));
    }

    /// <summary>Configuration keys are matched without regard to case, and so is the vocabulary.</summary>
    [Fact]
    public void A_name_is_known_whatever_case_it_was_typed_in()
    {
        Assert.Contains("tickseconds", SettingsVocabulary.Names);
        Assert.Contains("PLUGINS", SettingsVocabulary.Names);
    }

    /// <summary>Every class that names a section of the settings file: the root of each walk.</summary>
    private static IEnumerable<Type> Sections() =>
        typeof(RuntimeOptions).Assembly.GetTypes()
            .Where(type => type.GetField("Section", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true } field
                && field.FieldType == typeof(string));

    private static bool IsList(PropertyInfo property) =>
        property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>);

    /// <summary>
    /// The whole path of every setting below this class that is a list. A map is walked once with <c>*</c> for its key,
    /// and a class is not walked again inside itself, which no option class does.
    /// </summary>
    private static void ListPaths(Type type, string path, IReadOnlyList<Type> chain, ISet<string> paths)
    {
        if (chain.Contains(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is null)
            {
                continue;
            }

            var child = $"{path}:{property.Name}";
            if (IsList(property))
            {
                paths.Add(child);
            }
            else if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                // A map whose entries are lists: each entry is a list, under the one key of the map.
                if (property.PropertyType.GetGenericArguments()[1] is { IsGenericType: true } entry && entry.GetGenericTypeDefinition() == typeof(List<>))
                {
                    paths.Add($"{child}:*");
                }

                foreach (var nested in OptionTypes(property.PropertyType.GetGenericArguments()[1]))
                {
                    ListPaths(nested, $"{child}:*", [.. chain, type], paths);
                }
            }
            else
            {
                foreach (var nested in OptionTypes(property.PropertyType))
                {
                    ListPaths(nested, child, [.. chain, type], paths);
                }
            }
        }
    }

    private static void Walk(Type type, ISet<string> names, HashSet<Type> seen)
    {
        if (!seen.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // A setting is a property the file can set, whoever may call the setter: a notification plugin's
            // binding is filled in by hand after binding, and is still a key a person writes. A computed answer
            // with no setter at all is not a setting, and nothing in the file can name it.
            if (property.SetMethod is null)
            {
                continue;
            }

            names.Add(property.Name);

            foreach (var nested in OptionTypes(property.PropertyType))
            {
                Walk(nested, names, seen);
            }
        }
    }

    /// <summary>The option classes a property's type is made of: itself, or the arguments of a list or a map.</summary>
    private static IEnumerable<Type> OptionTypes(Type type) =>
        type.IsGenericType
            ? type.GetGenericArguments().SelectMany(OptionTypes)
            : type.IsClass && type.Assembly == typeof(RuntimeOptions).Assembly ? [type] : [];
}
