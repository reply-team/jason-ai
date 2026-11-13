import { screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { stubDesktop } from '../test/desktop';
import { FakeApi } from '../test/fake-api';
import { renderApp } from '../test/render-app';
import { loadMode, mode, modeRefusal, modeUnopened, switchMode } from './mode';

// The mode is which installation the desktop runs on, and the page learns it from the desktop alone, once, as it loads.
// A browser shows whatever runtime it is paired with: Live to the page, whatever its address says, and no strip.

afterEach(() => {
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
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

    it('carries the desktop’s sentence when the demo could not be opened, and the run is Live', async () => {
        const desktop = stubDesktop();
        desktop.modeUnopened = 'The demo could not be opened, so Jason opened your own installation.';
        expect(await loadMode(desktop)).toBe('live');
        expect(modeUnopened.value).toBe('The demo could not be opened, so Jason opened your own installation.');
        desktop.modeUnopened = null;
        await loadMode(desktop);
        expect(modeUnopened.value).toBeNull();
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
    it('draws no strip, even at an address that names the demo', async () => {
        history.replaceState(null, '', '/ui/?mode=demo');
        await loadMode();
        renderApp(
            new FakeApi().answer('approval.list', 'approval.list/empty').answer('decision.list', 'decision.list/empty'),
        );
        await waitFor(() => screen.getByRole('navigation'));
        expect(screen.queryByRole('region', { name: 'Demo' })).toBeNull();
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
