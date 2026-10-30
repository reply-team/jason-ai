using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The capabilities of Reply's API a test can say the account's Reply does not offer yet, one switch each in
/// <c>capabilities.json</c>, <c>{ "&lt;capability&gt;": true | false }</c>; a capability never named is offered. The
/// command that calls one not offered answers what reply-cli prints for a route Reply's API does not have: <c>reply.refused</c>,
/// status 404, no code of Reply's own, and Reply's sentence naming the route. A name this program has no switch for, a
/// value other than true or false, or a file that does not parse fails every command as a store it cannot read, so a
/// misspelt switch cannot make a row pass for nothing.
/// </summary>
internal static partial class Account
{
    private const string CapabilitiesFile = "capabilities.json";

    /// <summary>
    /// Each capability a switch can turn off, and the route of Reply's that offers it. <c>direct_send</c> is Reply's record
    /// of a direct send — its reference, its moment and its key — read by <c>direct-send get</c> and cancelled by
    /// <c>direct-send cancel</c> (Account.DirectSends.cs);
    /// <c>inbox_changes</c> is Reply's change feed, read by <c>inbox changes</c>.
    /// </summary>
    private static readonly Dictionary<string, string> Capabilities = new(StringComparer.Ordinal)
    {
        ["send_direct_email"] = "POST /v3/contacts/{0}/send-direct-email",
        ["direct_send"] = "GET /v3/direct-sends/{0}",
        ["inbox_changes"] = "GET /v3/inbox/threads/changes",
    };

    /// <summary>
    /// The refusal of a file this program cannot read as switches — one that does not parse, a switch it does not know, or
    /// one that is not <c>true</c> or <c>false</c> — or null when every switch is one it knows and reads.
    /// </summary>
    private static Ending? UnknownCapability(string root)
    {
        var file = Path.Combine(root, CapabilitiesFile);
        if (!File.Exists(file))
        {
            return null;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(File.ReadAllText(file));
        }
        catch (System.Text.Json.JsonException)
        {
            return Refused("usage.store", $"'{CapabilitiesFile}' is not JSON.", exit: 2);
        }

        if (parsed is not JsonObject switches)
        {
            return Refused("usage.store", $"'{CapabilitiesFile}' is not an object of switches.", exit: 2);
        }

        var unknown = switches.Select(entry => entry.Key).FirstOrDefault(name => !Capabilities.ContainsKey(name));
        if (unknown is not null)
        {
            return Refused("usage.store", $"'{CapabilitiesFile}' names '{unknown}', which is no capability this stand-in can turn off.", exit: 2);
        }

        var unread = switches.FirstOrDefault(entry => entry.Value is not JsonValue value || !value.TryGetValue<bool>(out _)).Key;
        return unread is null
            ? null
            : Refused("usage.store", $"'{CapabilitiesFile}' sets '{unread}' to something other than true or false.", exit: 2);
    }

    /// <summary>Whether the account's Reply offers a capability; one never switched off is offered.</summary>
    private static bool Offered(string root, string capability)
    {
        var file = Path.Combine(root, CapabilitiesFile);
        return !(File.Exists(file)
            && JsonNode.Parse(File.ReadAllText(file)) is JsonObject switches
            && switches[capability] is JsonValue value
            && value.TryGetValue<bool>(out var offered)
            && !offered);
    }

    /// <summary>
    /// What reply-cli prints when Reply's API has no route for the call, about the ids the call named; <paramref
    /// name="routed"/> is what the route names — a contact, or a send's reference — and <paramref name="route"/> the route
    /// where the capability's switch covers more than the one it lists: <c>direct_send</c>'s cancel.
    /// </summary>
    private static Ending NotOffered(string capability, object routed, JsonObject? ids, string? route = null) =>
        Refused(
            "reply.refused", "Reply refused the call.", status: 404,
            detail: "No endpoint matches " + string.Format(System.Globalization.CultureInfo.InvariantCulture, route ?? Capabilities[capability], routed) + ".",
            ids: ids);
}
