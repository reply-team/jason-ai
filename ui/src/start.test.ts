import { describe, expect, it, vi } from 'vitest';
import type { Bridge } from './platform/bridge';
import type { JasonDesktop } from './platform/desktop';
import { start, type Starts } from './start';

// The page's start: in the desktop's own window the console starts at once, with nothing awaited first; in a browser the
// pairing is read, and only then is the shell chosen.

const windowObject = { platform: 'darwin' } as JasonDesktop;
const tabObject = { platform: 'linux' } as JasonDesktop;
const noneObject = { platform: 'win32' } as JasonDesktop;
const bridge = {} as Bridge;

function deps(over: Partial<Starts> & { bridge?: Bridge | null } = {}) {
    const order: string[] = [];
    const bootstrap = vi.fn(async () => {
        order.push('bootstrap');
    });
    const made: Starts = {
        windowDesktop: () => undefined,
        session: { bootstrap, bridge: { value: over.bridge ?? null } },
        tabDesktop: vi.fn(() => tabObject),
        noDesktop: vi.fn(() => noneObject),
        setPlace: vi.fn((kind: string) => void order.push(`place:${kind}`)),
        startConsole: vi.fn(async (_desktop: JasonDesktop, bootstrapped: boolean) => {
            order.push(`console:${bootstrapped}`);
        }),
        ...over,
    };
    return { made, bootstrap, order };
}

describe('the start of the page', () => {
    it('starts the console in the window at once, awaiting nothing and reading no pairing first', () => {
        const { made, bootstrap, order } = deps({ windowDesktop: () => windowObject });
        void start(made);
        // Nothing has been awaited: the console was started in the same turn the page started.
        expect(order).toEqual(['place:window', 'console:false']);
        expect(made.startConsole).toHaveBeenCalledWith(windowObject, false);
        expect(bootstrap).not.toHaveBeenCalled();
        expect(made.tabDesktop).not.toHaveBeenCalled();
    });

    it('reads the pairing in a browser, then starts the console over the tab object when a bridge is held', async () => {
        const { made, order } = deps({ bridge });
        await start(made);
        expect(order).toEqual(['bootstrap', 'place:tab', 'console:true']);
        expect(made.tabDesktop).toHaveBeenCalledWith(bridge);
        expect(made.startConsole).toHaveBeenCalledWith(tabObject, true);
    });

    it('starts the console over the object that does nothing of the desktop in a browser tab with no bridge', async () => {
        const { made, order } = deps();
        await start(made);
        // The place that has no desktop is named, so it can do nothing of one.
        expect(order).toEqual(['bootstrap', 'place:none', 'console:true']);
        expect(made.startConsole).toHaveBeenCalledWith(noneObject, true);
        expect(made.tabDesktop).not.toHaveBeenCalled();
    });
});
