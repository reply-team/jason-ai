import { chats } from '../console/state';
import { route } from '../router';

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
