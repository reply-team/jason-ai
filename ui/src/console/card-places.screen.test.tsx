import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import type { DesktopConversationRecord, DesktopKeptEntry } from '../platform/desktop';
import { loadMode, mode } from '../platform/mode';
import { setPlace, tabDesktop } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { standInBridge, standInSecret } from '../test/bridge';
import { setupFound, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { nothingWaitsInChats } from '../test/waiting';
import { acts } from './card-acts';
import { cardReads } from './card-frame';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, feedId, feedUnseen } from './state';
import { forgetWaiting } from './waiting';

// The two cards the chat's AI asks for that only lead to a place of the window: the card that opens Connect, where
// the AI hosts and the model keys are, and the card that opens Accounts, the Accounts page, where Reply's sign-in and its
// accounts are. Each is its header, one sentence of the window's and one button. Neither reads anything, so neither
// says when it was read or has a Refresh, and neither turns an earlier card of facts into a stub; neither holds a key,
// a login, a form or a link, and a click on its button opens the place and does nothing else.

const setupChat = 'conv_01JDPLACES000000000000000S';
const fenced = (value: unknown) => `\`\`\`card\n${JSON.stringify(value)}\n\`\`\``;
const asking = (kind: string, before = 'Here is where that is done.'): DesktopKeptEntry => ({
    kind: 'text',
    text: `${before}\n\n${fenced({ kind })}`,
});
const turn = (kind: string): DesktopKeptEntry[] => [{ kind: 'user', text: 'Where do I set that up?' }, asking(kind)];
const setUp = (entries: DesktopKeptEntry[]) => ({ id: setupChat, campaignId: null, title: 'Setting up', entries });

const aiSentence = 'The AI hosts and the model keys are in Connect.';
const replySentence = 'Reply’s sign-in and its accounts are on the Accounts page.';

function answering(fake: FakeApi): FakeApi {
    if (!fake.offers('plugin.list')) fake.answer('plugin.list', 'plugin.list/card-accounts-reply');
    if (!fake.offers('route.list')) fake.answer('route.list', 'route.list/card-accounts-reply');
    nothingWaitsInChats(fake)
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
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
    return fake;
}

function renderConsole(
    records: (Partial<DesktopConversationRecord> & { id: string })[],
    desktop: StubDesktop = stubDesktop(setupFound),
): { fake: FakeApi; desktop: StubDesktop } {
    const fake = answering(new FakeApi());
    for (const record of records) desktop.keep(record);
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return { fake, desktop };
}

const shownPane = () => document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;

async function openChat(title: string): Promise<HTMLElement> {
    await waitFor(() => {
        const list = within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', {
            name: 'Conversations',
        });
        fireEvent.click(within(list).getByRole('button', { name: title }));
        expect(document.querySelector('.area > .pane:not([hidden]) .chat-panel')).not.toBeNull();
    });
    return shownPane();
}

async function openFeed(): Promise<HTMLElement> {
    await waitFor(() => {
        const menu = within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Menu' });
        fireEvent.click(within(menu).getByRole('button', { name: /^Feed/ }));
        expect(document.querySelector('.area > .pane:not([hidden]) .chat-panel')).not.toBeNull();
    });
    return shownPane();
}

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const card = (pane: HTMLElement, name: 'AI' | 'Reply') => waitFor(() => within(pane).getByRole('group', { name }));
const buttons = (shown: HTMLElement) =>
    within(shown)
        .getAllByRole('button')
        .map((each) => words(each));
/** The desktop's members a card could act through: none is called by either card. */
const acting = (desktop: StubDesktop) =>
    desktop.calls.filter(
        (each) => /^(setup|providers|chat\.(send|mode|create))\b/.test(each.name) && each.name !== 'setup.status',
    );

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    acts.value = {};
    cardReads.value = {};
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    attention.loaded.value = false;
    attention.detail.value = null;
    forgetWaiting();
    feedId.value = null;
    feedUnseen.value = false;
});

afterEach(() => {
    vi.restoreAllMocks();
    mode.value = 'live';
});

