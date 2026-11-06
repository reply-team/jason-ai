using System.Collections;
using System.Collections.Frozen;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using Jason.Contracts.Configuration;
using Jason.Contracts.Discovery;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>
/// Binding that turns a value of the wrong type into a validation failure. The standard binder refuses text where
/// a number or a switch belongs by throwing, and what it throws is neither what anything reading the settings is
/// ready for nor fit to be read: its message repeats the value, and a value typed onto the wrong line is often a
/// secret. So the refusal is caught where the binder raises it and recorded against the instance being bound as
/// one sentence — the setting's path and the type it has to be — and the section's
/// <see cref="BindingFailureValidator{TOptions}"/> refuses that instance with it.
/// </summary>
/// <remarks>
/// From there it is a refusal like any other, handled where validation failures already were: the live seam keeps
/// the last settings that validated, a reload is rejected naming the setting, and a start fails. None of them has
/// to know there is a binder at all, which is the point — a second kind of failure would have needed a second
/// catch in every one of them, and the one that was missing is the one that stopped the runtime.
/// </remarks>
internal static class SafeBinding
{
    private static readonly IReadOnlySet<string> NoPluginIds = FrozenSet<string>.Empty;

    /// <summary>The sentences recorded for each instance whose binding was refused, kept exactly as long as the instance.</summary>
    private static readonly ConditionalWeakTable<object, string[]> Refused = new();

    /// <summary>
    /// What a section whose paths cannot carry a plugin's id is told is installed: nothing. A key of such a section
    /// that is not a settings name is then written as a placeholder, which is the direction a mistake should fall.
    /// </summary>
    public static Func<IReadOnlySet<string>> NoInstalledPlugins { get; } = () => NoPluginIds;

    /// <summary>
    /// Binds the section into the instance, and where the binder refuses a value, records the sentence that says
    /// so for <see cref="TryGetFailures"/> instead of throwing. What the binder bound before it refused stays bound,
    /// and nothing after it is; the validator refuses the instance either way.
    /// </summary>
    /// <remarks>
    /// The binder does not refuse everything it cannot read. A switch, a number or a word written as an object or a list
    /// is passed over, and the setting keeps its default; an entry of a map that holds a value of the wrong type — a
    /// channel's switch written as text, text where a whole entry belongs — is left out of the map, and one the map
    /// already held keeps what the map started with; an item of a list
    /// that it cannot make — an object or a list where a word belongs — is left out of the list, and the items after it
    /// move up into its place; and text where a whole map, list or object belongs is passed over, leaving everything
    /// under it at its default. The binder goes on as though none of it had been written. So the section is compared
    /// with the type each of its paths has to be, and each of these is recorded the same way, by its path and that
    /// type: no setting, map or list is used with anything of the file passed over.
    /// </remarks>
    /// <param name="installedPluginIds">
    /// Called only when there is a sentence to compose, and at most once for each binding, so the listing is the one on
    /// disk at that moment.
    /// </param>
    public static void Bind(IConfigurationSection section, object options, Func<IReadOnlySet<string>> installedPluginIds)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(installedPluginIds);

        // Listed once for every sentence this binding composes, and only if there is one.
        var listing = new Lazy<IReadOnlySet<string>>(installedPluginIds);
        IReadOnlySet<string> Installed() => listing.Value;

        try
        {
            section.Bind(options);
        }
        catch (InvalidOperationException binder)
        {
            // The exception goes no further than these sentences: not to a logger, not into a field, not inside
            // another exception. Each of those would carry the value along with it.
            Refused.AddOrUpdate(options, [.. Refusal(section, options, binder, Installed)]);
            return;
        }

