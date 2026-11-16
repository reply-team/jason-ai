import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import type { DesktopConversationRecord, DesktopKeptEntry } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { nothingWaitsInChats } from '../test/waiting';
import { acts } from './card-acts';
import { proposals } from './card-plan';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, feedId, feedUnseen } from './state';
import { forgetWaiting } from './waiting';

// A plan the chat's AI proposes, as a person meets it: the AI's part under its name, saying it creates and sends
// nothing, every word of it drawn as its characters and each draft one click away; under it what is on record now, read
// from the runtime when the card is drawn and on its Refresh; after a reopen only the conversation's last card that
// reads reads, an earlier one waiting on "Show now". Its question's options send their words as a choice's do and lock
// as a choice does. Nothing on it approves, creates or sends.

const winterChat = 'conv_01JDPLAN00000000000000000W';
const springChat = 'conv_01JDPLAN00000000000000000A';
const setupChat = 'conv_01JDPLAN00000000000000000S';
const at = '2026-10-16T09:05:00.000Z';

const fenced = (value: unknown) => `\`\`\`card\n${JSON.stringify(value)}\n\`\`\``;
const draft = 'Hi Ada,\n\n**Bold** words stay as typed.\n- not a list\n# not a heading';
const plan = {
    kind: 'plan',
    title: '**Winter** outreach',
    who: 'Heads of sales at 40 logistics firms',
    offer: 'A route audit',
    sender: 'From your own mailbox',
    goal: 'Book 3 calls',
    channels: ['email', 'linkedin'],
    steps: [
        { when: 'First', channel: 'email', subject: 'A # question', body: draft },
        { when: 'Three days after', channel: 'linkedin', body: 'A short note.' },
    ],
    rules: ['If they reply, stop the steps'],
    manual: ['Call the 7 who open twice'],
};
const asking = { ...plan, question: 'Does this look right?', options: [{ label: 'Yes, go on' }, { label: 'Shorter' }] };
const proposing = (value: unknown = plan, words = 'Here is my proposal.'): DesktopKeptEntry => ({
    kind: 'text',
    text: `${words}\n\n${fenced(value)}`,
    at,
});
const turn = (value: unknown = plan): DesktopKeptEntry[] => [{ kind: 'user', text: 'Plan it' }, proposing(value)];

const winter = (entries: DesktopKeptEntry[]) => ({
    id: winterChat,
    campaignId: 'cmp_5',
    title: 'Winter plan',
    entries,
});
const spring = (entries: DesktopKeptEntry[]) => ({ id: springChat, campaignId: 'cmp_1', title: 'Spring', entries });
/** A plan that sends on one channel alone. */
const only = (channel: 'email' | 'linkedin') => ({
    ...plan,
    channels: [channel],
    steps: [{ when: 'First', channel, body: 'A short note.' }],
});
const setUp = (entries: DesktopKeptEntry[]) => ({ id: setupChat, campaignId: null, title: 'Sending', entries });

/** The console over a fake that answers what the console reads at its start, with the given conversations kept. */
function renderConsole(
    records: (Partial<DesktopConversationRecord> & { id: string })[],
    fake: FakeApi = new FakeApi(),
    desktop: StubDesktop = stubDesktop(),
): { fake: FakeApi; desktop: StubDesktop } {
    nothingWaitsInChats(fake)
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen');
    if (!fake.offers('campaign.summary')) fake.answer('campaign.summary', 'campaign.summary/all');
    if (!fake.offers('system.info')) fake.answer('system.info', 'system.info/with-ui');
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
    for (const record of records) desktop.keep(record);
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return { fake, desktop };
}

/**
 * What the draft agent's plan card reads: the agent, its people now, and the plugin a send takes on each channel the
 * plan names.
 */
const winterReads = () =>
    new FakeApi()
        .answer('campaign.get', 'campaign.get/card-plan-draft')
        .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-plan-draft')
        .answer('route.resolve', 'route.resolve/card-plan-email', 'route.resolve/card-plan-linkedin');
