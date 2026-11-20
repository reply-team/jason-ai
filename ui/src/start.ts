import type { JasonDesktop } from './platform/desktop';

// The console is drawn in Jason Desktop's own window alone, which puts its object on the page before the page runs, and
// starts at once there, with nothing awaited before it. Any other place, a plain browser tab at the runtime's `/ui/`, gets
// one short message and nothing else: it reads no pairing and asks the runtime nothing.

export interface Starts {
    /** The window's object, undefined in a browser. */
    windowDesktop: () => JasonDesktop | undefined;
    /** Mounts the console over the window's object. */
    startConsole: (desktop: JasonDesktop) => Promise<void>;
    /** Draws the one message a plain browser tab gets. */
    showOwnPlace: () => Promise<void>;
}

export function start(deps: Starts): Promise<void> {
    const window = deps.windowDesktop();
    return window === undefined ? deps.showOwnPlace() : deps.startConsole(window);
}