        var leftOut = new List<string>();
        LeftOut(section, options.GetType(), options, bound: true, leftOut, Installed);
        if (leftOut.Count > 0)
        {
            Refused.AddOrUpdate(options, [.. leftOut]);
        }
    }

    /// <summary>
    /// What a binding the binder refused is refused for: the binder's own words, read for the path and the type, where
    /// they are words it is known to use. Where they are not — a word that may be absent, written as an object, is
    /// refused in words that name neither — the shapes the section was written in say it, as far as they can be told
    /// without what the binder bound, which stopped at the refusal; and failing those, the section.
    /// </summary>
    private static List<string> Refusal(IConfigurationSection section, object options, InvalidOperationException binder, Func<IReadOnlySet<string>> installed)
    {
        if (SettingsFailure.Read(binder, installed()) is { } said)
        {
            return [said];
        }

        var shapes = new List<string>();
        LeftOut(section, options.GetType(), options, bound: false, shapes, installed);
        return shapes.Count > 0 ? shapes : [SettingsFailure.Unbindable(section.Path, binder, installed())];
    }

    /// <summary>
    /// The sentence for everything of the section the binder passed over, by the type each of its paths has to be: a
    /// setting read from text written as an object or a list; text where a whole map, list or object belongs; an item
    /// of a list the binder could not make; and, where the section was bound to the end (<paramref name="bound"/>), an
    /// entry of a map the binder left out. It looks into the options' own objects, every entry of every map and every
    /// list; the items of a list are checked one by one but not looked into, and a type of the platform's own is not
    /// looked into at all.
    /// </summary>
    private static void LeftOut(IConfigurationSection section, Type type, object? instance, bool bound, List<string> failures, Func<IReadOnlySet<string>> installed)
    {
        if (IsText(type))
        {
            // A switch, a number or a word written as an object or a list: the binder keeps the default for it.
            if (section.GetChildren().Any())
            {
                failures.Add($"{SettingKey.ShownPath(section.Path, installed())} could not be read as {Nullable.GetUnderlyingType(type) ?? type}.");
            }

            return;
        }

        // Only what the file wrote is walked. A section it left out holds nothing to refuse, and walking the declared
        // types into it would never end for a type that holds one of its own; the file's own depth bounds the walk.
        if (!section.Exists())
        {
            return;
        }

        var shape = ShapeOf(type);
        if (shape == Shape.Other)
        {
            return;
        }

        // Text where the whole of it belongs. An empty text is what an empty list or object becomes on the way in, and
        // is read as one.
        if (!string.IsNullOrEmpty(section.Value))
        {
            failures.Add($"{SettingKey.ShownPath(section.Path, installed())} could not be read as {Expected(type, shape)}.");
            return;
        }

        switch (shape)
        {
            case Shape.Map:
                var map = instance as IDictionary;
                var compare = bound && map is not null;
                var entryType = ElementType(type);
                foreach (var entry in section.GetChildren())
                {
                    var held = map is not null && map.Contains(entry.Key);
                    if (compare && (!held || Unreadable(entry, entryType)))
                    {
                        failures.Add(Unmade(entry, entryType, installed()));
                        continue;
                    }

                    LeftOut(entry, entryType, held ? map![entry.Key] : null, bound, failures, installed);
                }

                break;

            case Shape.List:
                var itemType = ElementType(type);
                foreach (var item in section.GetChildren())
                {
                    if (ItemLeftOut(item, itemType, installed) is { } failure)
                    {
                        failures.Add(failure);
                    }
                }

                break;

            default:
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length > 0 || property.GetMethod is not { IsPublic: true })
                    {
                        continue;
                    }

                    // A setting read from text is one the binder writes; a value computed from the others is no setting.
                    if (IsText(property.PropertyType) && property.SetMethod is not { IsPublic: true })
                    {
                        continue;
                    }

                    LeftOut(section.GetSection(property.Name), property.PropertyType, instance is null ? null : property.GetValue(instance), bound, failures, installed);
                }

                break;
        }
    }

    /// <summary>
    /// The sentence for an item of a list the binder could not make, or null for one it made. A word cannot be made from
    /// an object or a list, and an object cannot be made from text; past that, the binder makes the item or refuses it —
    /// a number it cannot read in a list of numbers, a value of the wrong type inside an object of a list — and its
    /// refusal is what says the item was left out. An item written as nothing keeps its place, empty, for the section's
    /// own validator to judge.
    /// </summary>
    private static string? ItemLeftOut(IConfigurationSection item, Type itemType, Func<IReadOnlySet<string>> installed)
    {
        var expected = Nullable.GetUnderlyingType(itemType) ?? itemType;
        if (IsText(itemType) ? item.GetChildren().Any() : !string.IsNullOrEmpty(item.Value))
        {
            return $"{SettingKey.ShownPath(item.Path, installed())} could not be read as {expected}.";
        }

        try
        {
            item.Get(itemType);
            return null;
        }
        catch (InvalidOperationException binder)
        {
            return SettingsFailure.Read(binder, installed()) ?? $"{SettingKey.ShownPath(item.Path, installed())} could not be read as {expected}.";
        }
    }

    /// <summary>
    /// Whether an entry the map already held was written with a value the binder cannot read. Left out of a map that
    /// starts with that key, it looks like an entry the file kept: the map's own default stands in for what the file got
    /// wrong, and only reading the entry on its own tells them apart.
    /// </summary>
    private static bool Unreadable(IConfigurationSection entry, Type entryType)
    {
        if (!IsText(entryType))
        {
            return false;
        }

        try
        {
            entry.Get(entryType);
            return false;
        }
        catch (InvalidOperationException)
        {
            // Unmade asks again and says why; the exception goes no further than this answer.
            return true;
        }
    }

    /// <summary>
    /// Why the binder left an entry out, in its own words where reading the entry on its own draws them out — a value of
    /// the wrong type inside it is then named by its whole path — and otherwise by the entry's path and the type the
    /// entry has to be. Never the value, which the binder's words carry and the sentence does not.
    /// </summary>
    private static string Unmade(IConfigurationSection entry, Type entryType, IReadOnlySet<string> installedPluginIds)
    {
        try
        {
            entry.Get(entryType);
        }
        catch (InvalidOperationException binder) when (SettingsFailure.Read(binder, installedPluginIds) is { } said)
        {
            // Read for the path and the type alone, as every refusal of the binder is; the exception goes no further.
            return said;
        }
        catch (InvalidOperationException)
        {
            // Words the binder's pattern does not know: the entry's own path and type say it instead.
        }

        return $"{SettingKey.ShownPath(entry.Path, installedPluginIds)} could not be read as {entryType}.";
    }

    /// <summary>
    /// Whether the binder reads a value of this type from one line of text: a word, a number, a switch, an enum's name,
    /// anything with a converter from text, and any of those that may be absent.
    /// </summary>
    private static bool IsText(Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        return value == typeof(string) || value.IsPrimitive || value.IsEnum || value == typeof(decimal)
            || TypeDescriptor.GetConverter(value).CanConvertFrom(typeof(string));
    }

    /// <summary>What a path of the section holds, as far as what was passed over is concerned.</summary>
    private enum Shape
    {
        /// <summary>A type of the platform's own, or a value that is no object: not looked into.</summary>
        Other,

        Map,

        List,

        /// <summary>An object of the settings, whose properties are its paths.</summary>
        Object,
    }

    private static Shape ShapeOf(Type type) =>
        typeof(IDictionary).IsAssignableFrom(type) ? Shape.Map
        : typeof(IList).IsAssignableFrom(type) ? Shape.List
        : type.IsClass && !IsPlatform(type) ? Shape.Object
        : Shape.Other;

    /// <summary>A type of the platform's own — a JSON node, say — which the binder reads in its own way, if at all.</summary>
    private static bool IsPlatform(Type type) =>
        type.Namespace is { } name
        && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal));

    /// <summary>What has to be where text was written instead: a map of entries, a list of items, or an object.</summary>
    private static string Expected(Type type, Shape shape) => shape switch
    {
        Shape.Map => $"a map of {ElementType(type)}",
        Shape.List => $"a list of {ElementType(type)}",
        _ => type.ToString(),
    };

    /// <summary>The type of a map's entries or of a list's items: the last of its type's arguments.</summary>
    private static Type ElementType(Type type) =>
        type.IsGenericType ? type.GetGenericArguments()[^1] : type.GetElementType() ?? typeof(object);

    /// <summary>
    /// Records sentences for an instance a section was read into by hand, for the section's
    /// <see cref="BindingFailureValidator{TOptions}"/> to refuse it with, exactly as a refused binding is recorded.
    /// </summary>
    public static void Refuse(object options, IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(failures);
        Refused.AddOrUpdate(options, [.. failures]);
    }

    /// <summary>The sentences recorded for an instance whose binding was refused, if it was.</summary>
    public static bool TryGetFailures(object options, [NotNullWhen(true)] out IReadOnlyList<string>? failures)
    {
        var recorded = Refused.TryGetValue(options, out var sentences);
        failures = sentences;
        return recorded;
    }

    /// <summary>
    /// The plugins installed in a data directory: the directories the plugin loader scans that hold a manifest and are
    /// named the way a plugin's id has to be. Any other directory there is not a plugin, whatever it was meant to become —
    /// a folder made by hand, a download half unpacked — and its name is whatever somebody typed, so a key that matches
    /// it is no more fit to be shown than any other. A listing that cannot be read names nothing, so a sentence
    /// composed meanwhile shows a plugin's id as a placeholder.
    /// </summary>
    public static IReadOnlySet<string> InstalledPluginIds(JasonPaths paths)
    {
        try
        {
            return PackageScanner.Scan(paths)
                .Where(candidate => candidate.HasManifest && ManifestRules.IsPluginId(candidate.Name))
                .Select(candidate => candidate.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or SecurityException)
        {
            return NoPluginIds;
        }
    }
}

/// <summary>
/// Refuses an instance whose binding was refused, with the sentences <see cref="SafeBinding"/> recorded for it. One is
/// registered for every section that is bound, ahead of the section's own validators, so the first thing said about
/// a section that could not be read is that it could not be read.
/// </summary>
internal sealed class BindingFailureValidator<TOptions> : IValidateOptions<TOptions>
    where TOptions : class
{
    public ValidateOptionsResult Validate(string? name, TOptions options) =>
        SafeBinding.TryGetFailures(options, out var failures) ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
}