describe('the card that opens Connect', () => {
    it('is its header, the window’s sentence and Open Connect, and says nothing of a read', async () => {
        renderConsole([setUp(turn('ai_setup'))]);
        const pane = await openChat('Setting up');
        const shown = await card(pane, 'AI');
        expect(words(shown)).toBe(`AI ${aiSentence} Open Connect`);
        expect(buttons(shown)).toEqual(['Open Connect']);
        expect(shown.querySelector('a, input, select, textarea, form')).toBeNull();
        expect(within(shown).queryByText(/^As of /)).toBeNull();
        expect(within(shown).queryByText('Reading...')).toBeNull();
        expect(within(shown).queryByText('On record now')).toBeNull();
        // The block never reaches the page; the words before it stay the AI's.
        expect(pane.textContent).not.toContain('"kind"');
        expect(within(pane).getByText('Here is where that is done.')).toBeTruthy();
        // Its header is at plain weight, as every card's is.
        expect(shown.querySelector('b, strong')).toBeNull();
    });

    it('opens Connect, where the AI hosts are, and acts on nothing', async () => {
        const { desktop } = renderConsole([setUp(turn('ai_setup'))]);
        const shown = await card(await openChat('Setting up'), 'AI');
        const before = acting(desktop);
        fireEvent.click(within(shown).getByRole('button', { name: 'Open Connect' }));
        await waitFor(() => expect(within(shownPane()).getByRole('heading', { name: 'Connect' })).toBeTruthy());
        expect(within(shownPane()).getByText('Claude Code', { selector: 'b' })).toBeTruthy();
        expect(acting(desktop)).toEqual(before);
    });
});

describe('the card that opens Accounts', () => {
    it('is its header, the window’s sentence and Open Accounts, and says nothing of a read', async () => {
        const { fake } = renderConsole([setUp(turn('reply_setup'))]);
        const pane = await openChat('Setting up');
        const shown = await card(pane, 'Reply');
        expect(words(shown)).toBe(`Reply ${replySentence} Open Accounts`);
        expect(buttons(shown)).toEqual(['Open Accounts']);
        expect(shown.querySelector('a, input, select, textarea, form')).toBeNull();
        expect(within(shown).queryByText(/^As of /)).toBeNull();
        // It reads nothing: none of the accounts card's reads is made.
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect(fake.calls.filter((each) => ['route.list', 'plugin.list'].includes(each.operation))).toEqual([]);
    });

    it('opens the Accounts page, where Reply’s card is, and acts on nothing', async () => {
        const { desktop } = renderConsole([setUp(turn('reply_setup'))]);
        const shown = await card(await openChat('Setting up'), 'Reply');
        const before = acting(desktop);
        fireEvent.click(within(shown).getByRole('button', { name: 'Open Accounts' }));
        await waitFor(() => expect(route.value).toEqual({ screen: 'accounts' }));
        await waitFor(() => expect(within(shownPane()).getByRole('heading', { name: 'Provider' })).toBeTruthy());
        expect(acting(desktop)).toEqual(before);
    });
});

