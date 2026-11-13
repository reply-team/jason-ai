import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { route } from '../router';
import { stubDesktop } from '../test/desktop';
import { FakeApi } from '../test/fake-api';
import { renderApp } from '../test/render-app';
import { addressBar, addressFor, loadMode, mode, modeOfAddress, modeRefusal, switchMode } from './mode';

// The mode, Live or Demo: read once as the page loads, from the desktop's seam in the desktop and from the address
// alone in a browser, never stored by the page; in Demo a strip above every screen says so and switches, in Live none.

const loaded: string[] = [];
const realLoad = addressBar.load;

/** Puts the page at an address, as a browser opened it. */
const at = (path: string) => history.replaceState(null, '', path);

beforeEach(() => {
    loaded.length = 0;
    addressBar.load = (url) => void loaded.push(url);
    modeRefusal.value = null;
    at('/ui/');
});

afterEach(() => {
    addressBar.load = realLoad;
    mode.value = 'live';
    modeRefusal.value = null;
    at('/ui/');
});

const strip = () => screen.queryByRole('region', { name: 'Demo' });
const answered = () =>
    new FakeApi().answer('approval.list', 'approval.list/empty').answer('decision.list', 'decision.list/empty');

describe('the mode in the address', () => {
    it('is Demo for mode=demo before the hash, and Live for anything else or nothing', () => {
        expect(modeOfAddress('?mode=demo')).toBe('demo');
        expect(modeOfAddress('?x=1&mode=demo')).toBe('demo');
        for (const live of ['', '?mode=live', '?mode=Demo', '?demo', '?mode=', '?mode=sample', '?modes=demo'])
            expect(modeOfAddress(live), live).toBe('live');
    });

    it('is switched by setting the parameter for Demo and taking it away for Live, the route and the rest kept', () => {
        const live = 'http://127.0.0.1:5000/ui/?x=1#/campaigns/cmp_1/people?filter=new';
        const demo = addressFor(live, 'demo');
        expect(demo).toBe('http://127.0.0.1:5000/ui/?x=1&mode=demo#/campaigns/cmp_1/people?filter=new');
        expect(addressFor(demo, 'live')).toBe(live);
        expect(addressFor('http://127.0.0.1:5000/ui/?mode=demo#/people', 'live')).toBe(
            'http://127.0.0.1:5000/ui/#/people',
        );
    });
});

describe('the mode read as the page loads', () => {
    it('in a browser comes from the address alone and is stored nowhere', async () => {
        localStorage.clear();
        sessionStorage.clear();
        at('/ui/?mode=demo#/people');
        expect(await loadMode()).toBe('demo');
        expect(mode.value).toBe('demo');
        expect(localStorage.length).toBe(0);
        expect(sessionStorage.length).toBe(0);
        at('/ui/#/people');
        expect(await loadMode()).toBe('live');
    });

    it('survives the page’s own routing, which moves the hash and keeps the parameter', async () => {
        at('/ui/?mode=demo#/attention');
        location.hash = '#/settings';
        history.replaceState(null, '', '#/people');
        expect(location.search).toBe('?mode=demo');
        expect(await loadMode()).toBe('demo');
    });

    it('in the desktop comes from the seam, whatever the address says', async () => {
        const desktop = stubDesktop();
        desktop.modeKept = 'demo';
        expect(await loadMode(desktop)).toBe('demo');
        desktop.modeKept = 'live';
        at('/ui/?mode=demo#/');
        expect(await loadMode(desktop)).toBe('live');
    });

    it('in a desktop without the member, or whose answer fails or is no mode, is Live', async () => {
        mode.value = 'demo';
        expect(await loadMode({})).toBe('live');
        mode.value = 'demo';
        expect(await loadMode({ mode: { get: () => Promise.reject(new Error('gone')) } })).toBe('live');
        mode.value = 'demo';
        expect(await loadMode({ mode: { get: () => Promise.resolve('sample') } })).toBe('live');
    });
});

describe('the strip in the browser shell', () => {
    it('is above every screen in Demo, saying the data is sample, nothing is sent and it starts fresh', async () => {
        at('/ui/?mode=demo');
        await loadMode();
        renderApp(answered());
        await waitFor(() => expect(strip()).not.toBeNull());
        const words = strip()!.textContent ?? '';
        expect(words).toMatch(/sample data/);
        expect(words).toMatch(/nothing is sent/i);
        expect(words).toMatch(/starts fresh/);
        route.value = { screen: 'settings' };
        await waitFor(() => screen.getByRole('heading', { name: 'Settings' }));
        expect(strip()).not.toBeNull();
        route.value = { screen: 'chat' };
        await waitFor(() => screen.getByRole('heading', { name: 'Chat' }));
        expect(strip()).not.toBeNull();
    });

    it('is not there in Live, on any screen', async () => {
        await loadMode();
        renderApp(answered());
        await waitFor(() => screen.getByRole('navigation'));
        expect(strip()).toBeNull();
        route.value = { screen: 'settings' };
        await waitFor(() => screen.getByRole('heading', { name: 'Settings' }));
        expect(strip()).toBeNull();
    });

    it('marks Demo and switches to Live by loading the address without the parameter', async () => {
        at('/ui/?mode=demo#/settings');
        await loadMode();
        renderApp(answered());
        const group = await waitFor(() => within(strip()!).getByRole('group', { name: 'Mode' }));
        const demo = within(group).getByRole('button', { name: 'Demo' });
        const live = within(group).getByRole('button', { name: 'Live' });
        expect(demo.getAttribute('aria-pressed')).toBe('true');
        expect(live.getAttribute('aria-pressed')).toBe('false');
        fireEvent.click(demo);
        expect(loaded).toEqual([]);
        fireEvent.click(live);
        expect(loaded).toHaveLength(1);
        const next = new URL(loaded[0]!);
        expect(next.origin + next.pathname).toBe(`${location.origin}/ui/`);
        expect(next.searchParams.has('mode')).toBe(false);
    });

    it('has a short form beside the long one, for a narrow window', async () => {
        at('/ui/?mode=demo');
        await loadMode();
        renderApp(answered());
        await waitFor(() => expect(strip()).not.toBeNull());
        const long = strip()!.querySelector('.mode-strip-long')?.textContent ?? '';
        const short = strip()!.querySelector('.mode-strip-short')?.textContent ?? '';
        expect(short.length).toBeGreaterThan(0);
        expect(short.length).toBeLessThan(long.length / 2);
    });
});

describe('switching', () => {
    it('in a browser to Demo loads the address with the parameter, and to the mode in force does nothing', async () => {
        at('/ui/#/people');
        await loadMode();
        await switchMode('live');
        expect(loaded).toEqual([]);
        await switchMode('demo');
        expect(loaded).toEqual([`${location.origin}/ui/?mode=demo#/people`]);
    });

    it('in the desktop asks the seam and leaves the reload to the desktop', async () => {
        const desktop = stubDesktop();
        await loadMode(desktop);
        await switchMode('demo');
        expect(desktop.calls.filter((each) => each.name === 'mode.set')).toEqual([
            { name: 'mode.set', args: ['demo'] },
        ]);
        expect(loaded).toEqual([]);
        expect(modeRefusal.value).toBeNull();
    });

    it('in the desktop says the desktop’s refusal when the mode could not be kept', async () => {
        const desktop = stubDesktop();
        desktop.refuse.set('mode.set', 'The mode could not be saved, so the window stays as it is.');
        await loadMode(desktop);
        await switchMode('demo');
        expect(modeRefusal.value).toBe('The mode could not be saved, so the window stays as it is.');
        expect(mode.value).toBe('live');
    });
});
