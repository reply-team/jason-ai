import { act as flush, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { loadMode, mode } from '../platform/mode';
import { setPlace } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { parseRoute, route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { setupFound, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { fixtures, type FixtureName } from '../test/fixtures';
import { agentFacts } from './agent-facts';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { closeMenus } from './side';
import { forgetWaiting } from './waiting';
import { activePanel, chats, conversations, feedId, lines, setup, setupError } from './state';

// A right-click on a row of Recent, or Shift+F10 or the menu key on it, opens the acts its own view offers, in place: it
// never opens the row by itself. The agent's moves keep their own dialog, which names the agent; each conversation act
// shows that row's chat first and acts there, as the agent's own "…" does.

const spring = 'conv_01JDKEPT0000000000000000S1';
const autumn = 'conv_01JDKEPT0000000000000000A1';
const winter = 'conv_01JDKEPT0000000000000000W1';
const setupChat = 'conv_01JDKEPT0000000000000000U1';

const archivedWinter = fixtures['campaign.get/archived'].response;

/** Spring is live, Autumn a draft, Winter archived. */
const agents: Record<string, FixtureName> = {
    cmp_1: 'campaign.get/details',
    cmp_2: 'campaign.get/unlinked',
    cmp_4: 'campaign.get/archived',
};

function api(): FakeApi {
    const campaignOf = (body: object) => (body as { campaign_id?: string }).campaign_id;
    return new FakeApi()
        .answerAnyRequest('approval.list', 'approval.list/empty')
        .answerAnyRequest('decision.list', 'decision.list/empty')
        .answerAnyRequest('preference.list', 'preference.list/set')
        .answerAnyRequest('campaign.list', 'campaign.list/screen')
        .answerAnyRequest('campaign.summary', (body) => {
            const ids = (body as { campaign_ids?: string[] }).campaign_ids ?? [];
            if (ids.length !== 1) return 'campaign.summary/all';
            return ids[0] === 'cmp_4' ? 'campaign.summary/archived' : 'campaign.summary/some';
        })
        .answerAnyRequest('campaign.get', (body) => agents[campaignOf(body) ?? ''])
        .answerAnyRequest('campaign.list_contacts', 'campaign.list_contacts/summary-waiting')
        .answerAnyRequest('workitem.list', 'workitem.list/creating-none')
        .answerAnyRequest('journal.list', 'journal.list/by-campaign')
        .answerAnyRequest('contact.threads', 'contact.threads/two-threads')
        .answerAnyRequest('plugin.list', 'plugin.list/none')
        .answerAnyRequest('route.list', 'route.list/empty')
        .answerAnyRequest('ui.tokens', 'ui.tokens/two')
        .answerAnyRequest('system.info', 'system.info/with-ui');
}

async function renderConsole(): Promise<StubDesktop> {
    const desktop = stubDesktop({ ...setupFound });
    desktop.keep({ id: spring, campaignId: 'cmp_1', title: 'Spring' });
    desktop.keep({ id: autumn, campaignId: 'cmp_2', title: 'Autumn' });
    desktop.keep({ id: winter, campaignId: 'cmp_4', title: 'Winter' });
    desktop.keep({ id: setupChat, title: 'Set-up' });
    setPlace('window');
    await loadMode(desktop);
    const fake = api();
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = parseRoute('');
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    await session.bootstrap();
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} bootstrapped />);
    await waitFor(() => expect(document.querySelector('[data-pane]')).not.toBeNull());
    // The agents are listed, so each row's state is known.
    await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
    return desktop;
}

const side = () => document.querySelector<HTMLElement>('.side')!;
const recent = () => within(side()).getByRole('navigation', { name: 'Conversations' });
const row = (name: string) => waitFor(() => within(recent()).getByRole('button', { name }));
const menu = () => document.querySelector<HTMLElement>('.row-menu');
const words = (at: HTMLElement) =>
    [...at.querySelectorAll('[role="menuitem"], [role="separator"]')].map((each) =>
        each.getAttribute('role') === 'separator' ? '-' : each.textContent,
    );
const item = (name: string) => within(menu()!).getByRole('menuitem', { name });
const restarts = (desktop: StubDesktop) =>
    desktop.calls.filter((each) => each.name === 'chat.restart').map((each) => each.args[0]);

/** Holds the desktop's answer to one chat act on one conversation until the test lets it go, counting what it was asked. */
function holdAnswer(desktop: StubDesktop, act: 'open' | 'restart', id: string) {
    const chat = desktop.chat as unknown as Record<string, (...args: unknown[]) => Promise<unknown>>;
    const real = chat[act]!;
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
        release = resolve;
    });
    let asked = 0;
    chat[act] = (...args: unknown[]) => {
        if (args[0] !== id) return real(...args);
        asked += 1;
        return gate.then(() => real(...args));
    };
    return {
        asked: () => asked,
        release: () =>
            flush(async () => {
                release();
                await gate;
            }),
    };
}

