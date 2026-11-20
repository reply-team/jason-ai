import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
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
import { chatDesktop, journalLine, stubDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { nothingWaitsInChats } from '../test/waiting';
import { acts } from './card-acts';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, feedId, feedUnseen } from './state';
import { forgetWaiting } from './waiting';

// The cards the chat's AI asks for, as a person meets them: a card block is never shown as JSON, a choice's words are
// the AI's and drawn as their characters, a click sends exactly the option's label and nothing else, and the choice locks
// on whatever follows it, on a reopen as on the page that drew it. In an agent's chat it also locks on a person's act on
// that agent after it was asked, read once from the journal and kept current by its stream; a chat of no agent reads
// nothing. A block that does not read is one line in the desktop's words, said once to the desktop's log by its kind
// and why.

const setupChat = 'conv_01JDCARDS0000000000000000S';
const springChat = 'conv_01JDCARDS0000000000000000A';
/** The moment the desktop kept the choice in Spring's chat: the recordings of the acts read were made with it. */
const at = '2026-10-16T09:05:00.000Z';

const fenced = (value: unknown) => `\`\`\`card\n${JSON.stringify(value)}\n\`\`\``;
const choice = {
    kind: 'choice',
    question: 'Who should the emails come from?',
    options: [{ label: 'From me', detail: 'Your own mailbox' }, { label: 'From someone else on my team' }],
};
const asking = (text = `Two ways to send it.\n\n${fenced(choice)}`): DesktopKeptEntry => ({ kind: 'text', text, at });

/** The console over a fake that answers what the console reads at its start, with the given conversations kept. */
function renderConsole(
    records: (Partial<DesktopConversationRecord> & { id: string })[],
    fake: FakeApi = new FakeApi(),
    journal: StubStream = stubStream(),
    desktop: StubDesktop = stubDesktop(),
): { fake: FakeApi; desktop: StubDesktop; journal: StubStream } {
    nothingWaitsInChats(fake)
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('system.info', 'system.info/with-ui');
    watch(agentHeads(fake));
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    for (const record of records) desktop.keep(record);
    render(<Console api={fake} desktop={desktop} stream={journal.stream} />);
    return { fake, desktop, journal };
}

/** A chat kept with one exchange before the choice is asked. */
const opened: DesktopKeptEntry[] = [
    { kind: 'user', text: 'Hi' },
    { kind: 'text', text: 'Hello.', at },
];
/** The AI's words that end in the choice, said in the chat while the page has it open, and the turn's end. */
function ask(
    desktop: StubDesktop,
    chatId: string,
    text = `Two ways to send it.\n\n${fenced(choice)}`,
    moment: string | null = at,
) {
    flush(() => {
        desktop.emit(chatId, moment === null ? { kind: 'text', text } : { kind: 'text', text, at: moment });
        desktop.emit(chatId, { kind: 'done', error: null });
    });
}
const live = (pane: HTMLElement) =>
    waitFor(() => {
        const button = option(pane, 'From me');
        expect(button.hasAttribute('disabled')).toBe(false);
        return button;
    });

const setUp = (entries: DesktopKeptEntry[]) => ({ id: setupChat, campaignId: null, title: 'Sending', entries });
const spring = (entries: DesktopKeptEntry[]) => ({ id: springChat, campaignId: 'cmp_1', title: 'Spring', entries });

