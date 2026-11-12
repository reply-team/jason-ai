import { signal } from '@preact/signals-react';
import type { RouteResolution } from '../api/types';
import { here } from './state';

// Whether a campaign's chat should ask for Reply is a question of where the campaign's work goes. The runtime already
// answers it: `route.resolve` walks the four levels (the campaign's own operation route, its default, the global
// operation override, the global default) over the snapshot the claim reads. The page asks that and reads nothing of
// settings or plugins itself.

/** The provider operation whose route decides it: enrolment is the act a draft campaign cannot be started without. */
export const enrolOperation = 'campaign.enroll';

/** The plugin that performs Reply's operations. */
export const replyPlugin = 'reply';

/** The campaigns whose enrolment, as last read, goes through Reply. Anything not listed shows nothing. */
export const routedToReply = signal<readonly string[]>([]);

const asked = new Map<string, number>();

/**
 * Reads where a campaign's enrolment goes and records whether that is Reply. No route, another plugin and a read that
 * fails all record the same: not Reply, so nothing is shown. A late answer for a campaign asked again is dropped.
 */
export async function readEnrolRoute(campaignId: string): Promise<void> {
    const mine = (asked.get(campaignId) ?? 0) + 1;
    asked.set(campaignId, mine);
    // Nothing shows while the route is being read, whatever an earlier read said.
    routedToReply.value = routedToReply.value.filter((each) => each !== campaignId);
    let reply: boolean;
    try {
        const resolved = await here().api.call<RouteResolution>('route.resolve', {
            campaign_id: campaignId,
            operation: enrolOperation,
        });
        reply = resolved.plugin_id === replyPlugin;
    } catch {
        reply = false;
    }
    if (asked.get(campaignId) !== mine) return;
    if (reply) routedToReply.value = [...routedToReply.value, campaignId];
}
