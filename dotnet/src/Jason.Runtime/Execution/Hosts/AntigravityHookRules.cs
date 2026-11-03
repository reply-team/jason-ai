using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// What one attempt's hook file tells the <c>host-hook</c> mode: the command word that calls the runtime back, the memory
/// files a role may read, the tools it may use without being asked, the tools it may not use, and the one file it may
/// write. Carried as one argument of the hook's command line in base64url, so no path or quote of a person's ever reaches
/// a shell. Never the capability token and never the launch envelope.
/// </summary>
/// <param name="Cli">The command word a role calls back with.</param>
/// <param name="Read">The full paths of the memory files the launch names, each one a file the role may view.</param>
/// <param name="Approve">The host's own names of the tools the person granted.</param>
/// <param name="Deny">The host's own names of the tools that are not allowed, from the profile and from the tools it does not grant.</param>
/// <param name="Write">The full path of the one file a role may create, its result.</param>
public sealed record AntigravityHookRules(string Cli, IReadOnlyList<string> Read, IReadOnlyList<string> Approve, IReadOnlyList<string> Deny, string Write)
{
    /// <summary>The only version of the rules this build writes and reads.</summary>
    public const int Version = 1;

    public static string Encode(AntigravityHookRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var document = new JsonObject
        {
            ["v"] = Version,
            ["cli"] = rules.Cli,
            ["read"] = Strings(rules.Read),
            ["approve"] = Strings(rules.Approve),
            ["deny"] = Strings(rules.Deny),
            ["write"] = rules.Write,
        };
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(document.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The rules a text holds, or null where it is not base64url of a document of this version in exactly this shape.</summary>
    public static AntigravityHookRules? Decode(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(padded));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || HoldsAKeyTwice(root, "v", "cli", "write", "read", "approve", "deny")
                || !root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != Version
                || !root.TryGetProperty("cli", out var cli) || cli.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("write", out var write) || write.ValueKind != JsonValueKind.String
                || Strings(root, "read") is not { } read
                || Strings(root, "approve") is not { } approve
                || Strings(root, "deny") is not { } deny)
            {
                return null;
            }

            return new AntigravityHookRules(cli.GetString()!, read, approve, deny, write.GetString()!);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether an object holds a key the hook reads more than once, compared without regard to case. Readers disagree on which
    /// of two equal keys wins, so a document that holds one is not read at all.
    /// </summary>
    internal static bool HoldsAKeyTwice(JsonElement element, params string[] keys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            if (keys.Contains(name, StringComparer.OrdinalIgnoreCase) && !seen.Add(name))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(value => (JsonNode)JsonValue.Create(value)!)]);

    private static List<string>? Strings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            values.Add(item.GetString()!);
        }

        return values;
    }
}
