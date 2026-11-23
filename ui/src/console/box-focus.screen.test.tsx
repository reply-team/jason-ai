import { act as flush, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { loadMode, mode } from '../platform/mode';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { setupFound, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import type { FixtureName } from '../test/fixtures';
import { agentFacts } from './agent-facts';
import * as consoleCampaigns from './campaigns';
import { openingWaiting } from './chat';
import { Console } from './console';
import { attention } from './needs';
import { agentFailure, agentMaking } from './new-agent';
import { closeMenus } from './side';
import { startDraft, startMaking, startWaiting } from './start';
import { forgetWaiting } from './waiting';
import { activePanel, chats, conversations, feedId, here, lines, setup, setupError } from './state';

// A chat the person opens by their own act has the caret in its box, so they can type at once: a row of Chats or of
// Agents, the Feed's row, each plus, a choice in Search, Jason's mark for Start, and Back to a chat. Nothing else moves
// the focus: arrowing along the rows, a row's menu and its rename, a pane shown by anything but the person, a field the
// person went on into, and a box that cannot take it.

const spring = 'conv_01JDKEPT0000000000000000S1';
const setupChat = 'conv_01JDKEPT0000000000000000U1';

/** Spring is a live Sequence with a conversation, Autumn a draft with none. */
function screenApi(): FakeApi {
    const campaignOf = (body: object) => (body as { campaign_id?: string }).campaign_id;
    const get: Record<string, FixtureName> = { cmp_1: 'campaign.get/details', cmp_2: 'campaign.get/unlinked' };
    return new FakeApi()
        .answerAnyRequest('approval.list', 'approval.list/empty')
        .answerAnyRequest('decision.list', 'decision.list/empty')
        .answerAnyRequest('preference.list', 'preference.list/set')
        .answerAnyRequest('campaign.list', 'campaign.list/screen')
        .answerAnyRequest('campaign.summary', 'campaign.summary/all')
        .answerAnyRequest('campaign.get', (body) => get[campaignOf(body) ?? ''])
        .answerAnyRequest('campaign.list_contacts', 'campaign.list_contacts/summary-waiting')
        .answerAnyRequest('workitem.list', 'workitem.list/creating-none')
        .answerAnyRequest('journal.list', 'journal.list/by-campaign')
        .answerAnyRequest('contact.threads', 'contact.threads/two-threads')
        .answerAnyRequest('plugin.list', 'plugin.list/none')
        .answerAnyRequest('route.list', 'route.list/empty')
        .answerAnyRequest('ui.tokens', 'ui.tokens/two')
        .answerAnyRequest('system.info', 'system.info/with-ui');
}

/** The draft a click on New agent makes, as the runtime's own tests recorded it: "New agent 1". */
function newAgentApi(): FakeApi {
    return new FakeApi()
        .answerAnyRequest('approval.list', 'approval.list/empty')
        .answerAnyRequest('decision.list', 'decision.list/empty')
        .answerAnyRequest('preference.list', 'preference.list/set')
        .answerAnyRequest('campaign.list', 'campaign.list/new-agent')
        .answerAnyRequest('campaign.summary', 'campaign.summary/new-agent')
        .answerAnyRequest('campaign.get', 'campaign.get/new-agent')
        .answerAnyRequest('campaign.list_contacts', 'campaign.list_contacts/summary-waiting')
        .answerAnyRequest('workitem.list', 'workitem.list/creating-none')
        .answerAnyRequest('journal.list', 'journal.list/by-campaign')
        .answerAnyRequest('plugin.list', 'plugin.list/none')
        .answerAnyRequest('route.list', 'route.list/empty')
        .answerAnyRequest('ui.tokens', 'ui.tokens/two')
        .answerAnyRequest('system.info', 'system.info/with-ui');
}

async function renderConsole(fake: FakeApi = screenApi()): Promise<StubDesktop> {
    const desktop = stubDesktop({ ...setupFound });
    desktop.keep({ id: spring, campaignId: 'cmp_1', title: 'Spring' });
    desktop.keep({ id: setupChat, title: 'Set-up' });
    desktop.agentCampaign = { id: 'cmp_1', name: 'New agent 1' };
    await loadMode(desktop);
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'chat' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    await session.bootstrap();
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    await waitFor(() => expect(document.querySelector('[data-pane="start"]')).not.toBeNull());
    await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
    return desktop;
}

const side = () => within(document.querySelector<HTMLElement>('.side')!);
const menu = () => within(side().getByRole('navigation', { name: 'Menu' }));
const agentsRow = (name: string) =>
    waitFor(() => within(side().getByRole('group', { name: 'Agents' })).getByRole('button', { name }));
const chatsRow = (name: string) =>
    waitFor(() => within(side().getByRole('group', { name: 'Chats' })).getByRole('button', { name }));
const needsRow = () => menu().getByRole('button', { name: /^Needs you/ });
const brand = () => side().getByRole('button', { name: 'Start' });
const pane = (name: string) => document.querySelector<HTMLElement>(`.area > .pane[data-pane="${name}"]`);
const chatBox = (name: string) =>
    pane(name)?.querySelector<HTMLTextAreaElement>('textarea[aria-label="Your message"]') ?? null;
const startBox = () =>
    pane('start')!.querySelector<HTMLTextAreaElement>('textarea[aria-label="Who do you want to reach?"]')!;

/** A click on a control, which takes the focus first as the browser's own click does; Enter and Space on it click it. */
function press(control: HTMLElement): void {
    control.focus();
    fireEvent.click(control);
}

/** The box of the pane, once it holds the focus. */
const focusedBox = (name: string) =>
    waitFor(() => {
        const box = chatBox(name);
        expect(box).not.toBeNull();
        expect(document.activeElement).toBe(box);
        return box!;
    });

/** Long enough for any focus asked for to have been given. */
const settled = () =>
    flush(async () => {
        await new Promise((resolve) => setTimeout(resolve, 250));
    });

const caretAtEnd = (box: HTMLTextAreaElement) => {
    expect(box.selectionStart).toBe(box.value.length);
    expect(box.selectionEnd).toBe(box.value.length);
};

async function leaveForNeeds(): Promise<void> {
    press(needsRow());
    await waitFor(() => expect(activePanel.value).toBe('needs'));
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
    agentFailure.value = null;
    agentMaking.value = false;
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
    startDraft.value = '';
    startWaiting.value = null;
    startMaking.value = null;
    openingWaiting.value = {};
});

afterEach(() => {
    cleanup();
    startMaking.value = null;
    mode.value = 'live';
});

describe('a chat the person opens', () => {
    it('from a row of Chats has the caret in its box, at the end of the words left there', async () => {
        await renderConsole();
        press(await chatsRow('Set-up'));
        const box = await focusedBox(`chat:${setupChat}`);
        fireEvent.change(box, { target: { value: 'Draft of a reply' } });
        await leaveForNeeds();
        press(await chatsRow('Set-up'));
        caretAtEnd(await focusedBox(`chat:${setupChat}`));
    });

    it('from a row of Agents has the caret in the agent’s box, with its conversation and with none yet', async () => {
        await renderConsole();
        press(await agentsRow('Spring'));
        await focusedBox(`chat:${spring}`);
        // Autumn has no conversation: its Chat is drawn unmade, and its box takes the focus as a conversation's does.
        press(await agentsRow('Autumn'));
        await focusedBox('chat:unmade:cmp_2');
    });

    it('from the Feed’s row has the caret in the Feed’s box', async () => {
        await renderConsole();
        await waitFor(() => expect(feedId.value).not.toBeNull());
        press(await waitFor(() => menu().getByRole('button', { name: /^Feed/ })));
        await focusedBox(`chat:${feedId.value}`);
    });

    it('from the plus over CHATS has the caret in the new chat’s box', async () => {
        const desktop = await renderConsole();
        press(side().getByRole('button', { name: 'New chat' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.create')).toBe(true));
        await waitFor(() => expect(activePanel.value?.startsWith('chat:conv_')).toBe(true));
        await focusedBox(activePanel.value!);
    });

    it('from the plus over AGENTS has the caret in the box of the new agent’s opening', async () => {
        await renderConsole(newAgentApi());
        press(side().getByRole('button', { name: 'New agent' }));
        const box = await waitFor(() => {
            const shown = document.querySelector<HTMLElement>('.area > .pane:not([hidden]) .agent-opening');
            expect(shown).not.toBeNull();
            const found = within(shown!).getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
            expect(document.activeElement).toBe(found);
            return found;
        });
        caretAtEnd(box);
    });

    it('from a choice in Search has the caret in its box: a conversation, and an agent with none yet', async () => {
        await renderConsole();
        await waitFor(() => expect(conversations.value.some((each) => each.id === spring)).toBe(true));
        const search = async (words: string) => {
            fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
            const input = await waitFor(() => within(document.body).getByRole('combobox'));
            fireEvent.change(input, { target: { value: words } });
            fireEvent.keyDown(input, { key: 'Enter' });
        };
        await search('spring');
        await focusedBox(`chat:${spring}`);
        await search('autumn');
        await focusedBox('chat:unmade:cmp_2');
    });

    it('from a row menu’s Open has the caret in its box', async () => {
        await renderConsole();
        const row = await chatsRow('Set-up');
        row.focus();
        fireEvent.keyDown(row, { key: 'F10', shiftKey: true });
        const opened = await waitFor(() => within(document.body).getByRole('menu'));
        fireEvent.click(within(opened).getByRole('menuitem', { name: 'Open' }));
        await focusedBox(`chat:${setupChat}`);
    });

    it('from the window’s New chat, by its menu or its key, has the caret in the new chat’s box', async () => {
        const desktop = await renderConsole();
        flush(() => desktop.command('chat.new'));
        await waitFor(() => expect(activePanel.value?.startsWith('chat:conv_')).toBe(true));
        await focusedBox(activePanel.value!);
    });

    it('reached again by Back has the caret in its box, as one opened from its row', async () => {
        await renderConsole();
        press(await agentsRow('Spring'));
        await focusedBox(`chat:${spring}`);
        await leaveForNeeds();
        expect(document.activeElement).toBe(needsRow());
        flush(() => history.back());
        await waitFor(() => expect(activePanel.value).toBe(`chat:${spring}`));
        await focusedBox(`chat:${spring}`);
    });
});

describe('Start reached by Jason’s mark', () => {
    it('has the caret in its box, at the end of the words typed there and not sent', async () => {
        await renderConsole();
        fireEvent.change(startBox(), { target: { value: 'CFOs of logistics firms in Ohio' } });
        await leaveForNeeds();
        press(brand());
        await waitFor(() => expect(document.activeElement).toBe(startBox()));
        caretAtEnd(startBox());
    });

    it('leaves the focus on the mark while its box cannot take it, and gives it to nothing else', async () => {
        await renderConsole();
        await leaveForNeeds();
        flush(() => {
            startMaking.value = 'CFOs of logistics firms in Ohio';
        });
        await waitFor(() => expect(startBox().disabled).toBe(true));
        press(brand());
        await waitFor(() => expect(activePanel.value).toBe('start'));
        await settled();
        expect(document.activeElement).toBe(brand());
    });
});

describe('the focus stays where it is', () => {
    it('while the arrows are pressed on a row: they open nothing', async () => {
        await renderConsole();
        const row = await agentsRow('Spring');
        row.focus();
        for (const key of ['ArrowDown', 'ArrowUp', 'ArrowDown']) fireEvent.keyDown(row, { key });
        await settled();
        expect(document.activeElement).toBe(row);
        expect(activePanel.value).toBe('start');
    });

    it('in a row’s menu and in its rename', async () => {
        await renderConsole();
        const row = await chatsRow('Set-up');
        row.focus();
        fireEvent.keyDown(row, { key: 'F10', shiftKey: true });
        const opened = await waitFor(() => within(document.body).getByRole('menu'));
        await waitFor(() => expect(opened.contains(document.activeElement)).toBe(true));
        await settled();
        expect(opened.contains(document.activeElement)).toBe(true);
        expect(activePanel.value).toBe('start');
        fireEvent.click(within(opened).getByRole('menuitem', { name: 'Rename conversation' }));
        const field = await waitFor(() => side().getByRole('textbox'));
        await waitFor(() => expect(document.activeElement).toBe(field));
        await settled();
        expect(document.activeElement).toBe(field);
    });

    it('when a chat is shown by anything but the person', async () => {
        await renderConsole();
        press(await chatsRow('Set-up'));
        await focusedBox(`chat:${setupChat}`);
        await leaveForNeeds();
        flush(() => here().dock.showChat(setupChat));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${setupChat}`));
        await settled();
        expect(document.activeElement).toBe(needsRow());
    });

    it('in a field the person went on into before the chat was drawn', async () => {
        await renderConsole();
        const row = await agentsRow('Spring');
        press(row);
        // Before the conversation is open the person is typing on Start's box: the chat, once shown, leaves it there.
        startBox().focus();
        await waitFor(() => expect(activePanel.value).toBe(`chat:${spring}`));
        await settled();
        expect(document.activeElement).toBe(startBox());
    });
});
