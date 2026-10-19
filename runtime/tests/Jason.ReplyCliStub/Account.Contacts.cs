using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The three commands on one contact by its Reply id — <c>contact get</c>, <c>contact update</c> and <c>contact delete</c>
/// — answered as reply-cli 0.13.0 documents them, from the same files the account keeps.
/// </summary>
internal static partial class Account
{
    /// <summary>reply-cli's names for the fields <c>contact update</c> takes, and Reply's names for them on the contact.</summary>
    private static readonly (string Field, string Name)[] UpdateFields =
        [("first_name", "firstName"), ("last_name", "lastName"), ("company", "company"), ("title", "title"), ("linkedin_url", "linkedInUrl")];

    /// <summary>The contact as Reply holds it: its fields, its opt-out, and its custom fields as Reply answers them.</summary>
    private static Ending ContactGet(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Contact id");
        }

        if (Find(Read(root, ContactsFile), id) is not { } contact)
        {
            return NoContact(id, ids: null);
        }

        var shown = new JsonObject();
        foreach (var name in new[] { "id", "email", "firstName", "lastName", "title", "company", "linkedInUrl", "callStatus", "meetingStatus" })
        {
            shown[name] = contact[name]?.DeepClone();
        }

        shown["isOptedOut"] = OptOuts(root).Contains(id);
        shown["customFields"] = contact["customFields"]?.DeepClone() ?? new JsonArray();
        return Done(shown);
    }

    /// <summary>
    /// Changes the fields stdin names and leaves the rest; a null clears one. A custom field is matched against the
    /// account's own, ignoring case, and written as the account spells it; one the account has none of is refused before
    /// anything is written. Every refusal says which contact it was about.
    /// </summary>
    private static Ending ContactUpdate(string root, Parsed line, string? stdin)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Contact id");
        }

        if (line.Option("--fields") != "-")
        {
            return Refused("usage.fields", "--fields takes only -: the fields are read from stdin.", exit: 2);
        }

        JsonObject given;
        try
        {
            given = JsonNode.Parse(stdin ?? string.Empty) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (JsonException)
        {
            return Refused("usage.fields", "The fields on stdin must be one JSON object.", exit: 2);
        }

        if (given.FirstOrDefault(member => member.Key != "custom_fields" && !UpdateFields.Any(field => field.Field == member.Key)) is { Key: { } stray })
        {
            return Refused("usage.fields", $"The fields have an unknown field '{stray}'.", exit: 2);
        }

        if (given.Count == 0)
        {
            return Refused("usage.fields", "The fields name nothing to change.", exit: 2);
        }

        var patch = new List<(string Name, string? Value)>();
        foreach (var (field, name) in UpdateFields)
        {
            if (!given.TryGetPropertyValue(field, out var raw))
            {
                continue;
            }

            var (value, bad) = FieldValue(raw, field);
            if (bad is not null)
            {
                return bad;
            }

            if (field == "linkedin_url" && value is not null)
            {
                value = LinkedInKey(value);
                if (value is null)
                {
                    return Refused("usage.fields", "linkedin_url must be an absolute http or https URL.", exit: 2);
                }
            }

            patch.Add((name, value));
        }

        var custom = new List<(string Name, string? Value)>();
        if (given.TryGetPropertyValue("custom_fields", out var listed))
        {
            if (listed is not JsonArray entries || entries.Count == 0)
            {
                return Refused("usage.fields", "custom_fields must be a list of at least one {name, value}.", exit: 2);
            }

            foreach (var entry in entries)
            {
                if (entry is not JsonObject pair || pair.Any(member => member.Key is not ("name" or "value")) || !pair.ContainsKey("value")
                    || pair["name"] is not JsonValue named || !named.TryGetValue<string>(out var text) || EcmaTrim(text).Length == 0)
                {
                    return Refused("usage.fields", "Each custom field must be {name, value}, with a name that is not blank.", exit: 2);
                }

                var name = EcmaTrim(text);
                if (custom.Any(seen => string.Equals(seen.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return Refused("usage.fields", "A custom field is named twice.", exit: 2);
                }

                var (value, bad) = FieldValue(pair["value"], "a custom field's value");
                if (bad is not null)
                {
                    return bad;
                }

                custom.Add((name, value));
            }
        }

        var ids = new JsonObject { ["contact_id"] = id };
        var resolved = new List<(string Title, string? Value)>();
        if (custom.Count > 0)
        {
            var titles = Records(root, CustomFieldsFile).Select(field => Text(field, "title")).Where(title => title.Length > 0).ToList();
            var missing = new JsonArray();
            foreach (var (name, value) in custom)
            {
                var title = titles.FirstOrDefault(held => held == name)
                    ?? titles.FirstOrDefault(held => string.Equals(held, name, StringComparison.OrdinalIgnoreCase));
                if (title is null)
                {
                    missing.Add(name);
                    continue;
                }

                resolved.Add((title, value));
            }

            if (missing.Count > 0)
            {
                return Refused(
                    "contact.field_not_found", "The account has no contact custom field by a name given, so nothing was changed.",
                    status: 200, ids: ids, details: new JsonObject { ["fields"] = missing });
            }
        }

        var contacts = Read(root, ContactsFile);
        if (Find(contacts, id) is not { } contact)
        {
            return NoContact(id, ids);
        }

        foreach (var (name, value) in patch)
        {
            contact[name] = value;
        }

        if (resolved.Count > 0)
        {
            var held = contact["customFields"] as JsonArray ?? [];
            foreach (var (title, value) in resolved)
            {
                foreach (var existing in held.OfType<JsonObject>().Where(entry => Text(entry, "key") == title).ToList())
                {
                    held.Remove(existing);
                }

                if (value is not null)
                {
                    held.Add(new JsonObject { ["key"] = title, ["value"] = value });
                }
            }

            contact["customFields"] = held.DeepClone();
        }

        Write(root, ContactsFile, contacts);
        return Done(new JsonObject { ["status"] = "updated", ["contact_id"] = id });
    }

    /// <summary>
    /// Deletes the contact with its history at Reply: its opt-out, its participations and its memberships go with it. A
    /// contact the account no longer holds is <c>contact.not_found</c>, which is what a repeat after a lost answer meets.
    /// </summary>
    private static Ending ContactDelete(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Contact id");
        }

        var ids = new JsonObject { ["contact_id"] = id };
        var contacts = Read(root, ContactsFile);
        if (Find(contacts, id) is not { } contact)
        {
            return NoContact(id, ids);
        }

        contacts.Remove(contact);
        Write(root, ContactsFile, contacts);

        var register = Read(root, OptOutsFile);
        foreach (var entry in register.Where(entry => entry!.GetValue<int>() == id).ToList())
        {
            register.Remove(entry);
        }

        Write(root, OptOutsFile, register);

        var enrollments = Read(root, EnrollmentsFile);
        foreach (var entry in enrollments.OfType<JsonObject>().Where(entry => entry["contactId"]!.GetValue<int>() == id).ToList())
        {
            enrollments.Remove(entry);
        }

        Write(root, EnrollmentsFile, enrollments);

        var lists = Read(root, ListsFile);
        foreach (var list in lists.OfType<JsonObject>())
        {
            var members = list["members"] as JsonArray ?? [];
            foreach (var member in members.Where(member => member!.GetValue<int>() == id).ToList())
            {
                members.Remove(member);
            }
        }

        Write(root, ListsFile, lists);
        return Done(new JsonObject { ["status"] = "deleted", ["contact_id"] = id });
    }

    /// <summary>A value as reply-cli takes one for a field: null to clear it, or a string that is not blank, trimmed.</summary>
    private static (string? Value, Ending? Refusal) FieldValue(JsonNode? raw, string label)
    {
        if (raw is null)
        {
            return (null, null);
        }

        if (raw is not JsonValue value || !value.TryGetValue<string>(out var text) || EcmaTrim(text).Length == 0)
        {
            return (null, Refused("usage.fields", $"{label} must be a string that is not blank, or null to clear it.", exit: 2));
        }

        return (EcmaTrim(text), null);
    }

    private static Ending NoContact(int id, JsonObject? ids) =>
        Refused("contact.not_found", $"This account has no contact {id}.", status: 404, replyCode: "contact.notFound", ids: ids);
}