describe('where the two cards stand', () => {
    it('draws both in an agent’s chat, whose agent neither names', async () => {
        renderConsole([
            {
                id: 'conv_01JDPLACES000000000000000A',
                campaignId: 'cmp_1',
                title: 'Spring',
                entries: [...turn('reply_setup'), { kind: 'user', text: 'And the AI?' }, asking('ai_setup', 'There.')],
            },
        ]);
        const pane = await openChat('Spring');
        expect(words(await card(pane, 'Reply'))).toContain(replySentence);
        expect(words(await card(pane, 'AI'))).toContain(aiSentence);
        expect(within(pane).queryByText('This chat shows only its own agent.')).toBeNull();
    });

    it('draws both in the Feed', async () => {
        // The Feed's row is in the demo's sidebar.
        mode.value = 'demo';
        renderConsole([
            {
                id: 'conv_01JDPLACES000000000000000F',
                campaignId: null,
                title: 'Feed',
                main: true,
                entries: [...turn('ai_setup'), { kind: 'user', text: 'And Reply?' }, asking('reply_setup', 'There.')],
            },
        ]);
        const feed = await openFeed();
        expect(buttons(await card(feed, 'AI'))).toEqual(['Open Connect']);
        expect(buttons(await card(feed, 'Reply'))).toEqual(['Open Accounts']);
    });

    it('leaves an earlier card of facts as the one that reads: it is read, never a stub', async () => {
        renderConsole([
            setUp([
                { kind: 'user', text: 'Which accounts can send?' },
                { kind: 'text', text: fenced({ kind: 'accounts' }) },
                { kind: 'user', text: 'And how do I connect Reply?' },
                asking('reply_setup'),
            ]),
        ]);
        const pane = await openChat('Setting up');
        await card(pane, 'Reply');
        await waitFor(() => {
            const accounts = within(pane).getByRole('group', { name: 'Accounts' });
            expect(within(accounts).getByText(/^As of /)).toBeTruthy();
            expect(within(accounts).queryByRole('button', { name: 'Show now' })).toBeNull();
        });
    });

    it('draws the line of a card not drawn for a card that holds a member, and tells the desktop its kind alone', async () => {
        const { desktop } = renderConsole([
            setUp([
                { kind: 'user', text: 'Use my key.' },
                { kind: 'text', text: fenced({ kind: 'ai_setup', key: 'sk-planted' }) },
            ]),
        ]);
        const pane = await openChat('Setting up');
        await waitFor(() =>
            expect(
                within(pane).getByText(
                    'Jason’s card did not hold what the card that opens Connect needs, so it is not shown.',
                ),
            ).toBeTruthy(),
        );
        expect(pane.textContent).not.toContain('sk-planted');
        expect(within(pane).queryByRole('group', { name: 'AI' })).toBeNull();
        await waitFor(() =>
            expect(desktop.calls.filter((each) => each.name === 'chat.cardRefused').map((each) => each.args)).toEqual([
                [setupChat, 'ai_setup', 'schema'],
            ]),
        );
    });
});

describe('the two cards in a browser tab', () => {
    it('draw as in the window, and each button opens its place there', async () => {
        const desktop = stubDesktop(setupFound);
        const stand = standInBridge(desktop);
        const tab = tabDesktop(new Bridge({ port: 43210, secret: standInSecret }, stand.fetch));
        setPlace('tab', tab);
        await loadMode(tab);
        desktop.keep({
            ...setUp([...turn('ai_setup'), { kind: 'user', text: 'And Reply?' }, asking('reply_setup', 'There.')]),
            mode: 'ask_first',
        });
        const fake = answering(new FakeApi().answer('ui.tokens', 'ui.tokens/two'));
        await session.bootstrap();
        try {
            render(<Console api={fake} desktop={tab} stream={stubStream().stream} bootstrapped />);
            const pane = await openChat('Setting up');
            const reply = await card(pane, 'Reply');
            expect(words(reply)).toBe(`Reply ${replySentence} Open Accounts`);
            expect(words(await card(pane, 'AI'))).toBe(`AI ${aiSentence} Open Connect`);
            fireEvent.click(within(reply).getByRole('button', { name: 'Open Accounts' }));
            await waitFor(() => expect(route.value).toEqual({ screen: 'accounts' }));
            // Each place says itself what only the desktop's window does.
            const offHere = "Done in Jason Desktop's own window.";
            await waitFor(() => expect(within(shownPane()).getByRole('heading', { name: 'Provider' })).toBeTruthy());
            await waitFor(() => expect(within(shownPane()).getAllByText(offHere).length).toBeGreaterThan(0));
            const again = await openChat('Setting up');
            fireEvent.click(within(await card(again, 'AI')).getByRole('button', { name: 'Open Connect' }));
            await waitFor(() => expect(within(shownPane()).getByRole('heading', { name: 'Connect' })).toBeTruthy());
            await waitFor(() => expect(within(shownPane()).getAllByText(offHere).length).toBeGreaterThan(0));
        } finally {
            cleanup();
            setPlace('window');
        }
    });
});
