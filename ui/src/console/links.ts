// Pure: where a `jason://` link takes the console. The desktop parsed the address from a closed table and hands the page
// only the page it names; the console opens that panel, or that screen at the entity, and does nothing else: no turn, no
// text put into a chat, no answer to anything, no mode, no setting.
import type { DesktopLink } from '../platform/desktop';
import { parseRoute, type Route } from '../router';

export type LinkPlace =
    | { panel: 'needs'; open?: { kind: 'approvals' | 'questions'; id: string } }
    | { panel: 'campaigns' }
    | { route: Route };

const at = (path: string, id: string): Route => parseRoute(`#/${path}/${encodeURIComponent(id)}`);

/** The panel or screen a link opens; null for a value that is no page of the table. */
export function linkPlace(link: DesktopLink): LinkPlace | null {
    if (link === null || typeof link !== 'object') return null;
    const id = 'id' in link && typeof link.id === 'string' ? link.id : null;
    switch (link.page) {
        case 'needs':
            return { panel: 'needs' };
        case 'campaigns':
            return { panel: 'campaigns' };
        case 'approval':
            return id === null ? null : { panel: 'needs', open: { kind: 'approvals', id } };
        case 'question':
            return id === null ? null : { panel: 'needs', open: { kind: 'questions', id } };
        case 'campaign':
            return id === null ? null : { route: at('campaigns', id) };
        case 'workitem':
            return id === null ? null : { route: at('workitems/now', id) };
        case 'person':
            return id === null ? null : { route: at('people', id) };
        default:
            return null;
    }
}
