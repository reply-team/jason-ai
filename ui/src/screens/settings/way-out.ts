import { route, type Route } from '../../router';
import { activePanel, here } from '../../console/state';

// The way out of Settings. The place the person was in when Settings opened is held here, in the page's memory and
// nowhere else: not in the address, not in storage, never sent to the runtime. A reload, or a link straight to
// Settings, leaves it empty, and the way out then goes to the start place.

type Place = { pane: string; route: Route };

let before: Place | null = null;

/** Whether the page shown is Settings. */
export const settingsShown = (): boolean => activePanel.value === 'page' && route.value.screen === 'settings';

/** Called as the dock is about to show Settings: remembers what is shown now, unless that is Settings already. */
export function rememberPlace(): void {
    if (settingsShown()) return;
    const pane = activePanel.value;
    before = pane === null ? null : { pane, route: route.value };
}

/** Forgets the place; the console does when it mounts, so one console's place is never another's. */
export function forgetPlace(): void {
    before = null;
}

/** Leaves Settings for the place it was opened from, else for the start place. */
export function leaveSettings(): void {
    const place = before;
    before = null;
    const dock = here().dock;
    if (place === null || place.pane === 'start') return dock.show('start');
    if (place.pane === 'page') return place.route.screen === 'settings' ? dock.show('start') : dock.page(place.route);
    // A chat, an agent's or any other, is shown again by its id.
    if (place.pane.startsWith('chat:')) return dock.showChat(place.pane.slice('chat:'.length));
    return dock.show(place.pane as Parameters<typeof dock.show>[0]);
}

/** What takes an Escape before Settings does: an open menu, list, dialog, the palette or the sidebar over the area. */
const takers = '[role="menu"], [role="listbox"], [role="dialog"], [role="alertdialog"], dialog[open]';
const fields = 'input, textarea, select, [contenteditable=""], [contenteditable="true"]';

/** The window's Escape while Settings is shown. A field in focus takes the first one (it only gives up its focus). */
export function escapeFromSettings(event: KeyboardEvent): void {
    if (event.key !== 'Escape' || event.defaultPrevented || !settingsShown()) return;
    const focused = document.activeElement;
    if (focused instanceof HTMLElement && focused.matches(fields) && focused.closest('[hidden]') === null) {
        focused.blur();
        return;
    }
    if (document.querySelector('.jc-console .mainc > .area[inert]') !== null) return;
    const taken = Array.from(document.querySelectorAll(takers)).some((each) => each.closest('[hidden]') === null);
    if (taken) return;
    leaveSettings();
}
