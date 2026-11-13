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

// The desktop's console in Demo: the strip above every panel, its two buttons asking the desktop through the seam; in
// Live no strip, and the start screen offers the demo in one line.

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

const strip = () => screen.queryByRole('region', { name: 'Demo' });
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
    it('draws the strip once, above the sidebar and every panel of the dock', async () => {
        await renderConsole('demo');
        const shown = await waitFor(() => {
            expect(strip()).not.toBeNull();
            return strip()!;
        });
        expect(screen.getAllByRole('region', { name: 'Demo' })).toHaveLength(1);
        const console = document.querySelector('.jc-console')!;
        expect(console.classList.contains('demo')).toBe(true);
        expect(shown.parentElement).toBe(console);
        expect(shown.nextElementSibling?.classList.contains('shell')).toBe(true);
        expect(shown.textContent).toMatch(/sample data/);
        expect(shown.textContent).toMatch(/Nothing in it is real/);
        expect(shown.textContent).toMatch(/nothing is sent to anyone/);
        expect(shown.textContent).toMatch(/back to your own installation/);
    });

    it('keeps the one strip above the console whichever screen is in front', async () => {
        const desktop = await renderConsole('demo');
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?' }));
        expect(strip()).not.toBeNull();
        desktop.command('navigate.settings');
        await waitFor(() => screen.getByRole('heading', { name: 'Settings' }));
        expect(screen.getAllByRole('region', { name: 'Demo' })).toHaveLength(1);
        desktop.command('navigate.connect');
        await waitFor(() => expect(document.querySelector('.dv-tab.dv-active-tab')?.textContent).toMatch(/Connect/));
        expect(screen.getAllByRole('region', { name: 'Demo' })).toHaveLength(1);
    });

    it('has a short form beside the long one, for a narrow window', async () => {
        await renderConsole('demo');
        const shown = await waitFor(() => strip()!);
        const long = shown.querySelector('.mode-strip-long')?.textContent ?? '';
        const short = shown.querySelector('.mode-strip-short')?.textContent ?? '';
        expect(short).toMatch(/sample data/);
        expect(short.length).toBeLessThan(long.length / 2);
    });

    it('switches to Live through the seam, and Demo, the one marked, asks nothing', async () => {
        const desktop = await renderConsole('demo');
        const group = await waitFor(() => within(strip()!).getByRole('group', { name: 'Mode' }));
        const demo = within(group).getByRole('button', { name: 'Demo' });
        expect(demo.getAttribute('aria-pressed')).toBe('true');
        fireEvent.click(demo);
        fireEvent.click(within(group).getByRole('button', { name: 'Live' }));
        await waitFor(() => expect(modeSets(desktop)).toEqual([{ name: 'mode.set', args: ['live'] }]));
    });

    it('says the desktop’s refusal in the strip when the mode could not be kept', async () => {
        const desktop = await renderConsole('demo');
        desktop.refuse.set('mode.set', 'The mode could not be saved, so Jason stays as it is.');
        const group = await waitFor(() => within(strip()!).getByRole('group', { name: 'Mode' }));
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
    it('draws no strip', async () => {
        await renderConsole('live');
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?' }));
        expect(strip()).toBeNull();
        expect(document.querySelector('.jc-console')!.classList.contains('demo')).toBe(false);
    });

    it('offers the demo in one line on the start screen, which switches to Demo through the seam', async () => {
        const desktop = await renderConsole('live');
        const button = await waitFor(() => {
            expect(offer()).not.toBeNull();
            return offer()!;
        });
        expect(button.closest('.start')).not.toBeNull();
        fireEvent.click(button);
        await waitFor(() => expect(modeSets(desktop)).toEqual([{ name: 'mode.set', args: ['demo'] }]));
    });

    it('says the desktop’s refusal beside the offer when the mode could not be kept', async () => {
        const desktop = await renderConsole('live');
        desktop.refuse.set('mode.set', 'The mode could not be saved, so Jason stays as it is.');
        fireEvent.click(await waitFor(() => offer()!));
        const said = await waitFor(() => screen.getByText('The mode could not be saved, so Jason stays as it is.'));
        expect(said.getAttribute('role')).toBe('alert');
        expect(said.closest('.start')).not.toBeNull();
    });
});

describe('A demo the desktop could not open', () => {
    const unopened = 'The demo could not be opened, so Jason opened your own installation.';
    const note = () => screen.queryByRole('status', { name: 'Demo not opened' });

    it('says so above the console on the first screen, in the desktop’s words, with no demo strip', async () => {
        await renderConsole('live', unopened);
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?' }));
        const said = note()!;
        expect(said.textContent).toMatch(/^The demo could not be opened, so Jason opened your own installation\./);
        const console = document.querySelector('.jc-console')!;
        expect(said.parentElement).toBe(console);
        expect(said.nextElementSibling?.classList.contains('shell')).toBe(true);
        expect(strip()).toBeNull();
        expect(offer()).not.toBeNull();
    });

    it('is closed by the person, and the console is Live as before', async () => {
        await renderConsole('live', unopened);
        const said = await waitFor(() => note()!);
        fireEvent.click(within(said).getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(note()).toBeNull());
        expect(document.querySelector('.jc-console')!.classList.contains('demo')).toBe(false);
    });
});
