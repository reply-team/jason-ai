import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { loadMode, mode, modeRefusal, modeUnopened } from '../platform/mode';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations, lines, setup, setupError } from './state';
import { startDraft, startMaking, startWaiting } from './start';

// The desktop's console: the top bar above every panel in both modes, its switch asking the desktop through the seam;
// in Demo one short sentence says the data is sample, in Live a mark.

async function renderConsole(kept: 'live' | 'demo', unopened: string | null = null): Promise<StubDesktop> {
    const desktop = stubDesktop();
    desktop.modeKept = kept;
    desktop.modeUnopened = unopened;
    // The page reads the mode once as it loads, before the console is drawn (`mount.tsx`).
    await loadMode(desktop);
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('route.resolve', 'route.resolve/global-default');
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    fake.answer('system.info', 'system.info/with-ui');
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    await waitFor(() => expect(setup.value).not.toBeNull());
    return desktop;
}

const strip = () => screen.queryByRole('region', { name: 'Mode bar' });
const offer = () => screen.queryByRole('button', { name: 'See Jason with sample data' });
const modeSets = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'mode.set');

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    setup.value = null;
    setupError.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    startDraft.value = '';
    startWaiting.value = null;
    startMaking.value = null;
});

afterEach(() => {
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
});

describe('Demo in the desktop', () => {
    it('draws the bar once, above the sidebar and every panel of the dock', async () => {
        await renderConsole('demo');
        const shown = await waitFor(() => {
            expect(strip()).not.toBeNull();
            return strip()!;
        });
        expect(screen.getAllByRole('region', { name: 'Mode bar' })).toHaveLength(1);
        const console = document.querySelector('.jc-console')!;
        expect(shown.parentElement).toBe(console);
        expect(shown.nextElementSibling?.classList.contains('shell')).toBe(true);
        expect(shown.querySelector('.mode-mark')?.textContent).toBe('Demo data Nothing here is real or sent.');
        expect(shown.querySelector('.mode-mark strong')?.textContent).toBe('Demo data');
        expect(shown.textContent).not.toMatch(/fresh/i);
        expect(shown.classList.contains('mode-demo')).toBe(true);
    });

    it('keeps the one strip above the console whichever screen is in front', async () => {
        const desktop = await renderConsole('demo');
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?' }));
        expect(strip()).not.toBeNull();
        desktop.command('navigate.settings');
        await waitFor(() => screen.getByRole('heading', { name: 'Settings' }));
        expect(screen.getAllByRole('region', { name: 'Mode bar' })).toHaveLength(1);
        desktop.command('navigate.connect');
        await waitFor(() => expect(document.querySelector('.dv-tab.dv-active-tab')?.textContent).toMatch(/Connect/));
        expect(screen.getAllByRole('region', { name: 'Mode bar' })).toHaveLength(1);
    });

    it('has a short form beside the long one, for a narrow window', async () => {
        await renderConsole('demo');
        const shown = await waitFor(() => strip()!);
        const long = shown.querySelector('.mode-strip-long')?.textContent ?? '';
        const short = shown.querySelector('.mode-strip-short')?.textContent ?? '';
        expect(short).toBe('Sample data. Nothing is sent.');
        expect(short.length).toBeLessThan(long.length);
    });

    it('has one place for the Open in control, left of the switch', async () => {
        await renderConsole('demo');
        const shown = await waitFor(() => strip()!);
        const slot = shown.querySelector('[data-slot="open-in"]');
        expect(slot).not.toBeNull();
        expect(slot!.nextElementSibling).toBe(within(shown).getByRole('group', { name: 'Data' }));
    });

    it('switches to Live through the seam, and Demo, the one marked, asks nothing', async () => {
        const desktop = await renderConsole('demo');
        const group = await waitFor(() => within(strip()!).getByRole('group', { name: 'Data' }));
        const demo = within(group).getByRole('button', { name: 'Demo' });
        expect(demo.getAttribute('aria-pressed')).toBe('true');
        fireEvent.click(demo);
        fireEvent.click(within(group).getByRole('button', { name: 'Live' }));
        await waitFor(() => expect(modeSets(desktop)).toEqual([{ name: 'mode.set', args: ['live'] }]));
    });

    it('says the desktop’s refusal in the strip when the mode could not be kept', async () => {
        const desktop = await renderConsole('demo');
        desktop.refuse.set('mode.set', 'The mode could not be saved, so Jason stays as it is.');
        const group = await waitFor(() => within(strip()!).getByRole('group', { name: 'Data' }));
        fireEvent.click(within(group).getByRole('button', { name: 'Live' }));
        const alert = await waitFor(() => within(strip()!).getByRole('alert'));
        expect(alert.textContent).toBe('The mode could not be saved, so Jason stays as it is.');
    });

    it('offers no demo on the start screen', async () => {
        await renderConsole('demo');
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?' }));
        expect(offer()).toBeNull();
    });
});

