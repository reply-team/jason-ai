using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;

namespace Jason.Runtime.Contacts;

/// <summary>
/// The wire shape of a contact. Channels come out in one stable order — the primary first, then by channel
/// and value — so two reads of an unchanged contact are byte-identical and a diff between them means something.
/// </summary>
public static class ContactMapper
{
    public static ContactDto ToDto(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return new ContactDto(
            contact.PublicId,
            contact.FirstName,
            contact.LastName,
            contact.Company,
            contact.Title,
            contact.TimeZone,
            [.. contact.Channels
                .OrderByDescending(channel => channel.IsPrimary)
                .ThenBy(channel => channel.Channel, StringComparer.Ordinal)
                .ThenBy(channel => channel.Value, StringComparer.Ordinal)
                .Select(channel => new ChannelDto(channel.Channel, channel.Value, channel.Label, channel.IsPrimary, channel.Data))],
            contact.Custom,
            ExternalIdStore.ToDtos(contact.ExternalIds),
            ToOffset(contact.CreatedAt),
            ToOffset(contact.UpdatedAt),
            contact.ArchivedAt is { } archived ? ToOffset(archived) : null);
    }

    /// <summary>Timestamps are stored as UTC <see cref="DateTime"/> and leave the runtime as offsets.</summary>
    public static DateTimeOffset ToOffset(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
