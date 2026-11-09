using System.Text.Json;
using Jason.Cli.Doctor;
using Jason.Contracts.Harness;

namespace Jason.Cli.Moving;

/// <summary>Where the harness root is, and what decided it.</summary>
/// <param name="Path">The folder, full and without a trailing separator.</param>
/// <param name="From">What named it: <see cref="InstallationPlaces.FromOption"/>, <see cref="InstallationPlaces.FromVariable"/>,
/// <see cref="InstallationPlaces.FromSettings"/> or <see cref="InstallationPlaces.FromDefault"/>.</param>
/// <param name="Written">The value as it was written, which the root's rule is held to; null for the default.</param>
public sealed record HarnessRootPlace(string Path, string From, [property: System.Text.Json.Serialization.JsonIgnore] string? Written = null)
{
    /// <summary>
    /// Why this root may not be one, by the rule the runtime holds it to (<see cref="HarnessRootRule"/>), each place
    /// compared where it leads; null when it may.
    /// </summary>
    public string? Refusal(CliEnvironment env) => HarnessRootRule.Refusal(Written, env.Paths.Root, InstallationPlaces.Home(env));
}

/// <summary>
/// The places an installation keeps its files on this machine, read without the runtime: the two verbs that move an
/// installation run while it is stopped, so the harness root the runtime would report is resolved here the way the runtime
/// resolves it — the <c>JASON_Harness__Root</c> variable over the settings file's <c>Harness:Root</c> over <c>Jason</c> in the
/// home directory.
/// </summary>
internal static class InstallationPlaces
{
    /// <summary>The variable the runtime reads <c>Harness:Root</c> from, over the settings file.</summary>
    public const string RootVariable = "JASON_Harness__Root";

    /// <summary>The setting that names the harness root.</summary>
    public const string RootSetting = HarnessRootRule.Setting;

    /// <summary>The folder in the home directory the harness root is when nothing names another.</summary>
    public const string DefaultFolder = HarnessRootRule.DefaultFolder;

    public const string FromOption = "root_option";

    public const string FromVariable = "environment_variable";

    public const string FromSettings = "settings_file";

    public const string FromDefault = "default";

    /// <summary>The account's home directory, or the one this environment names.</summary>
    public static string Home(CliEnvironment env) => IdentityFacts.HomeFor(env.Identity);

    /// <summary>
    /// The variable's value when it is set, by any spelling of its name the configuration reads as the same key; null when
    /// it is not set.
    /// </summary>
    public static string? Variable(CliEnvironment env)
    {
        if (env.Variables is { } variables)
        {
            return variables.FirstOrDefault(variable => string.Equals(variable.Key, RootVariable, StringComparison.OrdinalIgnoreCase)).Value;
        }

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && string.Equals(name, RootVariable, StringComparison.OrdinalIgnoreCase) && entry.Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// The harness root as the runtime would resolve it from this environment and the settings file's text, which may be
    /// null for a file that is not there. A value that is blank is no value, as the runtime reads it.
    /// </summary>
    /// <exception cref="JsonException">The settings file names the root and is not JSON the configuration reads.</exception>
    public static HarnessRootPlace HarnessRoot(CliEnvironment env, string? settings)
    {
        if (Variable(env) is { } variable)
        {
            return string.IsNullOrWhiteSpace(variable) ? Default(env) : new HarnessRootPlace(Spelt(variable), FromVariable, variable);
        }

        return SettingsRoot(settings) is { } written && !string.IsNullOrWhiteSpace(written)
            ? new HarnessRootPlace(Spelt(written), FromSettings, written)
            : Default(env);
    }

    /// <summary>A value as a full path, or as it was written when it is not one the platform reads, which the rule then refuses.</summary>
    private static string Spelt(string written)
    {
        try
        {
            return FolderPaths.Normal(written);
        }
        catch (Exception unreadable) when (unreadable is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return written;
        }
    }

    /// <summary>The root nothing names: <see cref="DefaultFolder"/> in the home directory.</summary>
    public static HarnessRootPlace Default(CliEnvironment env) =>
        new(FolderPaths.Normal(System.IO.Path.Combine(Home(env), DefaultFolder)), FromDefault);

    /// <summary>
    /// The value the settings file gives <c>Harness:Root</c>, read as the configuration reads it: comments and trailing commas
    /// allowed, keys compared without case, a key spelt with a colon the same as the section it names. Null when it gives
    /// none.
    /// </summary>
    public static string? SettingsRoot(string? settings)
    {
        if (settings is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(settings, Reading);
        string? found = null;
        Walk(document.RootElement, string.Empty, ref found);
        return found;
    }

    /// <summary>How the configuration reads a settings file.</summary>
    internal static JsonDocumentOptions Reading { get; } = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static void Walk(JsonElement element, string path, ref string? found)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            if (string.Equals(path, RootSetting, StringComparison.OrdinalIgnoreCase) && element.ValueKind == JsonValueKind.String)
            {
                found = element.GetString();
            }

            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            Walk(property.Value, path.Length == 0 ? property.Name : path + ":" + property.Name, ref found);
        }
    }
}