/** What a chat about no agent yet reads: the installation's send modes and its routes. */
const setupReads = () =>
    new FakeApi()
        .answer('system.info', 'system.info/with-ui', 'system.info/send-modes-set')
        .answer('route.list', 'route.list/card-accounts');

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

const proposal = (pane: HTMLElement) => within(pane).getByRole('group', { name: 'Jason’s proposal' });
const proposalsIn = (pane: HTMLElement) => within(pane).queryAllByRole('group', { name: 'Jason’s proposal' });
/** The AI's part of a plan card, and the runtime's. */
const ai = (card: HTMLElement) => card.querySelector<HTMLElement>('.card-ai')!;
const facts = (card: HTMLElement) => card.querySelector<HTMLElement>('.card-facts')!;
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const calls = (fake: FakeApi, operation: string) => fake.calls.filter((each) => each.operation === operation);
const writes = (fake: FakeApi) => fake.calls.filter((each) => each.kind === 'write');
const sends = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.send');
/**
 * The pane's plan card at `at` once its facts are read, "On record now" over them and the minute they were read: found
 * afresh, as every read that lands paints the thread again.
 */
const read = (pane: HTMLElement, at = 0) =>
    waitFor(() => {
        const card = proposalsIn(pane)[at]!;
        expect(within(facts(card)).queryByText('Reading...')).toBeNull();
        expect(facts(card).querySelector('.card-asof')).not.toBeNull();
        return card;
    });

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    acts.value = {};
    proposals.value = {};
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
    acts.value = {};
    proposals.value = {};
});