describe('Live in the desktop', () => {
    it('draws the bar with a Live mark and no demo sentence, the Live button marked', async () => {
        await renderConsole('live');
        const shown = await waitFor(() => strip()!);
        expect(shown.querySelector('.mode-mark strong')?.textContent).toBe('Live');
        expect(shown.classList.contains('mode-live')).toBe(true);
        expect(shown.textContent).not.toMatch(/Nothing here is real/);
        expect(shown.querySelector('[data-slot="open-in"]')).not.toBeNull();
        const group = within(shown).getByRole('group', { name: 'Data' });
        expect(within(group).getByRole('button', { name: 'Live' }).getAttribute('aria-pressed')).toBe('true');
        expect(within(group).getByRole('button', { name: 'Demo' }).getAttribute('aria-pressed')).toBe('false');
    });

    it('switches to Demo through the seam from the bar', async () => {
        const desktop = await renderConsole('live');
        const group = await waitFor(() => within(strip()!).getByRole('group', { name: 'Data' }));
        fireEvent.click(within(group).getByRole('button', { name: 'Live' }));
        fireEvent.click(within(group).getByRole('button', { name: 'Demo' }));
        await waitFor(() => expect(modeSets(desktop)).toEqual([{ name: 'mode.set', args: ['demo'] }]));
    });

    it('offers no demo on the start screen, the bar’s switch being the way in', async () => {
        await renderConsole('live');
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?' }));
        expect(offer()).toBeNull();
    });

    it('says the desktop’s refusal in the bar when the mode could not be kept', async () => {
        const desktop = await renderConsole('live');
        desktop.refuse.set('mode.set', 'The mode could not be saved, so Jason stays as it is.');
        const group = await waitFor(() => within(strip()!).getByRole('group', { name: 'Data' }));
        fireEvent.click(within(group).getByRole('button', { name: 'Demo' }));
        const alert = await waitFor(() => within(strip()!).getByRole('alert'));
        expect(alert.textContent).toBe('The mode could not be saved, so Jason stays as it is.');
    });
});

describe('A demo the desktop could not open', () => {
    const unopened = 'The demo could not be opened, so Jason opened your own installation.';
    const note = () => screen.queryByRole('status', { name: 'Demo not opened' });

    it('says so in the bar on the first screen, in the desktop’s words', async () => {
        await renderConsole('live', unopened);
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?' }));
        const said = note()!;
        expect(said.textContent).toMatch(/^The demo could not be opened, so Jason opened your own installation\./);
        expect(strip()!.contains(said)).toBe(true);
        expect(strip()!.nextElementSibling?.classList.contains('shell')).toBe(true);
        expect(strip()!.querySelector('.mode-mark strong')?.textContent).toBe('Live');
        expect(offer()).toBeNull();
    });

    it('is closed by the person, and the console is Live as before', async () => {
        await renderConsole('live', unopened);
        const said = await waitFor(() => note()!);
        fireEvent.click(within(said).getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(note()).toBeNull());
        expect(strip()).not.toBeNull();
    });
});