/** Opens a kept chat from the sidebar, as a person does, and returns its pane. */
async function openChat(title: string): Promise<HTMLElement> {
    await waitFor(() => {
        const list = within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', {
            name: 'Conversations',
        });
        fireEvent.click(within(list).getByRole('button', { name: title }));
        expect(document.querySelector('.area > .pane:not([hidden]) .chat-panel')).not.toBeNull();
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

/** Opens the Feed from the sidebar's menu and returns its pane. */
async function openFeed(): Promise<HTMLElement> {
    await waitFor(() => {
        const menu = within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Menu' });
        fireEvent.click(within(menu).getByRole('button', { name: /^Feed/ }));
        expect(document.querySelector('.area > .pane:not([hidden]) .chat-panel')).not.toBeNull();
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

const card = (pane: HTMLElement) => within(pane).getByRole('group', { name: 'Jason asks' });
const option = (pane: HTMLElement, label: string) =>
    within(card(pane)).getByRole('button', { name: new RegExp(`^${label}`) });
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const sends = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.send');
const refusals = (desktop: StubDesktop) =>
    desktop.calls.filter((each) => each.name === 'chat.cardRefused').map((each) => each.args);
const reads = (fake: FakeApi) =>
    fake.calls.filter((each) => each.operation === 'journal.list').map((each) => each.body);
const box = () => screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
/** The open choice's field for the person's own words, or null where the card draws none. */
const own = (pane: HTMLElement) =>
    within(card(pane)).queryByRole<HTMLInputElement>('textbox', { name: 'Something else…' });

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    acts.value = {};
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    attention.loaded.value = false;
    attention.detail.value = null;
    forgetWaiting();
    feedId.value = null;
    feedUnseen.value = false;
});

describe('a choice the AI asked for', () => {
    it('is drawn under the AI’s name from the block, which never reaches the page, every word of it as its characters', async () => {
        const marked = {
            kind: 'choice',
            question: '**Who** sends it?',
            options: [{ label: '# From me', detail: '_your_ mailbox' }, { label: 'From the team' }],
        };
        renderConsole([setUp([{ kind: 'user', text: 'Set it up' }, asking(`Two ways.\n\n${fenced(marked)}`)])]);
        const pane = await openChat('Sending');
        const asked = await waitFor(() => card(pane));
        expect(words(asked).startsWith('Jason asks')).toBe(true);
        expect(within(asked).getByText('**Who** sends it?')).toBeTruthy();
        expect(within(asked).getByText('# From me')).toBeTruthy();
        expect(within(asked).getByText('_your_ mailbox')).toBeTruthy();
        expect(asked.querySelector('strong, em, h1, h2, code, a')).toBeNull();
        // The words before the block stay, with the speaker's name over them; the block's JSON is nowhere.
        expect(within(pane).getByText('Two ways.')).toBeTruthy();
        expect(pane.textContent).not.toContain('"kind"');
        expect(pane.textContent).not.toContain('```');
        expect(pane.querySelector('pre, code')).toBeNull();
    });

    it('draws no speaker’s name over a card that is a turn’s only words', async () => {
        renderConsole([setUp([{ kind: 'user', text: 'Set it up' }, asking(fenced(choice))])]);
        const pane = await openChat('Sending');
        await waitFor(() => card(pane));
        expect(pane.querySelector('.who')).toBeNull();
        expect(pane.querySelector('.m.ai')).toBeNull();
    });

    it('sends exactly the option’s label as the person’s message, once, and then shows it chosen', async () => {
        const { desktop } = renderConsole([setUp(opened)]);
        const pane = await openChat('Sending');
        ask(desktop, setupChat);
        fireEvent.click(await live(pane));
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        const [id, text, options] = sends(desktop)[0]!.args as [string, string, object];
        expect([id, text]).toEqual([setupChat, 'From me']);
        expect(Object.keys(options).sort()).toEqual(['host']);
        // The person's bubble says the same words; the card is locked, its chosen option marked.
        await waitFor(() => expect(within(pane).getAllByText('From me').length).toBeGreaterThan(1));
        const chosen = option(pane, 'From me');
        expect(chosen.hasAttribute('disabled')).toBe(true);
        expect(chosen.getAttribute('aria-pressed')).toBe('true');
        expect(words(chosen)).toContain('You chose this');
        expect(option(pane, 'From someone else').hasAttribute('disabled')).toBe(true);
        fireEvent.click(option(pane, 'From someone else'));
        expect(sends(desktop)).toHaveLength(1);
    });

    it('is locked by a message the person typed, which it says, and by a sentence of the desktop’s, with no mark', async () => {
        const { desktop } = renderConsole([setUp(opened)]);
        const pane = await openChat('Sending');
        ask(desktop, setupChat);
        await live(pane);
        flush(() => desktop.emit(setupChat, { kind: 'notice', text: 'A folder was left out of the chat.' }));
        await waitFor(() => expect(option(pane, 'From me').hasAttribute('disabled')).toBe(true));
        expect(within(card(pane)).queryByText(/You chose this|your own words/)).toBeNull();
        cleanup();
        chats.value = {};
        const again = renderConsole([setUp(opened)]);
        const typed = await openChat('Sending');
        ask(again.desktop, setupChat);
        await live(typed);
        fireEvent.change(box(), { target: { value: 'Both of us, alternately' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(sends(again.desktop)).toHaveLength(1));
        await waitFor(() => within(card(typed)).getByText('You answered in your own words.'));
        expect(option(typed, 'From me').hasAttribute('disabled')).toBe(true);
    });

    it('sends what is typed in its field as the person’s message, once, by the composer’s path, and leaves the draft', async () => {
        const { desktop } = renderConsole([setUp(opened)]);
        const pane = await openChat('Sending');
        ask(desktop, setupChat);
        await live(pane);
        fireEvent.change(box(), { target: { value: 'A draft of my own' } });
        const field = own(pane)!;
        expect(field.getAttribute('placeholder')).toBe('Something else…');
        const send = within(card(pane)).getByRole('button', { name: 'Send your own answer' });
        // An empty field sends nothing, and the second press of a double press is no second send.
        fireEvent.click(send, { detail: 1 });
        fireEvent.input(field, { target: { value: '  Both of us, alternately  ' } });
        fireEvent.click(send, { detail: 2 });
        expect(sends(desktop)).toHaveLength(0);
        fireEvent.click(send, { detail: 1 });
        fireEvent.click(send, { detail: 2 });
        fireEvent.keyDown(field, { key: 'Enter' });
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        const [id, text, options] = sends(desktop)[0]!.args as [string, string, object];
        expect([id, text]).toEqual([setupChat, 'Both of us, alternately']);
        expect(Object.keys(options).sort()).toEqual(['host']);
        // The person's own words lock the choice as a typed message does; the composer's draft is as it was.
        await waitFor(() => within(card(pane)).getByText('You answered in your own words.'));
        expect(within(pane).getByText('Both of us, alternately')).toBeTruthy();
        expect(own(pane)).toBeNull();
        expect(box().value).toBe('A draft of my own');
        cleanup();
        chats.value = {};
        const again = renderConsole([setUp(opened)]);
        const entered = await openChat('Sending');
        ask(again.desktop, setupChat);
        await live(entered);
        fireEvent.input(own(entered)!, { target: { value: 'From our sales lead' } });
        fireEvent.keyDown(own(entered)!, { key: 'Enter' });
        await waitFor(() => expect(sends(again.desktop)).toHaveLength(1));
        expect(sends(again.desktop)[0]!.args[1]).toBe('From our sales lead');
    });

    it('draws its field only while it is open: never on a locked choice, an earlier one, or one opened again', async () => {
        const { desktop } = renderConsole([setUp(opened)]);
        const pane = await openChat('Sending');
        ask(desktop, setupChat);
        await live(pane);
        expect(own(pane)).not.toBeNull();
        flush(() => desktop.emit(setupChat, { kind: 'notice', text: 'A folder was left out of the chat.' }));
        await waitFor(() => expect(option(pane, 'From me').hasAttribute('disabled')).toBe(true));
        expect(own(pane)).toBeNull();
        // The person answers below, and the AI asks again: the earlier card draws no field, the new one does.
        fireEvent.change(box(), { target: { value: 'Neither yet' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        ask(desktop, setupChat);
        await waitFor(() => expect(within(pane).getAllByRole('group', { name: 'Jason asks' })).toHaveLength(2));
        const [earlier, last] = within(pane).getAllByRole('group', { name: 'Jason asks' });
        await waitFor(() => expect(within(last!).queryByRole('textbox', { name: 'Something else…' })).not.toBeNull());
        expect(within(earlier!).queryByRole('textbox', { name: 'Something else…' })).toBeNull();
        cleanup();
        chats.value = {};
        renderConsole([setUp([{ kind: 'user', text: 'Set it up' }, asking()])]);
        const reopened = await openChat('Sending');
        await waitFor(() => within(card(reopened)).getByText(/asked before the chat was opened again/));
        expect(own(reopened)).toBeNull();
    });

    it('keeps the words typed in its field, and the field’s focus, when the chat is painted again', async () => {
        const { desktop } = renderConsole([setUp(opened)]);
        const pane = await openChat('Sending');
        ask(desktop, setupChat);
        await live(pane);
        fireEvent.change(box(), { target: { value: 'A draft of my own' } });
        const field = own(pane)!;
        field.focus();
        fireEvent.input(field, { target: { value: 'Both of us' } });
        expect(document.activeElement).toBe(field);
        // An event for the conversation that adds nothing to its thread: the thread is painted again, its field anew.
        flush(() => desktop.emit(setupChat, { kind: 'renamed', title: 'Sending, renamed' }));
        await waitFor(() => expect(field.isConnected).toBe(false));
        const again = own(pane)!;
        expect(again.value).toBe('Both of us');
        expect(document.activeElement).toBe(again);
        expect(box().value).toBe('A draft of my own');
        expect(sends(desktop)).toHaveLength(0);
    });

    it('is drawn answered as it was when the conversation is opened again, and locked, saying why, when it was not', async () => {
        const fake = new FakeApi();
        renderConsole(
            [
                setUp([
                    { kind: 'user', text: 'Set it up' },
                    asking(),
                    { kind: 'user', text: ' From someone else on my team ' },
                    { kind: 'text', text: 'Noted.', at },
                ]),
                spring([{ kind: 'user', text: 'Who sends?' }, asking()]),
            ],
            fake,
        );
        const pane = await openChat('Sending');
        const chosen = await waitFor(() => option(pane, 'From someone else'));
        expect(chosen.getAttribute('aria-pressed')).toBe('true');
        expect(option(pane, 'From me').hasAttribute('disabled')).toBe(true);
        // What locked it may be a sentence of the desktop's, which its record does not keep: it is drawn locked.
        const agent = await openChat('Spring');
        await waitFor(() =>
            within(card(agent)).getByText('This was asked before the chat was opened again; write your answer below.'),
        );
        expect(option(agent, 'From me').hasAttribute('disabled')).toBe(true);
        expect(reads(fake)).toEqual([]);
    });

    it('in a tab that may not send is only shown, as the plan card is; in one that may, it sends', async () => {
        const desktop = chatDesktop();
        const stand = standInBridge(desktop);
        const tab = tabDesktop(new Bridge({ port: 43210, secret: standInSecret }, stand.fetch));
        setPlace('tab', tab);
        await loadMode(tab);
        desktop.keep({ ...setUp(opened), mode: 'auto_edit' });
        const fake = new FakeApi()
            .answer('plugin.list', 'plugin.list/none')
            .answer('route.list', 'route.list/empty')
            .answer('ui.tokens', 'ui.tokens/two');
        nothingWaitsInChats(fake)
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('preference.list', 'preference.list/set')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('system.info', 'system.info/with-ui');
        watch(agentHeads(fake));
        activatePlugins(browserPlugins);
        localStorage.clear();
        location.hash = '';
        route.value = { screen: 'attention' };
        session.clear();
        localStorage.setItem(
            'jason.ui.session',
            JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
        );
        await session.bootstrap();
        try {
            render(<Console api={fake} desktop={tab} stream={stubStream().stream} bootstrapped />);
            const pane = await openChat('Sending');
            await waitFor(() => expect(stand.asked.some((each) => each.path === '/events')).toBe(true));
            ask(desktop, setupChat);
            const asked = await waitFor(() => card(pane));
            await waitFor(() => within(asked).getByText('This is answered in Jason Desktop.'));
            expect(option(pane, 'From me').hasAttribute('disabled')).toBe(true);
            fireEvent.click(option(pane, 'From me'));
            expect(stand.asked.filter((each) => each.member === 'chat.send')).toHaveLength(0);
            // The desktop's log is not the tab's to write to: no refusal is asked of the bridge.
            expect(stand.asked.some((each) => each.member === 'chat.cardRefused')).toBe(false);
        } finally {
            cleanup();
            setPlace('window');
        }
    });
});

describe('a card that does not read', () => {
    it('draws one line in the desktop’s words, tells the desktop’s log once by its kind and why, and never what it held', async () => {
        const turns: DesktopKeptEntry[] = [
            { kind: 'user', text: 'One' },
            { kind: 'text', text: fenced({ kind: 'chart', planted: 'planted words' }), at },
            { kind: 'user', text: 'Two' },
            { kind: 'text', text: '```card\n{"kind":"choice","question":"planted words",\n```', at },
            { kind: 'user', text: 'Three' },
            { kind: 'text', text: `\`\`\`card\n{"kind":"choice","question":"${'planted '.repeat(2001)}"}\n\`\`\``, at },
            { kind: 'user', text: 'Four' },
            {
                kind: 'text',
                text: fenced({
                    kind: 'choice',
                    question: 'planted words',
                    options: [{ label: 'Approve' }, { label: 'No' }],
                }),
                at,
            },
        ];
        const { desktop } = renderConsole([setUp(turns)]);
        const pane = await openChat('Sending');
        await waitFor(() => expect(refusals(desktop)).toHaveLength(4));
        expect(within(pane).getByText('Jason asked for a card this app does not draw.')).toBeTruthy();
        expect(within(pane).getByText('Jason’s card could not be read, so it is not shown.')).toBeTruthy();
        expect(within(pane).getByText('Jason’s card was too long to read, so it is not shown.')).toBeTruthy();
        expect(
            within(pane).getByText('Jason’s card did not hold what a choice card needs, so it is not shown.'),
        ).toBeTruthy();
        expect(refusals(desktop)).toEqual([
            [setupChat, 'unknown', 'kind_unknown'],
            [setupChat, 'unknown', 'malformed'],
            [setupChat, 'unknown', 'too_long'],
            [setupChat, 'choice', 'schema'],
        ]);
        expect(pane.textContent).not.toContain('planted');
        expect(within(pane).queryByRole('group', { name: 'Jason asks' })).toBeNull();
        // A redraw of the thread, for a word typed in the box, tells the log nothing more.
        fireEvent.change(box(), { target: { value: 'And?' } });
        flush(() => desktop.emit(setupChat, { kind: 'notice', text: 'A folder was left out of the chat.' }));
        await waitFor(() => within(pane).getByText('A folder was left out of the chat.'));
        expect(refusals(desktop)).toHaveLength(4);
    });

    it('draws nothing for a block still being written while its turn runs, and says it could not be read once the turn ends', async () => {
        const { desktop } = renderConsole([
            setUp([
                { kind: 'user', text: 'Hi' },
                { kind: 'text', text: 'Hello.', at },
            ]),
        ]);
        const pane = await openChat('Sending');
        fireEvent.change(box(), { target: { value: 'Who sends?' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        flush(() => desktop.emit(setupChat, { kind: 'text', text: 'Let me ask.\n```card\n{"kind":"choice",', at }));
        await waitFor(() => within(pane).getByText('Let me ask.'));
        expect(within(pane).queryByText(/Jason’s card/)).toBeNull();
        expect(pane.textContent).not.toContain('"kind"');
        flush(() => desktop.emit(setupChat, { kind: 'done', error: null }));
        await waitFor(() => within(pane).getByText('Jason’s card could not be read, so it is not shown.'));
    });
});

describe('a choice in an agent’s chat', () => {
    /** Opens Spring's chat and has the AI ask the choice in it. */
    async function asked(fake: FakeApi, journal: StubStream = stubStream()) {
        const { desktop } = renderConsole([spring(opened)], fake, journal);
        const pane = await openChat('Spring');
        ask(desktop, springChat);
        await waitFor(() => card(pane));
        return { pane, desktop };
    }

    it('reads once whether the person acted on the agent since it was asked, and is live when they did not', async () => {
        const fake = new FakeApi().answer('journal.list', 'journal.list/card-choice-acts-none');
        const { pane } = await asked(fake);
        await live(pane);
        expect(reads(fake)).toEqual([
            {
                campaign_id: 'cmp_1',
                kinds: [
                    'campaign_started',
                    'campaign_paused',
                    'approval_approved',
                    'approval_rejected',
                    'decision_answered',
                ],
                since: at,
                limit: 50,
            },
        ]);
    });

    it('is locked, saying so, by a person’s act on the agent after it was asked', async () => {
        const fake = new FakeApi().answer('journal.list', 'journal.list/card-choice-acts');
        const { pane } = await asked(fake);
        await waitFor(() =>
            within(card(pane)).getByText(
                'You acted on this agent after this was asked; write your answer below if it still stands.',
            ),
        );
        expect(option(pane, 'From me').hasAttribute('disabled')).toBe(true);
    });

    it('is locked by a person’s act the journal’s stream brings while it is live, and not by a role’s', async () => {
        const fake = new FakeApi().answer('journal.list', 'journal.list/card-choice-acts-none');
        const journal = stubStream();
        const { pane } = await asked(fake, journal);
        await live(pane);
        flush(() => {
            journal.open();
            journal.send(
                journalLine({
                    id: 'jrn_21',
                    kind: 'approval_approved',
                    campaign_id: 'cmp_1',
                    ts: '2026-10-16T09:06:00.000Z',
                    actor: { type: 'role', id: 'critic' },
                }),
            );
        });
        expect(option(pane, 'From me').hasAttribute('disabled')).toBe(false);
        flush(() =>
            journal.send(
                journalLine({
                    id: 'jrn_22',
                    kind: 'campaign_paused',
                    campaign_id: 'cmp_1',
                    ts: '2026-10-16T09:07:00.000Z',
                    actor: { type: 'human', id: 'ada' },
                }),
            ),
        );
        await waitFor(() => within(card(pane)).getByText(/^You acted on this agent after this was asked/));
        expect(option(pane, 'From me').hasAttribute('disabled')).toBe(true);
        expect(reads(fake)).toHaveLength(1);
    });

    it('is drawn locked, with the quiet words, when the read fails', async () => {
        const fake = new FakeApi().answer('journal.list', 'journal.list/card-choice-acts-none');
        fake.unreachable('journal.list');
        const { pane } = await asked(fake);
        await waitFor(() =>
            within(card(pane)).getByText('Whether this still stands could not be read; write your answer below.'),
        );
        expect(option(pane, 'From me').hasAttribute('disabled')).toBe(true);
    });

    it('is drawn locked, with the quiet words and no read, when an earlier desktop kept its words with no moment', async () => {
        const fake = new FakeApi();
        const { desktop } = renderConsole([spring(opened)], fake);
        const pane = await openChat('Spring');
        ask(desktop, springChat, fenced(choice), null);
        await waitFor(() =>
            within(card(pane)).getByText('Whether this still stands could not be read; write your answer below.'),
        );
        expect(reads(fake)).toEqual([]);
    });

    it('reads no act in a set-up chat or in the Feed, where only what follows it locks it', async () => {
        const fake = new FakeApi();
        // The Feed's row is in the demo's sidebar.
        mode.value = 'demo';
        const feedChat = 'conv_01JDCARDS0000000000000000F';
        const { desktop } = renderConsole(
            [setUp(opened), { id: feedChat, campaignId: null, title: 'Feed', main: true, entries: opened }],
            fake,
        );
        const pane = await openChat('Sending');
        ask(desktop, setupChat);
        await live(pane);
        const feed = await openFeed();
        ask(desktop, feedChat);
        await live(feed);
        expect(reads(fake)).toEqual([]);
    });

    it('reads the acts again when the stream starts afresh after a resume the runtime refused, as lines were missed', async () => {
        const fake = new FakeApi().answer('journal.list', 'journal.list/card-choice-acts-none');
        const journal = stubStream();
        const { pane } = await asked(fake, journal);
        await live(pane);
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_31', kind: 'contacts_added', campaign_id: 'cmp_1' }));
        });
        // The runtime goes away; the stream resumes after its last line, and the runtime refuses that resume.
        flush(() => journal.close());
        await waitFor(() => expect(journal.opened).toHaveLength(2), { timeout: 3000 });
        fake.answer('journal.list', 'journal.list/card-choice-acts');
        flush(() => journal.close());
        await waitFor(() => expect(reads(fake)).toHaveLength(2), { timeout: 3000 });
        await waitFor(() => within(card(pane)).getByText(/^You acted on this agent after this was asked/));
        expect(option(pane, 'From me').hasAttribute('disabled')).toBe(true);
    });

    it('ignores the answer of a read made before the stream started afresh, which arrives after the read made again', async () => {
        // The first read finds an act and the second none; both are held, and the first answers first.
        let answered = 0;
        const fake = new FakeApi().answerAnyRequest('journal.list', () =>
            answered++ === 0 ? 'journal.list/card-choice-acts' : 'journal.list/card-choice-acts-none',
        );
        const release = fake.hold('journal.list');
        const journal = stubStream();
        const { pane } = await asked(fake, journal);
        await waitFor(() => expect(reads(fake)).toHaveLength(1));
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_41', kind: 'contacts_added', campaign_id: 'cmp_1' }));
        });
        flush(() => journal.close());
        await waitFor(() => expect(journal.opened).toHaveLength(2), { timeout: 3000 });
        flush(() => journal.close());
        await waitFor(() => expect(reads(fake)).toHaveLength(2), { timeout: 3000 });
        flush(() => release());
        await live(pane);
        expect(answered).toBe(2);
        expect(within(card(pane)).queryByText(/^You acted on this agent/)).toBeNull();
    });
});

afterEach(() => {
    acts.value = {};
    mode.value = 'live';
});
