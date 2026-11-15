import { act as flush } from '@testing-library/react';
import { chats, here } from '../console/state';
import { registry } from '../platform/slots';
import { parseRoute, route } from '../router';

// What the console's one area shows, read from the page as a person sees it: the one pane not hidden.

/** The pane in sight; null before the console is mounted. */
export const shownPane = (): HTMLElement | null => document.querySelector<HTMLElement>('.area > .pane:not([hidden])');

/**
 * What the area shows: a panel's name (`start`, `needs`, `campaigns`, `activity`, `connect`, `dashboard`, `page`), or
 * `chat:<id>`; undefined before the console is mounted.
 */
export const shown = (): string | undefined => shownPane()?.dataset['pane'];

const names: Readonly<Record<string, string>> = {
    start: 'Start',
    dashboard: 'Dashboard',
    needs: 'Needs you',
    campaigns: 'Agents',
    activity: 'Activity',
    connect: 'Connect',
};

/**
 * What the area shows, by the name the sidebar gives it: a panel's, a chat's title, or `page:<screen>` for one of the
 * browser shell's screens; undefined before the console is mounted.
 */
export function inFront(): string | undefined {
    const pane = shown();
    if (pane === undefined) return undefined;
    if (pane.startsWith('chat:')) return chats.value[pane.slice('chat:'.length)]?.title ?? pane;
    if (pane === 'page') return `page:${route.value.screen}`;
    return names[pane] ?? pane;
}

const panels = { Agents: 'campaigns', Dashboard: 'dashboard', Activity: 'activity', Connect: 'connect' } as const;
const pages = {
    People: 'people',
    Accounts: 'accounts',
    Performance: 'performance',
    'Work items': 'workitems',
    System: 'system',
} as const;

/**
 * Brings a panel or a page forward the way a link or the palette does, for the places Live's sidebar does not draw a
 * row for (the demo's sidebar does, and `side.screen.test.tsx` holds both lists).
 */
export function bringForward(name: keyof typeof panels | keyof typeof pages): void {
    flush(() => {
        if (name in panels) here().dock.show(panels[name as keyof typeof panels]);
        else {
            const address = registry
                .list('screen')
                .find((each) => each.id === pages[name as keyof typeof pages])!
                .href();
            here().dock.page(parseRoute(address));
        }
    });
}
