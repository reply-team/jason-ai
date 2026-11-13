import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { loadMode, mode, modeRefusal, modeUnopened } from '../platform/mode';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, setupFound, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, lines, setup, setupError } from './state';

// In the demo the page's acts that connect or change a real account are off and say why, once per card; in Live they
// are on. Reads stay: the accounts shown are whatever the installation holds. Connecting an AI is not one of them.

const sentence = 'Not in the demo: accounts are connected in your own installation.';

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

async function renderAt(kept: 'live' | 'demo', panel: 'Connect' | 'Accounts'): Promise<StubDesktop> {
    const desktop = chatDesktop({
        ...setupFound,
        plugins: setupFound.plugins.map((each) => (each.id === 'ntfy' ? { ...each, binding: true } : each)),
    });
    desktop.modeKept = kept;
    await loadMode(desktop);
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
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    const side = () => within(document.querySelector<HTMLElement>('.side')!);
    await waitFor(() => side().getByRole('button', { name: panel }));
    fireEvent.click(side().getByRole('button', { name: panel }));
    await waitFor(() => {
        if (panel === 'Connect') expect(document.querySelector('.plugin')).not.toBeNull();
        else expect(document.querySelector('.acct')).not.toBeNull();
    });
    return desktop;
}

const replyCard = () => within(document.body).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
const button = (inside: HTMLElement, name: string) => within(inside).getByRole('button', { name }) as HTMLButtonElement;
const notes = () => screen.queryAllByRole('note').filter((each) => words(each) === sentence);
const rowOf = (card: HTMLElement) =>
    within(card).getByText('Sales inbox', { selector: 'b' }).closest<HTMLElement>('.acct')!;

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
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
});

describe('The Connect panel in the demo', () => {
    it('keeps the strip on it, and says once on each of the Reply and Plugins cards that accounts are not connected here', async () => {
        await renderAt('demo', 'Connect');
        expect(screen.getAllByRole('region', { name: 'Demo' })).toHaveLength(1);
        expect(notes()).toHaveLength(2);
        expect(within(replyCard()).getAllByRole('note').map(words)).toContain(sentence);
    });

    it('turns off Sign in, the API key, Add account and every act on an account row, and still lists the accounts', async () => {
        const desktop = await renderAt('demo', 'Connect');
        const card = replyCard();
        expect(words(card)).toContain('Sales inbox');
        for (const name of ['Sign in', 'Connect', 'Add account']) expect(button(card, name).disabled).toBe(true);
        const acts = within(rowOf(card)).getAllByRole('button') as HTMLButtonElement[];
        expect(acts.map(words)).toEqual(['Limits', 'Unblock', 'Delete']);
        for (const each of acts) expect(each.disabled).toBe(true);
        for (const each of [...acts, button(card, 'Add account'), button(card, 'Sign in')]) fireEvent.click(each);
        expect(card.querySelector('.add-account')).toBeNull();
        expect(
            desktop.calls.filter((each) => each.name.startsWith('setup.reply') || each.name === 'setup.connectReply'),
        ).toEqual([]);
    });

    it('does not run a stale control either: the act itself refuses in the demo', async () => {
        const desktop = await renderAt('demo', 'Connect');
        const card = replyCard();
        const add = button(card, 'Add account');
        add.disabled = false;
        fireEvent.click(add);
        const del = button(rowOf(card), 'Delete');
        del.disabled = false;
        fireEvent.click(del);
        expect(card.querySelector('.add-account')).toBeNull();
        expect(desktop.calls.some((each) => each.name === 'setup.replyDelete')).toBe(false);
    });

    it('turns off configuring and disconnecting a notification plugin', async () => {
        const desktop = await renderAt('demo', 'Connect');
        const plugin = document.querySelector<HTMLElement>('.plugin[data-id="ntfy"]')!;
        for (const name of ['Configure', 'Disconnect', 'Use the public server']) {
            const each = button(plugin, name);
            expect(each.disabled).toBe(true);
            fireEvent.click(each);
        }
        expect(desktop.calls.some((each) => each.name === 'setup.configurePlugin')).toBe(false);
    });

    it('leaves connecting an AI alone: a program Install and a model key Keep stay on', async () => {
        await renderAt('demo', 'Connect');
        const ai = within(document.body).getByText('AI', { selector: 'b' }).closest<HTMLElement>('.card')!;
        expect(button(ai, 'Install').disabled).toBe(false);
        const keys = within(document.body).getByText('Model keys', { selector: 'b' }).closest<HTMLElement>('.card')!;
        expect(button(keys, 'Keep').disabled).toBe(false);
        expect(words(ai)).not.toContain(sentence);
        expect(words(keys)).not.toContain(sentence);
    });
});

describe('The Accounts page in the demo', () => {
    it('keeps the strip, lists the accounts, and says once that the Reply card acts are off', async () => {
        await renderAt('demo', 'Accounts');
        expect(screen.getAllByRole('region', { name: 'Demo' })).toHaveLength(1);
        const card = replyCard();
        expect(words(card)).toContain('Sales inbox');
        expect(within(card).getAllByRole('note').map(words)).toEqual([sentence]);
        expect(button(card, 'Add account').disabled).toBe(true);
    });
});

describe('The same screens in Live', () => {
    it('say nothing of the demo and keep every act on the Connect panel', async () => {
        await renderAt('live', 'Connect');
        expect(screen.queryByRole('region', { name: 'Demo' })).toBeNull();
        expect(notes()).toHaveLength(0);
        const card = replyCard();
        for (const name of ['Sign in', 'Connect', 'Add account']) expect(button(card, name).disabled).toBe(false);
        const plugin = document.querySelector<HTMLElement>('.plugin[data-id="ntfy"]')!;
        for (const name of ['Configure', 'Disconnect']) expect(button(plugin, name).disabled).toBe(false);
        fireEvent.click(button(card, 'Add account'));
        await waitFor(() => expect(replyCard().querySelector('.add-account')).not.toBeNull());
    });

    it('keep the Accounts page Reply card on, with no sentence', async () => {
        await renderAt('live', 'Accounts');
        expect(screen.queryByRole('region', { name: 'Demo' })).toBeNull();
        expect(notes()).toHaveLength(0);
        expect(button(replyCard(), 'Add account').disabled).toBe(false);
    });
});
