using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Configuration;

/// <summary>
/// One global route: the plugin an operation's work goes to, and the binding that says which account of it to
/// work through. <see cref="Binding"/> is a <see cref="JsonNode"/> rather than a <see cref="JsonObject"/> so
/// that a settings file saying something other than an object can be reported by name instead of disappearing;
/// validation on start refuses anything but an object, so everything downstream reads an object or nothing.
/// </summary>
public sealed class RouteEntry
{
    public string? Plugin { get; set; }

    public JsonNode? Binding { get; set; }
}

/// <summary>
/// The global half of routing: a default, and the operations that override it. The campaign half lives in the
/// database, because it is edited through the API; this half lives in <c>settings.json</c>, because an installer
/// or a bootstrap configuration has to be able to write it before a runtime has ever run.
/// <para>
/// There is no shipped default. A product that routed somewhere out of the box would make its vendor-neutrality
/// a claim contradicted by its own configuration.
/// </para>
/// <para>
/// Both halves are frozen into a route snapshot by a reload; nothing here is read while work is being dispatched.
/// </para>
/// </summary>
public sealed partial class RoutesOptions
{
    public const string Section = "Routes";

    public RouteEntry? Default { get; set; }

    public Dictionary<string, RouteEntry> Operations { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Read rather than bound. The configuration system flattens a file into string keys and string values, and
    /// the standard binder cannot make a <see cref="JsonObject"/> out of one at all, so a binding is rebuilt
    /// here: an object from a section with named children, an array from one whose children are 0…n-1, and a
    /// leaf from its text — booleans and JSON numbers as themselves, everything else as a string. The one thing
    /// that cannot survive the round trip is a string spelled like a number or like a boolean: by the time the
    /// section is read, <c>"12345"</c> and <c>12345</c> are the same five characters. A binding field that must
    /// be a numeric string is set per campaign with <c>route set</c>, which never goes through the
    /// configuration system.
    /// </summary>
    public static RoutesOptions Read(IConfiguration section)
    {
        var options = new RoutesOptions();
        Fill(section, options);
        return options;
    }

    /// <summary>The same read, into the instance the options factory made, which is what the options system needs.</summary>
    internal static void Fill(IConfiguration section, RoutesOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);

        var global = section.GetSection(nameof(Default));
        options.Default = global.Exists() ? Entry(global) : null;

        options.Operations.Clear();
        foreach (var operation in section.GetSection(nameof(Operations)).GetChildren())
        {
            options.Operations[operation.Key] = Entry(operation);
        }
    }

    private static RouteEntry Entry(IConfigurationSection section) => new()
    {
        Plugin = section[nameof(RouteEntry.Plugin)],
        Binding = Json(section.GetSection(nameof(RouteEntry.Binding))),
    };

    private static JsonNode? Json(IConfigurationSection section)
    {
        // Bounded by the configuration system itself: its JSON parser refuses a document nested deeper than 64.
        var children = section.GetChildren().ToList();
        if (children.Count == 0)
        {
            return section.Value is null ? null : Leaf(section.Value);
        }

        if (IsArray(children))
        {
            return new JsonArray([.. children.Select(Json)]);
        }

        var members = new JsonObject();
        foreach (var child in children)
        {
            members[child.Key] = Json(child);
        }

        return members;
    }

    private static bool IsArray(List<IConfigurationSection> children)
    {
        for (var index = 0; index < children.Count; index++)
        {
            if (!string.Equals(children[index].Key, index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static JsonNode Leaf(string value)
    {
        // The configuration system's JSON parser hands every scalar over as text — a boolean spelled the way
        // .NET spells one, a number as it was written — so the two that JSON has words for are read back, and
        // everything else is a string.
        if (bool.TryParse(value, out var flag))
        {
            return JsonValue.Create(flag);
        }

        return (Number().IsMatch(value) ? JsonNode.Parse(value) : null) ?? JsonValue.Create(value)!;
    }

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][-+]?[0-9]+)?\z")]
    private static partial Regex Number();
}
