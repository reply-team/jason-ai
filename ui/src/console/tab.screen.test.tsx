import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import { loadMode, mode, modeRefusal, modeUnopened } from '../platform/mode';
import { setPlace, tabDesktop } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { standInBridge, standInSecret } from '../test/bridge';
import { chatDesktop, setupFound, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, lines, setup, setupError } from './state';

// The console in a browser tab the desktop opened: the console over the tab's object, drawn as the window draws it.
// What the bridge's table does not carry is drawn off, in its place, with one sentence; Open in is not drawn at all; and
// no member outside the table is ever asked of the bridge.

const elsewhere = "Done in Jason Desktop's own window.";

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

async function renderTab(panel: 'Connect' | 'Dashboard' = 'Connect') {
    const desktop = chatDesktop({
        ...setupFound,
        plugins: setupFound.plugins.map((each) => (each.id === 'ntfy' ? { ...each, binding: true } : each)),
    });
    const stand = standInBridge(desktop);
    const tab = tabDesktop(new Bridge({ port: 43210, secret: standInSecret }, stand.fetch));
    setPlace('tab', tab);
    await loadMode(tab);
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('ui.tokens', 'ui.tokens/two')
        .answer('system.info', 'system.info/with-ui');
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
    // The page's start reads the pairing before it starts the console in a tab.
    await session.bootstrap();
    render(<Console api={fake} desktop={tab} stream={stubStream().stream} bootstrapped />);
    const side = () => within(document.querySelector<HTMLElement>('.side')!);
    if (panel === 'Connect') {
        await waitFor(() => side().getByRole('button', { name: 'More' }));
        fireEvent.click(side().getByRole('button', { name: 'More' }));
        fireEvent.click(side().getByRole('menuitem', { name: /^Connect/ }));
        await waitFor(() => expect(document.querySelector('.plugin')).not.toBeNull());
    } else await waitFor(() => expect(document.querySelector('.side')).not.toBeNull());
    return { desktop, stand };
}

beforeEach(() => {
    setup.value = null;
    setupError.value = null;
    chats.value = {};
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

afterEach(() => {
    cleanup();
    setPlace('window');
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
});

describe('The console in a tab', () => {
    it('is drawn, with the mode bar, and has no Open in', async () => {
        await renderTab();
        expect(screen.getAllByRole('region', { name: 'Mode bar' })).toHaveLength(1);
        expect(document.querySelector('[data-slot="open-in"]')?.children).toHaveLength(0);
    });

    it('draws the Demo | Live switch off, with its sentence on each button', async () => {
        await renderTab();
        const group = screen.getByRole('group', { name: 'Data' });
        const buttons = within(group).getAllByRole('button') as HTMLButtonElement[];
        expect(buttons.length).toBe(2);
        for (const each of buttons) {
            expect(each.disabled).toBe(true);
            expect(each.title).toBe(elsewhere);
        }
    });

    it('shows what Connect found, and draws every act off with the one sentence on each card', async () => {
        const { desktop } = await renderTab();
        const notes = screen.getAllByRole('note').filter((each) => words(each) === elsewhere);
        expect(notes.length).toBeGreaterThanOrEqual(2);
        const reply = within(document.body).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
        // Checking again is a read, which the bridge carries.
        const acts = (within(reply).getAllByRole('button') as HTMLButtonElement[]).filter(
            (each) => words(each) !== 'Check again',
        );
        expect(acts.length).toBeGreaterThan(1);
        for (const each of acts) {
            expect(each.disabled, words(each)).toBe(true);
            fireEvent.click(each);
        }
        expect(desktop.calls.filter((each) => each.name.startsWith('setup.') && each.name !== 'setup.status')).toEqual(
            [],
        );
    });

    it('asks the bridge only for members of its table while the console starts', async () => {
        const { stand } = await renderTab();
        await waitFor(() => expect(stand.asked.some((each) => each.member === 'setup.status')).toBe(true));
        const table = new Set(['mode.get', 'theme.get', 'fullAccess.standing', 'setup.status', 'chat.list']);
        const members = stand.asked.filter((each) => each.path === '/call').map((each) => each.member ?? '');
        for (const member of members) expect(table.has(member), member).toBe(true);
    });
});
