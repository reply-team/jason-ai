import { screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { stubDesktop } from '../test/desktop';
import { FakeApi } from '../test/fake-api';
import { renderApp } from '../test/render-app';
import { firstCampaignTab, parseRoute, route } from '../router';
import { setPlace } from './places';
import { loadMode, mode, modePreparing, modeRefusal, modeUnopened, switchMode } from './mode';

// The mode is which installation the desktop runs on, and the page learns it from the desktop alone, once, as it loads.
// A browser shows whatever runtime it is paired with: Live to the page, whatever its address says, and no bar.

afterEach(() => {
    mode.value = 'live';
    modePreparing.value = false;
    modeRefusal.value = null;
    modeUnopened.value = null;
    firstCampaignTab.value = 'sequence';
    history.replaceState(null, '', '/ui/');
});

describe('the mode read as the page loads', () => {
    it('in the desktop comes from the seam', async () => {
        const desktop = stubDesktop();
        desktop.modeKept = 'demo';
        expect(await loadMode(desktop)).toBe('demo');
        expect(mode.value).toBe('demo');
        desktop.modeKept = 'live';
        expect(await loadMode(desktop)).toBe('live');
    });

    it('makes Chat the first tab of an agent in Live where the place makes a chat, People where it cannot, and Sequence in the demo', async () => {
        const desktop = stubDesktop();
        try {
            setPlace('window');
            desktop.modeKept = 'live';
            await loadMode(desktop);
            expect(firstCampaignTab.value).toBe('chat');
            desktop.modeKept = 'demo';
            await loadMode(desktop);
            expect(firstCampaignTab.value).toBe('sequence');
            setPlace('none');
            await loadMode();
            expect(firstCampaignTab.value).toBe('people');
        } finally {
            setPlace('window');
        }
    });

    it('carries the desktop’s sentence when the demo could not be opened, and the run is Live', async () => {
        const desktop = stubDesktop();
        desktop.modeUnopened = 'The demo could not be opened, so Jason opened your own installation.';
        expect(await loadMode(desktop)).toBe('live');
        expect(modeUnopened.value).toBe('The demo could not be opened, so Jason opened your own installation.');
        desktop.modeUnopened = null;
        await loadMode(desktop);
        expect(modeUnopened.value).toBeNull();
    });

    it('says whether the demo’s sample is being made, and follows the desktop until it is ready', async () => {
        const desktop = stubDesktop();
        desktop.modeKept = 'demo';
        desktop.modePreparing = true;
        await loadMode(desktop);
        expect(modePreparing.value).toBe(true);
        desktop.preparing(false);
        expect(modePreparing.value).toBe(false);
        // A desktop that says nothing of it, and Live, are never preparing.
        expect(await loadMode({ mode: { get: () => Promise.resolve({ mode: 'demo', unopened: null }) } })).toBe('demo');
        expect(modePreparing.value).toBe(false);
        desktop.modeKept = 'live';
        await loadMode(desktop);
        expect(modePreparing.value).toBe(false);
    });

    it('follows the mark before it reads the mode, so a ready said between the two is not lost', async () => {
        let said: ((on: boolean) => void) | null = null;
        const order: string[] = [];
        const seam = {
            onPreparing(handler: (on: boolean) => void) {
                order.push('follow');
                said = handler;
                return () => undefined;
            },
            get() {
                order.push('get');
                // The desktop answers from before the sample was made, and says it is ready before the answer lands.
                said?.(false);
                return Promise.resolve({ mode: 'demo', unopened: null, preparing: true });
            },
        };
        expect(await loadMode({ mode: seam })).toBe('demo');
        expect(order).toEqual(['follow', 'get']);
        expect(modePreparing.value).toBe(false);
    });

    it('takes a bare word from a desktop that answers one, and says nothing unopened', async () => {
        expect(await loadMode({ mode: { get: () => Promise.resolve('demo') } })).toBe('demo');
        expect(modeUnopened.value).toBeNull();
        expect(await loadMode({ mode: { get: () => Promise.resolve({ mode: 'sample', unopened: 7 }) } })).toBe('live');
        expect(modeUnopened.value).toBeNull();
    });

    it('in a desktop without the member, or whose answer fails or is no mode, is Live', async () => {
        mode.value = 'demo';
        expect(await loadMode({})).toBe('live');
        mode.value = 'demo';
        expect(await loadMode({ mode: { get: () => Promise.reject(new Error('gone')) } })).toBe('live');
        mode.value = 'demo';
        expect(await loadMode({ mode: { get: () => Promise.resolve('sample') } })).toBe('live');
    });

    it('in a browser is Live, whatever the address says, and nothing is stored', async () => {
        localStorage.clear();
        sessionStorage.clear();
        mode.value = 'demo';
        history.replaceState(null, '', '/ui/?mode=demo#/people');
        expect(await loadMode()).toBe('live');
        expect(localStorage.length).toBe(0);
        expect(sessionStorage.length).toBe(0);
    });
});

describe('the browser shell', () => {
    it('draws no bar, even at an address that names the demo', async () => {
        history.replaceState(null, '', '/ui/?mode=demo');
        await loadMode();
        renderApp(
            new FakeApi().answer('approval.list', 'approval.list/empty').answer('decision.list', 'decision.list/empty'),
        );
        await waitFor(() => screen.getByRole('navigation'));
        expect(screen.queryByRole('region', { name: 'Mode bar' })).toBeNull();
    });
});

describe('switching', () => {
    it('in the desktop asks the seam, and leaves starting again to the desktop', async () => {
        const desktop = stubDesktop();
        await loadMode(desktop);
        await switchMode('live');
        expect(desktop.calls.filter((each) => each.name === 'mode.set')).toEqual([]);
        await switchMode('demo');
        expect(desktop.calls.filter((each) => each.name === 'mode.set')).toEqual([
            { name: 'mode.set', args: ['demo'] },
        ]);
        expect(modeRefusal.value).toBeNull();
        expect(mode.value).toBe('live');
    });

    it('in the desktop says the desktop’s refusal when the mode could not be kept', async () => {
        const desktop = stubDesktop();
        desktop.refuse.set('mode.set', 'The mode could not be saved, so Jason stays as it is.');
        await loadMode(desktop);
        await switchMode('demo');
        expect(modeRefusal.value).toBe('The mode could not be saved, so Jason stays as it is.');
        expect(mode.value).toBe('live');
    });

    it('the stand-in rejects a word that is neither mode, as the desktop does, and keeps nothing', async () => {
        const desktop = stubDesktop();
        await expect(desktop.mode.set('sample' as 'live')).rejects.toThrow('That is not a mode.');
        expect(desktop.modeKept).toBe('live');
        expect(desktop.calls.filter((each) => each.name === 'mode.set')).toEqual([]);
    });

    it('in a desktop too old for it says so, and asks nothing', async () => {
        await loadMode({ mode: { get: () => Promise.resolve('live') } });
        await switchMode('demo');
        expect(modeRefusal.value).toBe('This version of the desktop cannot switch to the demo.');
    });

    it('in a browser asks nothing and goes nowhere', async () => {
        history.replaceState(null, '', '/ui/#/people');
        await loadMode();
        await switchMode('demo');
        expect(location.href).toBe(`${location.origin}/ui/#/people`);
        expect(modeRefusal.value).toBeNull();
    });
});

describe('a reload on an agent’s bare address', () => {
    it.each([
        ['window', 'chat'],
        ['none', 'people'],
    ] as const)(
        'opens the place’s first tab in the console’s Live, the address being read again once the mode is known: in the %s, %s',
        async (place, tab) => {
            history.replaceState(null, '', '/ui/#/campaigns/cmp_x');
            // The router read the address as it loaded, before the first tab was known.
            route.value = parseRoute(location.hash);
            expect(route.value).toMatchObject({ id: 'cmp_x', tab: 'sequence' });
            setPlace(place);
            const desktop = stubDesktop();
            desktop.modeKept = 'live';
            await loadMode(desktop);
            expect(route.value).toMatchObject({ id: 'cmp_x', tab });
            setPlace('window');
        },
    );

    it('opens Sequence in the demo', async () => {
        history.replaceState(null, '', '/ui/#/campaigns/cmp_x');
        route.value = parseRoute(location.hash);
        const desktop = stubDesktop();
        desktop.modeKept = 'demo';
        await loadMode(desktop);
        expect(route.value).toMatchObject({ id: 'cmp_x', tab: 'sequence' });
    });
});
