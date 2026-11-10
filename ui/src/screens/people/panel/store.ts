import { signal } from '@preact/signals-react';
import type { Api } from '../../../api/client';
import type {
    Contact,
    ContactCampaign,
    ContactUpdateRequest,
    Page,
    WorkItem,
    WorkItemCreateRequest,
    WorkItemSummary,
} from '../../../api/types';
import * as people from '../store';
import { fail, openStatuses } from '../store';

/** The work open for the person whose Next tab is shown, as workitem.list answers it, soonest due first. */
export const next = signal<{ contactId: string; items: WorkItemSummary[]; more: boolean } | null>(null);
export const nextFailure = signal<string | null>(null);
let nextGeneration = 0;

/** The same read the timeline's Planned rows make: the person's open work, by the runtime's own order. */
export async function loadNext(api: Api, contactId: string): Promise<void> {
    const mine = ++nextGeneration;
    nextFailure.value = null;
    if (next.value?.contactId !== contactId) next.value = null;
    try {
        const page = await api.call<Page<WorkItemSummary>>('workitem.list', {
            contact_id: contactId,
            status: openStatuses,
            order: 'due',
            limit: 50,
        });
        if (mine === nextGeneration) next.value = { contactId, items: page.items, more: page.next_cursor !== null };
    } catch (error) {
        if (mine === nextGeneration) fail(error, nextFailure);
    }
}

/** The campaigns a person is a live member of, in the order contact.get answered them: the ones an act for the person can go through. */
export function liveCampaigns(person: Contact): ContactCampaign[] {
    return (person.campaigns ?? []).filter((each) => each.state === 'enrolled' || each.state === 'paused');
}

/** The campaign an act uses unless the person picks another: the one the panel was opened from, else the first. */
export function defaultCampaign(person: Contact, openedFrom: string | undefined): string | null {
    const live = liveCampaigns(person);
    return (live.find((each) => each.campaign_id === openedFrom) ?? live[0])?.campaign_id ?? null;
}

/**
 * Saves the changes to the person as the paired person, and shows the record the runtime answered. contact.update
 * answers the record alone, so the campaigns and the latest line the panel had read stay as they were, here and in
 * the list beside it.
 */
export async function save(api: Api, changes: Omit<ContactUpdateRequest, 'actor'>): Promise<Contact> {
    const saved = await api.call<Contact>('contact.update', changes, 'write');
    const keep = (was: Contact): Contact => ({
        ...saved,
        campaigns: was.campaigns ?? null,
        last_activity: was.last_activity ?? null,
    });
    const open = people.person.value;
    if (open?.id === saved.id) people.person.value = keep(open);
    people.people.value = people.people.value.map((each) => (each.id === saved.id ? keep(each) : each));
    return saved;
}

/** Queues the provider's contact.update for the person in a campaign, with the input its document takes. */
export async function queueProviderUpdate(
    api: Api,
    campaignId: string,
    person: Contact,
    fields: Record<string, string | null>,
    reason: string | undefined,
): Promise<WorkItem> {
    const channel = person.channels.find((each) => each.primary) ?? person.channels[0];
    const body: Omit<WorkItemCreateRequest, 'actor'> = {
        campaign_id: campaignId,
        kind: 'provider_op',
        operation: 'contact.update',
        contact_id: person.id,
        context: { input: { channel: channel?.channel, fields } },
    };
    if (reason) body.reason = reason;
    return api.call<WorkItem>('workitem.create', body, 'write');
}
