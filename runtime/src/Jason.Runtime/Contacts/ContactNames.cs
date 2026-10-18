using Jason.Runtime.Persistence;

namespace Jason.Runtime.Contacts;

/// <summary>What a person is called where a row names them: first and last, either alone, or null when the record has neither.</summary>
public static class ContactNames
{
    public static string? Display(Contact? contact)
    {
        if (contact is null)
        {
            return null;
        }

        var name = string.Join(' ', new[] { contact.FirstName, contact.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));
        return name.Length == 0 ? null : name;
    }
}
