import { describe, expect, it, vi } from 'vitest';
import type { JasonDesktop } from './platform/desktop';
import { start, type Starts } from './start';

// The page's start: in the desktop's own window the console starts at once over the window's object; anywhere else the
// page shows its one message and never the console.

const windowObject = { platform: 'darwin' } as JasonDesktop;

function deps(windowDesktop: () => JasonDesktop | undefined) {
    const order: string[] = [];
    const made: Starts = {
        windowDesktop,
        startConsole: vi.fn(async () => void order.push('console')),
        showOwnPlace: vi.fn(async () => void order.push('message')),
    };
    return { made, order };
}

describe('the start of the page', () => {
    it('starts the console in the window at once, over the window’s object, and shows no message', () => {
        const { made, order } = deps(() => windowObject);
        void start(made);
        // Nothing has been awaited: the console was started in the same turn the page started.
        expect(order).toEqual(['console']);
        expect(made.startConsole).toHaveBeenCalledWith(windowObject);
        expect(made.showOwnPlace).not.toHaveBeenCalled();
    });

    it('shows the one message in a browser, and never the console', async () => {
        const { made, order } = deps(() => undefined);
        await start(made);
        expect(order).toEqual(['message']);
        expect(made.startConsole).not.toHaveBeenCalled();
    });
});