describe('a plan the AI proposes', () => {
    it('is drawn under the AI’s name, saying it creates and sends nothing, every word of it as its characters', async () => {
        renderConsole([winter(turn())], winterReads());
        const pane = await openChat('Winter plan');
        const card = await waitFor(() => proposal(pane));
        const rows = Array.from(ai(card).children).map(words);
        expect(rows.slice(0, 2)).toEqual(['Jason’s proposal', 'This card creates and sends nothing.']);
        const part = ai(card);
        for (const shown of [
            '**Winter** outreach',
            'Heads of sales at 40 logistics firms',
            'A route audit',
            'From your own mailbox',
            'Book 3 calls',
            'A # question',
            'If they reply, stop the steps',
            'Call the 7 who open twice',
        ])
            expect(within(part).getByText(shown)).toBeTruthy();
        for (const label of [
            'Who',
            'What we offer',
            'Written as',
            'Goal',
            'Channels',
            'Proposed steps',
            'How it adapts',
            'You do by hand',
        ])
            expect(within(part).getByText(label)).toBeTruthy();
        expect(within(part).getByText('Email, LinkedIn')).toBeTruthy();
        expect(words(part)).toContain('Three days after · LinkedIn');
        expect(part.querySelector('strong, em, h1, h2, h3, code, a, pre, li > ul')).toBeNull();
        // The words before the block stay, with the speaker's name over them; the block's JSON is nowhere.
        expect(within(pane).getByText('Here is my proposal.')).toBeTruthy();
        expect(pane.textContent).not.toContain('"kind"');
        expect(pane.textContent).not.toContain('```');
        // A draft is the AI's words, never an approval's message: nothing of it shows until it is opened.
        expect(pane.textContent).not.toContain('stay as typed');
        expect(card.querySelector('.ask-b')).toBeNull();
        expect(within(card).queryByRole('button', { name: /Approve|Send|Start/ })).toBeNull();
    });

    it('opens a draft in place under “Jason’s draft”, its line breaks kept and its Markdown as characters, and never moves the proposal’s label', async () => {
        const { fake, desktop } = renderConsole([winter(turn())], winterReads());
        const pane = await openChat('Winter plan');
        const card = await read(pane);
        const before = Array.from(ai(card).children).slice(0, 2).map(words);
        const open = within(card).getAllByRole('button', { name: 'Show the draft' })[0]!;
        expect(open.getAttribute('aria-expanded')).toBe('false');
        open.focus();
        fireEvent.click(open);
        const opened = await waitFor(() => within(proposal(pane)).getByText('Jason’s draft'));
        const again = proposal(pane);
        expect(Array.from(ai(again).children).slice(0, 2).map(words)).toEqual(before);
        const body = again.querySelector<HTMLElement>('.proposal-body')!;
        expect(body.textContent).toBe(draft);
        expect(opened.compareDocumentPosition(body) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(body.querySelector('strong, h1, ul, li')).toBeNull();
        // The press repainted the card and kept the person's place on the same control, which now hides the draft.
        const hide = within(again).getByRole('button', { name: 'Hide the draft' });
        expect(hide.getAttribute('aria-expanded')).toBe('true');
        expect(document.activeElement).toBe(hide);
        // The second step's draft is still closed: one press opens one draft.
        expect(within(again).getAllByRole('button', { name: 'Show the draft' })).toHaveLength(1);
        fireEvent.click(hide);
        await waitFor(() => expect(within(proposal(pane)).queryByText('Jason’s draft')).toBeNull());
        // Opening a draft asked nothing of the runtime and sent nothing.
        expect(writes(fake)).toEqual([]);
        expect(sends(desktop)).toEqual([]);
    });

    it('says what is on record now for the agent: its status, how its messages go, its people and the plugin that sends them', async () => {
        const { fake } = renderConsole([winter(turn())], winterReads());
        const pane = await openChat('Winter plan');
        const card = await read(pane);
        const part = facts(card);
        expect(words(part)).toMatch(/^On record now As of \d\d:\d\d Refresh/);
        expect(within(part).getByText('It is a draft: none of its work starts until it is started.')).toBeTruthy();
        expect(within(part).getAllByText('Every message waits for you')).toHaveLength(2);
        expect(within(part).getByText('2 people in this agent')).toBeTruthy();
        expect(words(part)).toContain('Plugin that sends email: reply It can send this agent’s email now.');
        expect(words(part)).toContain(
            'Plugin that sends LinkedIn messages: reply It can send this agent’s LinkedIn messages now.',
        );
        // No number of the AI's stands under "On record now": the only one there is the runtime's count of people.
        const counted = part.cloneNode(true) as HTMLElement;
        counted.querySelector('.card-asof')!.remove();
        expect(counted.textContent!.match(/\d+/g)).toEqual(['2']);
        expect(calls(fake, 'campaign.get').map((each) => each.body)).toEqual([{ campaign_id: 'cmp_5' }]);
        // A send names its channel, so where each channel's sends go is read by naming it, as the claim does.
        expect(calls(fake, 'route.resolve').map((each) => each.body)).toEqual([
            { campaign_id: 'cmp_5', operation: 'message.send', channel: 'email' },
            { campaign_id: 'cmp_5', operation: 'message.send', channel: 'linkedin' },
        ]);
        expect(calls(fake, 'campaign.summary').at(-1)!.body).toEqual({
            campaign_ids: ['cmp_5'],
            exclude_archived_contacts: true,
        });
    });

    it('reads again on Refresh, the same reads and nothing else, and decides nothing', async () => {
        const { fake, desktop } = renderConsole([winter(turn())], winterReads());
        const pane = await openChat('Winter plan');
        const card = await read(pane);
        const made = fake.calls.length;
        fireEvent.click(within(facts(card)).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => expect(fake.calls.length).toBe(made + 4));
        await read(pane);
        expect(
            fake.calls
                .slice(made)
                .map((each) => each.operation)
                .sort(),
        ).toEqual(['campaign.get', 'campaign.summary', 'route.resolve', 'route.resolve']);
        expect(writes(fake)).toEqual([]);
        expect(sends(desktop)).toEqual([]);
    });

    it('says no plugin is routed to send email for an agent the routes send nowhere', async () => {
        const fake = new FakeApi()
            .answer('campaign.get', 'campaign.get/card-plan-unrouted')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-plan-unrouted')
            .answer('route.resolve', 'route.resolve/card-plan-no-route-email');
        renderConsole([spring(turn(only('email')))], fake);
        const pane = await openChat('Spring');
        const card = await read(pane);
        expect(within(facts(card)).getByText('No plugin is routed to send email for this agent.')).toBeTruthy();
        expect(within(facts(card)).getByText('0 people in this agent')).toBeTruthy();
        expect(words(facts(card))).not.toContain('Plugin that sends');
    });

    it('names the plugin a channel route of the agent’s own sends its LinkedIn messages by', async () => {
        const fake = new FakeApi()
            .answer('campaign.get', 'campaign.get/card-plan-channels')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-plan-channels')
            .answer('route.resolve', 'route.resolve/on-a-channel');
        renderConsole([spring(turn(only('linkedin')))], fake);
        const pane = await openChat('Spring');
        const card = await read(pane);
        expect(words(facts(card))).toContain(
            'Plugin that sends LinkedIn messages: other-tool It can send this agent’s LinkedIn messages now.',
        );
        expect(words(facts(card))).not.toContain('email');
        expect(calls(fake, 'route.resolve').map((each) => each.body)).toEqual([
            { campaign_id: 'cmp_1', operation: 'message.send', channel: 'linkedin' },
        ]);
    });

    it('in a chat about no agent yet says so, with how messages go for every agent and the plugin the routes send them by', async () => {
        const { fake } = renderConsole([setUp(turn())], setupReads());
        const pane = await openChat('Sending');
        const part = facts(await read(pane));
        expect(within(part).getByText('No agent is created yet.')).toBeTruthy();
        expect(words(part)).toContain('People who have not replied yet: Goes automatically');
        expect(words(part)).toContain('People who have replied: Every message waits for you');
        expect(words(part)).toContain('Plugin that sends email: reply');
        expect(words(part)).toContain('Plugin that sends LinkedIn messages: reply');
        expect(calls(fake, 'route.list').map((each) => each.body)).toEqual([{}]);
        expect(calls(fake, 'campaign.get')).toEqual([]);
        expect(calls(fake, 'route.resolve')).toEqual([]);
    });

    it('in a chat about no agent yet names the plugin a global channel route sends that channel’s messages by', async () => {
        const fake = new FakeApi()
            .answer('system.info', 'system.info/with-ui', 'system.info/send-modes-set')
            .answer('route.list', 'route.list/card-plan-global-channels');
        renderConsole([setUp(turn())], fake);
        const pane = await openChat('Sending');
        const part = facts(await read(pane));
        expect(Array.from(part.querySelectorAll('.card-line')).map(words).slice(-2)).toEqual([
            'Plugin that sends email: reply',
            'Plugin that sends LinkedIn messages: other-tool',
        ]);
    });

    it('after a reopen is the one card that reads when it follows a data card, which waits on Show now', async () => {
        const { fake } = renderConsole(
            [winter([...turn({ kind: 'agent_status' }), ...turn()])],
            // The plan's reads alone: a read of the status card would find no recording and fail.
            winterReads(),
        );
        const pane = await openChat('Winter plan');
        await read(pane);
        const status = within(pane).getByRole('group', { name: 'Agent' });
        expect(words(status)).toBe('Agent Show now');
        expect(calls(fake, 'campaign.activity')).toEqual([]);
    });

    it('after a reopen reads only for the conversation’s last plan, an earlier one waiting on Show now', async () => {
        const { fake } = renderConsole(
            [setUp([...turn(), ...turn({ ...plan, title: 'Spring outreach' })])],
            setupReads(),
        );
        const pane = await openChat('Sending');
        await waitFor(() => expect(proposalsIn(pane)).toHaveLength(2));
        await read(pane, 1);
        const earlier = proposalsIn(pane)[0]!;
        // The earlier card's own words are all there; only its reading waits.
        expect(within(ai(earlier)).getByText('**Winter** outreach')).toBeTruthy();
        expect(words(facts(earlier))).toBe('On record now Show now');
        expect(calls(fake, 'route.list')).toHaveLength(1);
        fireEvent.click(within(facts(earlier)).getByRole('button', { name: 'Show now' }));
        const shown = await read(pane, 0);
        expect(calls(fake, 'route.list')).toHaveLength(2);
        expect(within(facts(shown)).getByText('No agent is created yet.')).toBeTruthy();
        // The later card's read stands as it was: Show now read the earlier one alone.
        expect(within(facts(proposalsIn(pane)[1]!)).getByText('No agent is created yet.')).toBeTruthy();
    });
});

describe('a plan’s question', () => {
    /** The AI's plan with its question, said in the chat while the page has it open, and the turn's end. */
    function ask(desktop: StubDesktop) {
        flush(() => {
            desktop.emit(setupChat, { kind: 'text', text: `Here it is.\n\n${fenced(asking)}`, at });
            desktop.emit(setupChat, { kind: 'done', error: null });
        });
    }
    const asked = (pane: HTMLElement) => within(proposal(pane)).getByRole('group', { name: 'Jason asks' });
    const option = (pane: HTMLElement, label: string) =>
        within(asked(pane)).getByRole('button', { name: new RegExp(`^${label}`) });

    it('stands under the AI’s name after what is on record, and sends exactly the option’s label, once, as a choice does', async () => {
        const { desktop } = renderConsole(
            [
                setUp([
                    { kind: 'user', text: 'Hi' },
                    { kind: 'text', text: 'Hello.', at },
                ]),
            ],
            setupReads(),
        );
        const pane = await openChat('Sending');
        ask(desktop);
        const question = await waitFor(() => asked(pane));
        expect(words(question)).toMatch(/^Jason asks Does this look right\?/);
        expect(facts(proposal(pane)).compareDocumentPosition(question) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        await waitFor(() => expect(option(pane, 'Yes, go on').hasAttribute('disabled')).toBe(false));
        fireEvent.click(option(pane, 'Yes, go on'));
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        expect(sends(desktop)[0]!.args.slice(0, 2)).toEqual([setupChat, 'Yes, go on']);
        await waitFor(() => expect(option(pane, 'Yes, go on').getAttribute('aria-pressed')).toBe('true'));
        expect(option(pane, 'Shorter').hasAttribute('disabled')).toBe(true);
        fireEvent.click(option(pane, 'Shorter'));
        expect(sends(desktop)).toHaveLength(1);
    });

    it('is drawn locked, saying why, when the conversation is opened again', async () => {
        renderConsole([setUp(turn(asking))], setupReads());
        const pane = await openChat('Sending');
        await waitFor(() =>
            within(asked(pane)).getByText('This was asked before the chat was opened again; write your answer below.'),
        );
        expect(option(pane, 'Yes, go on').hasAttribute('disabled')).toBe(true);
        expect(option(pane, 'Shorter').hasAttribute('disabled')).toBe(true);
    });

    it('is locked by what the person typed instead', async () => {
        const { desktop } = renderConsole(
            [
                setUp([
                    { kind: 'user', text: 'Hi' },
                    { kind: 'text', text: 'Hello.', at },
                ]),
            ],
            setupReads(),
        );
        const pane = await openChat('Sending');
        ask(desktop);
        await waitFor(() => expect(option(pane, 'Yes, go on').hasAttribute('disabled')).toBe(false));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'Make it warmer' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => within(asked(pane)).getByText('You answered in your own words.'));
        expect(option(pane, 'Yes, go on').hasAttribute('disabled')).toBe(true);
    });
});
