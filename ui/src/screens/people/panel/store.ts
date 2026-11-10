import { signal } from '@preact/signals-react';
import type { Api } from '../../../api/client';
import type { Page, WorkItemSummary } from '../../../api/types';
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
