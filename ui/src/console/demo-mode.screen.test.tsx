import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { loadMode, mode, modePreparing, modeRefusal, modeUnopened } from '../platform/mode';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { inFront } from '../test/area';
import { FakeApi, watch } from '../test/fake-api';
import { styleOf, tokenOf } from '../test/stylesheet';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations, lines, setup, setupError } from './state';
import { startDraft, startMaking, startWaiting } from './start';

// The desktop's console: the top strip right of the sidebar, above every panel in both modes, its switch asking the
// desktop through the seam; in Demo one short sentence says the data is sample, and Live says nothing but its pressed
// segment.

async function renderConsole(
    kept: 'live' | 'demo',
    unopened: string | null = null,
    preparing = false,
): Promise<StubDesktop> {
    const desktop = stubDesktop();
    desktop.modeKept = kept;
    desktop.modeUnopened = unopened;
    desktop.modePreparing = preparing;
    // The page reads the mode once as it loads, before the console is drawn (`mount.tsx`).
    await loadMode(desktop);
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
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

/** The strip heads the column right of the sidebar: the sidebar runs the window's height, the area is under the strip. */
function expectBesideTheSidebar(shown: HTMLElement): void {
    const column = shown.parentElement!;
    expect(column.classList.contains('mainc')).toBe(true);
    expect(column.previousElementSibling?.classList.contains('side')).toBe(true);
    expect(column.parentElement?.classList.contains('shell')).toBe(true);
    expect(column.parentElement?.parentElement).toBe(document.querySelector('.jc-console'));
    expect(shown.nextElementSibling?.classList.contains('area')).toBe(true);
}
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
    modePreparing.value = false;
    modeRefusal.value = null;
    modeUnopened.value = null;
});