/** Opens the row's menu by a right-click and picks one of its acts. */
async function pick(name: string, act: string): Promise<void> {
    fireEvent.contextMenu(await row(name));
    await waitFor(() => menu()!);
    fireEvent.click(item(act));
}

beforeEach(() => {
    closeMenus();
    setup.value = null;
    setupError.value = null;
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    feedId.value = null;
    agentFacts.value = {};
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.summary.value = {};
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    classicCampaigns.campaign.value = null;
    forgetWaiting();
});

afterEach(() => {
    cleanup();
    closeMenus();
    setPlace('window');
    mode.value = 'live';
});

describe('The menu on a row of Recent', () => {
    it('opens on a right-click at the pointer, named by the row, with Open focused, and opens nothing by itself', async () => {
        await renderConsole();
        const before = activePanel.value;
        const springRow = await row('Spring');
        // The page takes the right-click from the system: no other menu is drawn over it.
        expect(fireEvent.contextMenu(springRow, { clientX: 40, clientY: 300 })).toBe(false);
        const opened = await waitFor(() => menu()!);
        expect(opened.getAttribute('role')).toBe('menu');
        expect(opened.getAttribute('aria-label')).toBe('Spring');
        expect(document.activeElement).toBe(item('Open'));
        expect((await row('Spring')).classList.contains('menu-open')).toBe(true);
        expect(activePanel.value).toBe(before);
        expect(document.querySelector(`[data-pane="chat:${spring}"]`)).toBeNull();
    });

    it('opens from the keyboard on a focused row, by Shift+F10 and by the menu key, and Escape gives the focus back', async () => {
        await renderConsole();
        for (const key of [{ key: 'F10', shiftKey: true }, { key: 'ContextMenu' }]) {
            const springRow = await row('Spring');
            springRow.focus();
            fireEvent.keyDown(springRow, key);
            const opened = await waitFor(() => menu()!);
            expect(document.activeElement).toBe(item('Open'));
            fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
            expect(document.activeElement).toBe(item('Pause'));
            fireEvent.keyDown(opened, { key: 'Escape' });
            await waitFor(() => expect(menu()).toBeNull());
            expect(document.activeElement).toBe(await row('Spring'));
            expect((await row('Spring')).classList.contains('menu-open')).toBe(false);
        }
    });

    it('offers each agent the move its state allows and Archive…, an archived one neither, and a chat about no agent its conversation’s acts', async () => {
        await renderConsole();
        const rest = ['-', 'Rename conversation', 'Start conversation fresh', '-', 'Remove conversation'];
        fireEvent.contextMenu(await row('Spring'));
        await waitFor(() => expect(words(menu()!)).toEqual(['Open', 'Pause', 'Archive…', ...rest]));
        closeMenus();
        fireEvent.contextMenu(await row('Autumn'));
        await waitFor(() => expect(words(menu()!)).toEqual(['Open', 'Start', 'Archive…', ...rest]));
        closeMenus();
        // Winter, archived, is not among the agents listed: the menu asks nothing, and offers no move and no Archive….
        fireEvent.contextMenu(await row('Winter'));
        await waitFor(() => expect(words(menu()!)).toEqual(['Open', ...rest]));
        flush(() => {
            agentFacts.value = { cmp_4: { campaign: { ...archivedWinter }, creating: false } };
        });
        expect(words(menu()!)).toEqual(['Open', ...rest]);
        closeMenus();
        fireEvent.contextMenu(await row('Set-up'));
        await waitFor(() => expect(words(menu()!)).toEqual(['Open', ...rest]));
    });

    it('opens a move’s dialog named for the row’s agent, where another conversation is shown', async () => {
        await renderConsole();
        fireEvent.click(await row('Set-up'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${setupChat}`));
        fireEvent.contextMenu(await row('Spring'));
        await waitFor(() => menu()!);
        fireEvent.click(item('Pause'));
        const dialog = await waitFor(() => document.querySelector<HTMLElement>('[role="dialog"]')!);
        expect(dialog.getAttribute('aria-label')).toBe('Pause Spring');
        expect(menu()).toBeNull();
        // The move is made where it was asked: the conversation shown stays, and the runtime is asked nothing yet.
        expect(activePanel.value).toBe(`chat:${setupChat}`);
    });

    it('opens Archive…’s own dialog, named for the row’s agent, where another agent is shown', async () => {
        await renderConsole();
        fireEvent.click(await row('Spring'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${spring}`));
        fireEvent.contextMenu(await row('Autumn'));
        await waitFor(() => menu()!);
        fireEvent.click(item('Archive…'));
        const dialog = await waitFor(() => document.querySelector<HTMLElement>('[role="dialog"]')!);
        expect(dialog.getAttribute('aria-label')).toBe('Archive Autumn');
        expect(activePanel.value).toBe(`chat:${spring}`);
    });

    it('shows the row’s chat first for a conversation act, and acts there as the agent’s “…” does', async () => {
        const desktop = await renderConsole();
        fireEvent.contextMenu(await row('Spring'));
        await waitFor(() => menu()!);
        fireEvent.click(item('Rename conversation'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${spring}`));
        await waitFor(() => expect(chats.value[spring]?.renaming).toBe('Spring'));
        expect(location.hash).toBe('#/campaigns/cmp_1/chat');
        // Start fresh on a row not shown: the chat is shown, then it begins afresh.
        fireEvent.contextMenu(await row('Set-up'));
        await waitFor(() => menu()!);
        fireEvent.click(item('Start conversation fresh'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${setupChat}`));
        await waitFor(() =>
            expect(desktop.calls.filter((each) => each.name === 'chat.restart').map((each) => each.args[0])).toEqual([
                setupChat,
            ]),
        );
        // Remove shows the chat with its own question; nothing is removed until it is answered.
        fireEvent.contextMenu(await row('Autumn'));
        await waitFor(() => menu()!);
        fireEvent.click(item('Remove conversation'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${autumn}`));
        await waitFor(() => expect(chats.value[autumn]?.removing).toBe(true));
        expect(desktop.calls.filter((each) => each.name === 'chat.remove')).toEqual([]);
    });

    it('starts nothing afresh and removes nothing while the row’s conversation has a turn running', async () => {
        const desktop = await renderConsole();
        fireEvent.click(await row('Spring'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${spring}`));
        flush(() => {
            chats.value = { ...chats.value, [spring]: { ...chats.value[spring]!, running: true } };
        });
        fireEvent.contextMenu(await row('Spring'));
        await waitFor(() => menu()!);
        expect(item('Start conversation fresh')).toHaveProperty('disabled', true);
        expect(item('Remove conversation')).toHaveProperty('disabled', true);
        expect(desktop.calls.filter((each) => each.name === 'chat.restart' || each.name === 'chat.remove')).toEqual([]);
    });

    it('closes on a press outside it and on closeMenus, and a right-click outside a row is the system’s', async () => {
        await renderConsole();
        fireEvent.contextMenu(await row('Spring'));
        await waitFor(() => menu()!);
        fireEvent.mouseDown(document.body);
        await waitFor(() => expect(menu()).toBeNull());
        fireEvent.contextMenu(await row('Spring'));
        await waitFor(() => menu()!);
        flush(() => closeMenus());
        await waitFor(() => expect(menu()).toBeNull());
        // Elsewhere in the page a right-click is left to the system, and opens nothing.
        const brand = side().querySelector<HTMLElement>('nav') ?? side();
        expect(fireEvent.contextMenu(brand)).toBe(true);
        expect(fireEvent.contextMenu(document.body)).toBe(true);
        expect(menu()).toBeNull();
    });

    it('closes when its row is no longer drawn', async () => {
        await renderConsole();
        fireEvent.contextMenu(await row('Spring'));
        await waitFor(() => menu()!);
        flush(() => {
            conversations.value = conversations.value.filter((each) => each.id !== spring);
        });
        await waitFor(() => expect(menu()).toBeNull());
    });

    it('says on the row that it has a menu, and whether it is open', async () => {
        await renderConsole();
        const closed = await row('Spring');
        expect(closed.getAttribute('aria-haspopup')).toBe('menu');
        expect(closed.getAttribute('aria-expanded')).toBe('false');
        fireEvent.contextMenu(closed);
        await waitFor(() => menu()!);
        expect((await row('Spring')).getAttribute('aria-haspopup')).toBe('menu');
        expect((await row('Spring')).getAttribute('aria-expanded')).toBe('true');
        expect((await row('Autumn')).getAttribute('aria-expanded')).toBe('false');
        fireEvent.keyDown(menu()!, { key: 'Escape' });
        await waitFor(() => expect(menu()).toBeNull());
        expect((await row('Spring')).getAttribute('aria-expanded')).toBe('false');
    });

    it('moves round its acts by ArrowUp, Home and End, and Tab leaves it for the row', async () => {
        await renderConsole();
        fireEvent.contextMenu(await row('Spring'));
        const opened = await waitFor(() => menu()!);
        expect(document.activeElement).toBe(item('Open'));
        fireEvent.keyDown(opened, { key: 'ArrowUp' });
        expect(document.activeElement).toBe(item('Remove conversation'));
        fireEvent.keyDown(opened, { key: 'Home' });
        expect(document.activeElement).toBe(item('Open'));
        fireEvent.keyDown(opened, { key: 'End' });
        expect(document.activeElement).toBe(item('Remove conversation'));
        fireEvent.keyDown(opened, { key: 'Tab' });
        await waitFor(() => expect(menu()).toBeNull());
        expect(document.activeElement).toBe(await row('Spring'));
    });

    it('is not opened again by the system’s own event that follows Shift+F10', async () => {
        await renderConsole();
        const springRow = await row('Spring');
        springRow.focus();
        fireEvent.keyDown(springRow, { key: 'F10', shiftKey: true });
        await waitFor(() => menu()!);
        fireEvent.keyDown(menu()!, { key: 'ArrowDown' });
        expect(document.activeElement).toBe(item('Pause'));
        fireEvent.contextMenu(await row('Spring'), { button: 0 });
        expect(menu()).not.toBeNull();
        expect(document.activeElement).toBe(item('Pause'));
    });

    it('gives the focus to the first act it can take when the one focused is held, so its keys still work', async () => {
        await renderConsole();
        fireEvent.click(await row('Spring'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${spring}`));
        const springRow = await row('Spring');
        springRow.focus();
        fireEvent.keyDown(springRow, { key: 'ContextMenu' });
        await waitFor(() => menu()!);
        fireEvent.keyDown(menu()!, { key: 'End' });
        expect(document.activeElement).toBe(item('Remove conversation'));
        flush(() => {
            chats.value = { ...chats.value, [spring]: { ...chats.value[spring]!, running: true } };
        });
        await waitFor(() => expect(item('Remove conversation')).toHaveProperty('disabled', true));
        expect(document.activeElement).toBe(item('Open'));
        fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
        await waitFor(() => expect(menu()).toBeNull());
        expect(document.activeElement).toBe(await row('Spring'));
    });

    it('leaves the focus on the row or what the act shows after each act, never on the page itself', async () => {
        await renderConsole();
        const acts = [
            ['Spring', 'Open'],
            ['Autumn', 'Rename conversation'],
            ['Set-up', 'Start conversation fresh'],
            ['Winter', 'Remove conversation'],
        ] as const;
        for (const [name, act] of acts) {
            await pick(name, act);
            await waitFor(() => expect(menu()).toBeNull());
            await flush(async () => undefined);
            expect(document.activeElement).not.toBe(document.body);
            expect(document.activeElement).not.toBeNull();
        }
    });

    it('lets an act a later one overtook while it waited show nothing', async () => {
        const desktop = await renderConsole();
        const open = holdAnswer(desktop, 'open', spring);
        await pick('Spring', 'Rename conversation');
        await waitFor(() => expect(open.asked()).toBe(1));
        fireEvent.click(await row('Autumn'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${autumn}`));
        await open.release();
        expect(activePanel.value).toBe(`chat:${autumn}`);
        expect(location.hash).toBe('#/campaigns/cmp_2/chat');
        expect(chats.value[spring]?.renaming ?? null).toBeNull();
    });

    it('takes no second act on a conversation while one is under way', async () => {
        const desktop = await renderConsole();
        const restart = holdAnswer(desktop, 'restart', setupChat);
        await pick('Set-up', 'Start conversation fresh');
        await waitFor(() => expect(restart.asked()).toBe(1));
        await pick('Set-up', 'Start conversation fresh');
        await flush(async () => undefined);
        expect(restart.asked()).toBe(1);
        await restart.release();
        expect(restarts(desktop)).toEqual([setupChat]);
    });

    it('decides again once the chat is shown, and neither starts afresh nor removes while a turn it did not know of runs', async () => {
        const desktop = await renderConsole();
        const cases = [
            [spring, 'Spring', 'cmp_1', 'Start conversation fresh'],
            [autumn, 'Autumn', 'cmp_2', 'Remove conversation'],
        ] as const;
        for (const [id, name, campaignId, act] of cases) {
            const open = holdAnswer(desktop, 'open', id);
            fireEvent.contextMenu(await row(name));
            await waitFor(() => menu()!);
            // Taken while nothing said the conversation runs.
            expect(item(act)).toHaveProperty('disabled', false);
            fireEvent.click(item(act));
            await waitFor(() => expect(open.asked()).toBe(1));
            // Meanwhile a turn of it started, which the chat learns as it opens.
            desktop.keep({ id, campaignId, title: name, running: true });
            await open.release();
            await waitFor(() => expect(activePanel.value).toBe(`chat:${id}`));
            expect(chats.value[id]?.running).toBe(true);
            expect(chats.value[id]?.removing ?? false).toBe(false);
        }
        expect(restarts(desktop)).toEqual([]);
        expect(desktop.calls.filter((each) => each.name === 'chat.remove')).toEqual([]);
    });

    it('stands where it was opened, inside the window, measured again when its acts change', async () => {
        await renderConsole();
        const size = { width: window.innerWidth, height: window.innerHeight };
        Object.defineProperty(window, 'innerWidth', { value: 800, configurable: true });
        Object.defineProperty(window, 'innerHeight', { value: 600, configurable: true });
        const measured = HTMLElement.prototype.getBoundingClientRect;
        const spy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
            this: HTMLElement,
        ) {
            if (!this.classList.contains('row-menu')) return measured.call(this);
            const height = 30 * this.querySelectorAll('[role="menuitem"]').length;
            return { x: 0, y: 0, top: 0, left: 0, width: 230, height, right: 230, bottom: height, toJSON: () => ({}) };
        });
        try {
            fireEvent.contextMenu(await row('Spring'), { clientX: 780, clientY: 590, button: 2 });
            const opened = await waitFor(() => menu()!);
            // Six acts, 180 high: kept 4 inside the right and the bottom.
            expect(opened.style.getPropertyValue('--row-menu-x')).toBe('566px');
            expect(opened.style.getPropertyValue('--row-menu-y')).toBe('416px');
            // The agent is no longer listed: four acts, 120 high, placed again.
            flush(() => {
                consoleCampaigns.campaigns.value = consoleCampaigns.campaigns.value.filter(
                    (each) => each.id !== 'cmp_1',
                );
            });
            await waitFor(() => expect(within(menu()!).getAllByRole('menuitem')).toHaveLength(4));
            expect(menu()!.style.getPropertyValue('--row-menu-y')).toBe('476px');
        } finally {
            spy.mockRestore();
            Object.defineProperty(window, 'innerWidth', { value: size.width, configurable: true });
            Object.defineProperty(window, 'innerHeight', { value: size.height, configurable: true });
        }
    });

    it('closes when Recent scrolls or the window changes size', async () => {
        await renderConsole();
        fireEvent.contextMenu(await row('Spring'));
        await waitFor(() => menu()!);
        fireEvent.scroll(recent());
        await waitFor(() => expect(menu()).toBeNull());
        fireEvent.contextMenu(await row('Spring'));
        await waitFor(() => menu()!);
        fireEvent(window, new Event('resize'));
        await waitFor(() => expect(menu()).toBeNull());
    });
});
