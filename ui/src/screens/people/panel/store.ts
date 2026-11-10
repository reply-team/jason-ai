import { signal } from '@preact/signals-react';
import type { Api } from '../../../api/client';
import type {
    AddContactsResult,
    Contact,
    ContactCampaign,
    ContactUpdateRequest,
    Page,
    RemoveContactsResult,
    Suppression,
    SuppressionRemoved,
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

/** Where an address stands on Jason's register: the entry, null when it is not on it, absent until it was read. */
export const register = signal<{ contactId: string; entries: Record<string, Suppression | null> } | null>(null);
export const registerFailure = signal<string | null>(null);

export function addressKey(channel: string, value: string): string {
    return `${channel}\n${value}`;
}

function note(contactId: string, channel: string, value: string, entry: Suppression | null): void {
    const was = register.value?.contactId === contactId ? register.value.entries : {};
    register.value = { contactId, entries: { ...was, [addressKey(channel, value)]: entry } };
}

/** Reads the register for each of the person's addresses not read yet while their panel is open. */
export async function loadRegister(api: Api, person: Contact): Promise<void> {
    if (register.value?.contactId !== person.id) register.value = { contactId: person.id, entries: {} };
    registerFailure.value = null;
    const unread = person.channels.filter((c) => !(addressKey(c.channel, c.value) in register.value!.entries));
    await Promise.all(
        unread.map(async ({ channel, value }) => {
            try {
                const page = await api.call<Page<Suppression>>('suppression.list', { channel, value });
                if (register.value?.contactId === person.id) note(person.id, channel, value, page.items[0] ?? null);
            } catch (error) {
                if (register.value?.contactId === person.id) fail(error, registerFailure);
            }
        }),
    );
}

/** Puts an address on Jason's register: Jason will not contact it. */
export async function doNotContact(api: Api, contactId: string, channel: string, value: string, reason?: string) {
    const body: Record<string, unknown> = { channel, value };
    if (reason) body['reason'] = reason;
    note(contactId, channel, value, await api.call<Suppression>('suppression.add', body, 'write'));
}

/** Takes an address off Jason's register, with the reason the runtime requires for it. */
export async function contactAgain(api: Api, contactId: string, channel: string, value: string, reason: string) {
    await api.call<SuppressionRemoved>('suppression.remove', { channel, value, reason }, 'write');
    note(contactId, channel, value, null);
}

/** Forgets what the panel read about a person: another person, or the same one opened again, is read afresh. */
export function forget(): void {
    register.value = null;
    registerFailure.value = null;
}

/**
 * Excludes the person from a campaign as the paired person, and reads them again so Sequences shows where they now
 * stand. What remove_contacts answered for the person is returned whole: the caller says what happened.
 */
export async function exclude(
    api: Api,
    campaignId: string,
    contactId: string,
    reason?: string,
    reread = true,
): Promise<RemoveContactsResult['items'][number] | undefined> {
    const body: Record<string, unknown> = { campaign_id: campaignId, contact_ids: [contactId] };
    if (reason) body['reason'] = reason;
    const result = await api.call<RemoveContactsResult>('campaign.remove_contacts', body, 'write');
    if (reread) await people.open(api, contactId);
    return result.items.find((each) => each.contact_id === contactId) ?? result.items[0];
}

/**
 * What a move came to: done; refused at the add, so nothing changed; or added to the new campaign with the step out
 * of the old one refused, so the person is in both. Nothing is taken back.
 */
export type MoveOutcome = { moved: true } | { notAdded: string } | { notRemoved: unknown };

/**
 * Moves the person between campaigns as two acts in order: campaign.add_contacts to the new one, then
 * campaign.remove_contacts from the old one, only once the add stands. The person is read again after any act went
 * through. A refused add is thrown as it came when the call itself failed.
 */
export async function move(
    api: Api,
    contactId: string,
    from: string,
    to: string,
    reason?: string,
): Promise<MoveOutcome> {
    const body: Record<string, unknown> = { campaign_id: to, contacts: [{ contact_id: contactId }] };
    if (reason) body['reason'] = reason;
    const added = await api.call<AddContactsResult>('campaign.add_contacts', body, 'write');
    const item = added.items.find((each) => each.contact_id === contactId) ?? added.items[0];
    if (item?.status !== 'added' && item?.status !== 'already_member')
        return { notAdded: item?.error?.message ?? item?.status ?? 'no answer for the person' };
    let outcome: MoveOutcome;
    try {
        const out = await exclude(api, from, contactId, reason, false);
        outcome =
            out?.status === 'removed' || out?.status === 'already_excluded'
                ? { moved: true }
                : { notRemoved: out?.error?.message ?? out?.status ?? 'no answer for the person' };
    } catch (error) {
        outcome = { notRemoved: error };
    }
    await people.open(api, contactId);
    return outcome;
}
