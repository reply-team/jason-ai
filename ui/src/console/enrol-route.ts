import { signal } from '@preact/signals-react';
import type { RouteResolution } from '../api/types';
import { here } from './state';

// The Reply card in a campaign's chat is an offer, so it is hidden only on a positive answer that the campaign's work
// goes to another plugin. The runtime already answers where: `route.resolve` walks the levels that apply to an
// operation that takes no channel route, as this one does (the campaign's own operation route, its default, the global
// operation override, the global default) over the snapshot the claim reads.
// No route is not such an answer: on a fresh installation the sign-in the card offers is what writes the first route.

/** The provider operation whose route decides it: enrolment is the act a draft campaign cannot be started without. */
export const enrolOperation = 'campaign.enroll';

/** The plugin that performs Reply's operations. */
export const replyPlugin = 'reply';

/** Where a campaign's enrolment stands as last read: being read, routed to a plugin other than Reply, or not. */
export type EnrolRead = 'reading' | 'elsewhere' | 'open';

export const enrolReads = signal<Readonly<Record<string, EnrolRead>>>({});

/** Whether the card may be offered: the read is done and did not find the campaign routed to another plugin. */
export const offersReply = (campaignId: string): boolean => enrolReads.value[campaignId] === 'open';

const asked = new Map<string, number>();

/**
 * Reads where a campaign's enrolment goes. Another plugin hides the card; Reply, no route and a read that fails leave
 * it offered. A late answer for a campaign asked again is dropped.
 */
export async function readEnrolRoute(campaignId: string): Promise<void> {
    const mine = (asked.get(campaignId) ?? 0) + 1;
    asked.set(campaignId, mine);
    // Nothing shows while the route is being read, whatever an earlier read said.
    enrolReads.value = { ...enrolReads.value, [campaignId]: 'reading' };
    let read: EnrolRead;
    try {
        const resolved = await here().api.call<RouteResolution>('route.resolve', {
            campaign_id: campaignId,
            operation: enrolOperation,
        });
        read = resolved.plugin_id !== null && resolved.plugin_id !== replyPlugin ? 'elsewhere' : 'open';
    } catch {
        read = 'open';
    }
    if (asked.get(campaignId) !== mine) return;
    enrolReads.value = { ...enrolReads.value, [campaignId]: read };
}
