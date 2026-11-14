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
public class RouteEntry
{
    public string? Plugin { get; set; }

    public JsonNode? Binding { get; set; }
}

/// <summary>
/// One operation's global routes: the route of the operation itself, and the channels that override it, each a route of
/// its own under the channel it names (<c>Routes:Operations:&lt;operation&gt;:Channels:&lt;channel&gt;</c>). The channel is
/// a key of its own rather than part of the operation's, because the colon a compound key would need is what separates
/// the keys of a settings path. Where <see cref="Channels"/> names a route, the operation's own
/// <see cref="RouteEntry.Plugin"/> may be left out: the operation then has channel routes and no route of its own. A file
/// written before channel routes existed names none, and means what it meant.
/// </summary>
public sealed class OperationRouteEntry : RouteEntry
{
    public Dictionary<string, RouteEntry> Channels { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether text stood where the map of channels belongs, so nothing in it could be read. The validator, which knows how
    /// to name the operation's key, refuses it: reading past it would leave the operation without its channel routes and
    /// without a word.
    /// </summary>
    internal bool ChannelsUnreadable { get; set; }
}

/// <summary>
/// The global half of routing: a default, the operations that override it, and the channels that override an
/// operation. The campaign half lives in the database, because it is edited through the API; this half lives in
/// <c>settings.json</c>, because an installer or a bootstrap configuration has to be able to write it before a runtime
/// has ever run.
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

    public Dictionary<string, OperationRouteEntry> Operations { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The paths of channel routes written where no operation stands above them — under <c>Routes:Default</c>, or at the
    /// top of the section — each as <c>…:Channels:&lt;channel&gt;</c>. A channel route is a route of one operation, so
    /// none of these is a route. Read as nothing, they would leave the default's plugin quietly taking every send on that
    /// channel, so the reload refuses each one by its path.
    /// </summary>
    internal List<string> MisplacedChannels { get; } = [];

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
    /// <remarks>
    /// Text where the whole section, or its map of operations, belongs has nothing in it to read, and reading past it
    /// left no routes at all without a word. It is recorded against the instance by its path and what has to be there,
    /// as a bound section's is, for the section's <see cref="BindingFailureValidator{TOptions}"/> to refuse: a start
    /// then fails and a reload is rejected, naming the setting.
    /// </remarks>
    internal static void Fill(IConfiguration section, RoutesOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);

        var passedOver = new List<string>();
        if (section is IConfigurationSection { Value: { Length: > 0 } })
        {
            passedOver.Add($"{Section} could not be read as {typeof(RoutesOptions)}.");
        }

        if (section.GetSection(nameof(Operations)).Value is { Length: > 0 })
        {
            passedOver.Add($"{Section}:{nameof(Operations)} could not be read as a map of {typeof(RouteEntry)}.");
        }

        if (passedOver.Count > 0)
        {
            SafeBinding.Refuse(options, passedOver);
        }

        var global = section.GetSection(nameof(Default));
        options.Default = global.Exists() ? Entry(global) : null;

        options.MisplacedChannels.Clear();
        Misplaced(options, global.GetSection(nameof(OperationRouteEntry.Channels)), $"{Section}:{nameof(Default)}:{nameof(OperationRouteEntry.Channels)}");
        Misplaced(options, section.GetSection(nameof(OperationRouteEntry.Channels)), $"{Section}:{nameof(OperationRouteEntry.Channels)}");

        options.Operations.Clear();
        foreach (var operation in section.GetSection(nameof(Operations)).GetChildren())
        {
            options.Operations[operation.Key] = OperationEntry(operation);
        }
    }

    /// <summary>
    /// Records a <c>Channels</c> written where no operation stands above it: each channel under it by its whole path, or
    /// the map's own path where text stands there instead.
    /// </summary>
    private static void Misplaced(RoutesOptions options, IConfigurationSection channels, string path)
    {
        if (channels.Value is { Length: > 0 })
        {
            options.MisplacedChannels.Add(path);
        }

        foreach (var channel in channels.GetChildren())
        {
            options.MisplacedChannels.Add($"{path}:{channel.Key}");
        }
    }

    private static RouteEntry Entry(IConfigurationSection section) => new()
    {
        Plugin = section[nameof(RouteEntry.Plugin)],
        Binding = Json(section.GetSection(nameof(RouteEntry.Binding))),
    };

    private static OperationRouteEntry OperationEntry(IConfigurationSection section)
    {
        var channels = section.GetSection(nameof(OperationRouteEntry.Channels));
        var entry = new OperationRouteEntry
        {
            Plugin = section[nameof(RouteEntry.Plugin)],
            Binding = Json(section.GetSection(nameof(RouteEntry.Binding))),
            ChannelsUnreadable = channels.Value is { Length: > 0 },
        };

        foreach (var channel in channels.GetChildren())
        {
            entry.Channels[channel.Key] = Entry(channel);
        }

        return entry;
    }

    internal static JsonNode? Json(IConfigurationSection section)
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

    /// <summary>
    /// Whether the settings file can carry this binding as it is: written under a route and read back the way a reload
    /// reads it (<see cref="Json"/>), it is the same binding. It is not for a string spelled like a number or a boolean,
    /// an empty object or list, keys that differ only in case, or a key holding a colon — the settings writer refuses
    /// those rather than freeze a binding the caller did not send.
    /// </summary>
    internal static bool Carries(JsonObject binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        try
        {
            var document = new JsonObject { [nameof(RouteEntry.Binding)] = binding.DeepClone() }.ToJsonString();
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document), writable: false);
            var read = new ConfigurationBuilder().Add(new Microsoft.Extensions.Configuration.Json.JsonStreamConfigurationSource { Stream = stream }).Build();
            return JsonNode.DeepEquals(Json(read.GetSection(nameof(RouteEntry.Binding))), binding);
        }
        catch (Exception unread) when (unread is FormatException or InvalidOperationException)
        {
            return false;
        }
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
