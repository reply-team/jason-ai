import type { Api } from '../api/client';
import type { CampaignSummary, Contact, Page } from '../api/types';

/**
 * The first ten campaigns, or the first ten whose name holds the text. Archived ones are offered only where asked for:
 * nobody adds people to one, but its numbers still count.
 */
export async function searchCampaigns(api: Api, query: string, archived = false): Promise<CampaignSummary[]> {
    const asked = { ...(query ? { query } : {}), limit: 10, ...(archived ? { include_archived: true } : {}) };
    const page = await api.call<Page<CampaignSummary>>('campaign.list', asked);
    return page.items;
}

/** The first ten people, or the first ten found by anything they are known by. */
export async function searchPeople(api: Api, query: string): Promise<Contact[]> {
    const page = await api.call<Page<Contact>>('contact.list', query ? { query, limit: 10 } : { limit: 10 });
    return page.items;
}

export function displayName(contact: Contact): string {
    const name = [contact.first_name, contact.last_name].filter((part) => part).join(' ');
    return name || firstChannel(contact) || contact.id;
}

/** The value a person is most recognised by: their email where they have one, else whatever channel comes first. */
export function firstChannel(contact: Contact): string | undefined {
    return (contact.channels.find((c) => c.channel === 'email') ?? contact.channels[0])?.value;
}
