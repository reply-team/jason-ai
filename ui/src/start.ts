import type { Bridge } from './platform/bridge';
import type { JasonDesktop } from './platform/desktop';
import type { PlaceKind } from './platform/places';

// Which shell the page starts, and over which object. In Jason Desktop's own window the console starts at once, with
// nothing awaited before it. In a browser the page first learns whether the desktop opened the tab with a bridge, which
// takes the pairing's bootstrap; then a tab with a bridge gets the console over the tab's object, and any other tab
// keeps the classic shell.

export interface Starts {
    /** The window's object, undefined in a browser. */
    windowDesktop: () => JasonDesktop | undefined;
    /** The page's pairing: `bootstrap` reads it, and `bridge` is the desktop's bridge this tab holds, once it is read. */
    session: { bootstrap: () => Promise<void>; bridge: { value: Bridge | null } };
    /** The object of a tab with a bridge. */
    tabDesktop: (bridge: Bridge) => JasonDesktop;
    setPlace: (kind: PlaceKind, desktop?: JasonDesktop) => void;
    /** Mounts the console; `bootstrapped` says the pairing is already read. */
    startConsole: (desktop: JasonDesktop, bootstrapped: boolean) => Promise<void>;
    startClassic: () => Promise<void>;
}

export async function start(deps: Starts): Promise<void> {
    const window = deps.windowDesktop();
    if (window !== undefined) {
        deps.setPlace('window', window);
        return deps.startConsole(window, false);
    }
    await deps.session.bootstrap();
    const bridge = deps.session.bridge.value;
    if (bridge === null) {
        // The classic shell has no desktop to ask: its place can do nothing of one.
        deps.setPlace('none');
        return deps.startClassic();
    }
    const tab = deps.tabDesktop(bridge);
    deps.setPlace('tab', tab);
    return deps.startConsole(tab, true);
}