describe('Demo in the desktop', () => {
    it('draws the bar once, right of the sidebar and above every panel of the area', async () => {
        await renderConsole('demo');
        const shown = await waitFor(() => {
            expect(strip()).not.toBeNull();
            return strip()!;
        });
        expect(screen.getAllByRole('region', { name: 'Mode bar' })).toHaveLength(1);
        expectBesideTheSidebar(shown);
        expect(shown.querySelector('.mode-mark')?.textContent).toBe(
            'Demo: an example of what you can build on Jason. The data is samples, and nothing is sent.',
        );
        expect(shown.querySelector('.mode-mark strong')?.textContent).toBe('Demo:');
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
        await waitFor(() => expect(inFront()).toBe('Connect'));
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

describe('A demo whose sample is being made', () => {
    const mark = () => within(strip()!).queryByRole('status', { name: 'Preparing the demo…' });
    const switches = () => within(within(strip()!).getByRole('group', { name: 'Data' })).getAllByRole('button');

    it('shows the page at once with a quiet mark, the switch off with its reason, and asks nothing when clicked', async () => {
        const desktop = await renderConsole('demo', null, true);
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?' }));
        expect(mark()?.textContent).toBe('Preparing the demo…');
        for (const button of switches()) {
            expect(button.hasAttribute('disabled')).toBe(true);
            expect(button.getAttribute('title')).toBe('Available once the demo is ready.');
        }
        fireEvent.click(within(strip()!).getByRole('button', { name: 'Live' }));
        expect(modeSets(desktop)).toEqual([]);
    });

    it('takes the mark away and turns the switch on when the desktop says the sample is ready', async () => {
        const desktop = await renderConsole('demo', null, true);
        await waitFor(() => expect(mark()).not.toBeNull());
        desktop.preparing(false);
        await waitFor(() => expect(mark()).toBeNull());
        for (const button of switches()) {
            expect(button.hasAttribute('disabled')).toBe(false);
            expect(button.getAttribute('title')).toBeNull();
        }
        fireEvent.click(within(strip()!).getByRole('button', { name: 'Live' }));
        await waitFor(() => expect(modeSets(desktop)).toEqual([{ name: 'mode.set', args: ['live'] }]));
    });

    it('shows no mark in a demo whose sample is made, nor in Live', async () => {
        await renderConsole('demo');
        await waitFor(() => strip()!);
        expect(mark()).toBeNull();
        expect(switches().every((button) => !button.hasAttribute('disabled'))).toBe(true);
    });
});

describe('Live in the desktop', () => {
    it('draws the bar with no mark and no demo sentence, the Live segment pressed', async () => {
        await renderConsole('live');
        const shown = await waitFor(() => strip()!);
        expect(shown.querySelector('.mode-mark')).toBeNull();
        expect(shown.querySelector('.mode-dot')).toBeNull();
        expect(shown.classList.contains('mode-live')).toBe(true);
        expect(shown.textContent).not.toMatch(/an example of what you can build/);
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
        expectBesideTheSidebar(strip()!);
        expect(strip()!.querySelector('.mode-mark')).toBeNull();
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

describe('The strip’s look', () => {
    it('stands 38 px high right of the 284 px sidebar, white in Live with a line under it, and is the window’s drag region', async () => {
        await renderConsole('live');
        const shown = await waitFor(() => strip()!);
        expectBesideTheSidebar(shown);
        expect(styleOf(document.querySelector('.jc-console .side')!)).toMatchObject({ width: '284px' });
        expect(styleOf(shown)).toMatchObject({
            height: '38px',
            'box-sizing': 'border-box',
            padding: '0 12px 0 16px',
            background: 'var(--bg)',
            'border-bottom': '1px solid var(--line)',
            color: 'var(--mute)',
            '-webkit-app-region': 'drag',
        });
    });

    it('keeps every control in it out of the drag region, the Open in menu included', async () => {
        await renderConsole('live');
        const shown = await waitFor(() => strip()!);
        const split = await waitFor(() => within(shown).getByRole('group', { name: 'Open in' }));
        fireEvent.click(within(split).getByRole('button', { name: 'More places to open' }));
        const menu = await waitFor(() => within(shown).getByRole('menu'));
        const controls = [
            ...Array.from(shown.querySelectorAll('button')),
            within(shown).getByRole('group', { name: 'Data' }),
            split,
            menu,
        ];
        expect(controls.length).toBeGreaterThan(5);
        for (const control of controls)
            expect(styleOf(control)['-webkit-app-region'], control.outerHTML.slice(0, 80)).toBe('no-drag');
    });

    it('draws Demo | Live as one segmented control: a quiet track, the mode in force a white pill', async () => {
        await renderConsole('live');
        const group = await waitFor(() => within(strip()!).getByRole('group', { name: 'Data' }));
        expect(styleOf(group)).toMatchObject({
            background: 'var(--seg-track)',
            'border-radius': '7px',
            padding: '2px',
        });
        const live = within(group).getByRole('button', { name: 'Live' });
        const demo = within(group).getByRole('button', { name: 'Demo' });
        expect(styleOf(demo)).toMatchObject({
            border: '0',
            background: 'none',
            'border-radius': '5px',
            'font-size': '12px',
        });
        expect(styleOf(live)).toMatchObject({
            background: 'var(--seg-on)',
            color: 'var(--seg-on-ink)',
            'font-weight': '600',
        });
        expect(tokenOf('--seg-track')).toBe('rgba(20, 33, 43, 0.08)');
        expect(tokenOf('--seg-on')).toBe('#fff');
        // Open in sits beside it on the same quiet track, its parts plain until pointed at.
        const split = await waitFor(() => within(strip()!).getByRole('group', { name: 'Open in' }));
        expect(styleOf(split)).toMatchObject({
            background: 'var(--seg-track)',
            'border-radius': '7px',
            padding: '2px',
        });
        for (const part of within(split).getAllByRole('button'))
            expect(styleOf(part)).toMatchObject({ border: '0', background: 'none', 'font-size': '12px' });
    });

    it('is the demo’s warm strip in Demo, its dot amber', async () => {
        await renderConsole('demo');
        const shown = await waitFor(() => strip()!);
        expect(styleOf(shown)).toMatchObject({
            background: 'var(--demo-bg)',
            'border-bottom': '1px solid var(--demo-line)',
            color: 'var(--demo-ink)',
        });
        expect([tokenOf('--demo-bg'), tokenOf('--demo-line'), tokenOf('--demo-ink')]).toEqual([
            '#fff4d6',
            '#f2dc9c',
            '#6b4e00',
        ]);
        expect(styleOf(shown.querySelector('.mode-dot')!)).toMatchObject({ background: '#e0a100' });
    });
});
