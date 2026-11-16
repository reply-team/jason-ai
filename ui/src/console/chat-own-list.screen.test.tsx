import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import type { DesktopConversationRecord, DesktopKeptEntry } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { fixtures, type FixtureName } from '../test/fixtures';
import { nothingWaitsInChats } from '../test/waiting';
import { acts } from './card-acts';
import { cardReads } from './card-frame';
import { lists } from './card-own-list';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, feedId, feedUnseen } from './state';
import { forgetWaiting } from './waiting';

// A list the person gave in an agent's chat, as they meet it on the card the AI wrote: every row it will send, how each
// column was read, which addresses stand in what the person wrote, and the agent as the runtime holds it, with one
// button of the person's own. A press sends one recorded request and draws the runtime's answer row by row; a second
// press adds nobody twice. The agent's story: read as a draft, the list added and added again, the agent started, its
// send mode for people who have not replied set to automatic by a person, then archived.

const springChat = 'conv_01JDLIST00000000000000000A';
const setupChat = 'conv_01JDLIST00000000000000000S';
const at = '2026-10-16T09:05:00.000Z';

/** The recorded list, row by row, as the card holds it: the person's columns in the order they gave them. */
const recorded = [
    ['Alma', 'Novak', 'alma@example.test'],
    ['Ines', 'Duarte-Lopes', 'ines@example.test'],
    ['Alma', 'Novak', 'alma@example.test'],
    ['Omar', 'Reyes', 'omar@example.test'],
    ['Bruno', 'Silva', 'bruno-at-example'],
    ['Chen', 'Wu', ''],
    ['Rafa', 'Costa', 'rafa@example.test'],
];
const list = (rows: string[][] = recorded, fields: object = {}) => ({
    kind: 'own_list',
    match_by: 'email',
    columns: [
        { from: 'Name', field: 'first_name' },
        { from: 'Surname', field: 'last_name' },
        { from: 'Mail', field: 'email' },
    ],
    left_out: ['Notes'],
    rows,
    ...fields,
});
/** What the person pasted: every row but the last, whose address they never wrote here. */
const pasted = recorded
    .slice(0, -1)
    .map((row) => row.join(' '))
    .join('\n');

const fenced = (value: unknown) => `\`\`\`card\n${JSON.stringify(value)}\n\`\`\``;
const turn = (value: unknown, said = pasted): DesktopKeptEntry[] => [
    { kind: 'user', text: `Add these people:\n${said}` },
    { kind: 'text', text: `You gave me 7 rows; all 7 are on this card.\n\n${fenced(value)}`, at },
];
const spring = (entries: DesktopKeptEntry[], fields: Partial<DesktopConversationRecord> = {}) => ({
    id: springChat,
    campaignId: 'cmp_1',
    title: 'Spring',
    entries,
    ...fields,
});

/** The agent as each read finds it now: the test moves it along the story. */
let agent: FixtureName = 'campaign.get/own-list';

function renderConsole(
    records: (Partial<DesktopConversationRecord> & { id: string })[],
    fake: FakeApi = new FakeApi(),
): FakeApi {
    nothingWaitsInChats(fake);
    if (!fake.offers('approval.list')) fake.answer('approval.list', 'approval.list/pending');
    if (!fake.offers('decision.list')) fake.answer('decision.list', 'decision.list/pending');
    fake.answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('system.info', 'system.info/with-ui')
        .answer('campaign.summary', 'campaign.summary/all')
        .answerAnyRequest('campaign.get', () => agent)
        .answerAnyRequest('sender.list', 'sender.list/card-message-sender');
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
    const desktop = stubDesktop();
    for (const record of records) desktop.keep(record);
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return fake;
}

async function openChat(title: string): Promise<HTMLElement> {
    await waitFor(() => {
        const side = within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', {
            name: 'Conversations',
        });
        fireEvent.click(within(side).getByRole('button', { name: title }));
        expect(document.querySelector('.area > .pane:not([hidden]) .chat-panel')).not.toBeNull();
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
/**
 * The list card, once its agent has been read: a function that finds it again each time, since the thread is painted
 * afresh on every change and an element found before may no longer be the one drawn.
 */
async function readCard(pane: HTMLElement): Promise<() => HTMLElement> {
    const find = () => within(pane).getByRole('group', { name: 'Your list' });
    await waitFor(() => expect(within(find()).queryByText('Reading...')).toBeNull());
    return find;
}
const addButton = (card: HTMLElement) => within(card).queryByRole('button', { name: 'Add to this agent' });
const adds = (fake: FakeApi) => fake.calls.filter((call) => call.operation === 'campaign.add_contacts');
/** The rows of a table, each its cells' words. */
const rowsOf = (table: Element) =>
    [...table.querySelectorAll('tbody tr')].map((row) => [...row.querySelectorAll('td')].map(words));
/** The request the recording was made with, without the actor the client adds to every write. */
const recordedRequest = (name: 'campaign.add_contacts/own-list') => {
    const { actor, ...rest } = fixtures[name].request as { actor: unknown };
    return { actor, rest };
};

/** A page opened afresh: nothing the page held is left, as after a reload. */
function freshPage(): void {
    cleanup();
    chats.value = {};
    conversations.value = [];
    acts.value = {};
    cardReads.value = {};
    lists.value = {};
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    attention.loaded.value = false;
    attention.detail.value = null;
    forgetWaiting();
    feedId.value = null;
    feedUnseen.value = false;
}

beforeEach(() => {
    freshPage();
    agent = 'campaign.get/own-list';
});

describe('the list card', () => {
    it('draws every row it will send, how each column was read, and the agent with its one button', async () => {
        renderConsole([spring(turn(list()))]);
        const card = await readCard(await openChat('Spring'));
        expect(words(card())).toContain('The list as Jason read it');
        expect(words(card())).toContain('Name → First name, Surname → Last name, Mail → Email; left out: Notes');
        expect(words(card())).toContain('7 rows');
        const table = card().querySelector('.card-list .card-tbl')!;
        expect(rowsOf(table)).toEqual(recorded.map((row, at) => [String(at + 1), ...row]));
        expect(words(card())).toContain('On record now');
        expect(words(card())).toContain('It is a draft: none of its work starts until it is started.');
        expect(words(card())).toContain(
            'Someone Jason already holds with this email address is added as they are; this list does not change their record.',
        );
        expect(words(card())).toContain('Anyone taken out of this agent before is not added back.');
        expect(words(card())).toContain('Nothing is sent on a channel where the person is on the do-not-contact list.');
        expect(addButton(card())?.hasAttribute('disabled')).toBe(false);
    });

    it('says how many addresses are in what the person wrote, and shows each one that is not, unfolded', async () => {
        renderConsole([spring(turn(list()))]);
        const card = await readCard(await openChat('Spring'));
        // Six rows carry an address; five of those stand in the person's message, compared without regard to case.
        expect(words(card().querySelector('.card-compare')!)).toContain(
            '5 of 6 addresses are in what you wrote in this chat',
        );
        expect(rowsOf(card().querySelector('.card-compare .card-tbl')!)).toEqual([
            ['7', 'Rafa', 'Costa', 'rafa@example.test'],
        ]);
    });

    it('finds an address the person wrote in another case, and in an earlier message of the chat', async () => {
        renderConsole([
            spring([
                { kind: 'user', text: 'One more for later: RAFA@Example.test' },
                { kind: 'text', text: 'Noted.', at },
                ...turn(list()),
            ]),
        ]);
        const card = await readCard(await openChat('Spring'));
        expect(words(card().querySelector('.card-compare')!)).toBe(
            '6 of 6 addresses are in what you wrote in this chat',
        );
    });

    it('says, when none of its addresses is in the chat, that rows the AI read from a file cannot be compared', async () => {
        renderConsole([spring(turn(list(), 'They are in ~/lists/spring.csv'))]);
        const card = await readCard(await openChat('Spring'));
        expect(words(card().querySelector('.card-compare')!)).toBe(
            'None of these addresses is in what you wrote in this chat. If the AI read these rows from a file, Jason cannot compare them with it.',
        );
        // It gates nothing.
        expect(addButton(card())?.hasAttribute('disabled')).toBe(false);
    });

    it('folds a long list after ten rows, and Show more shows the rest; it sends nothing', async () => {
        const long = Array.from({ length: 12 }, (_, n) => [`P${n}`, 'Lee', `p${n}@example.test`]);
        const fake = renderConsole([spring(turn(list(long), long.map((row) => row.join(' ')).join('\n')))]);
        const card = await readCard(await openChat('Spring'));
        const table = () => card().querySelector('.card-list .card-tbl')!;
        expect(rowsOf(table())).toHaveLength(10);
        fireEvent.click(within(card()).getByRole('button', { name: 'Show more' }));
        await waitFor(() => expect(rowsOf(table())).toHaveLength(12));
        expect(within(card()).queryByRole('button', { name: 'Show more' })).toBeNull();
        expect(adds(fake)).toEqual([]);
    });

    it('sends exactly the recorded request on a press, and draws the runtime’s answer row by row', async () => {
        const fake = renderConsole(
            [spring(turn(list()))],
            new FakeApi().answer('campaign.add_contacts', 'campaign.add_contacts/own-list'),
        );
        const card = await readCard(await openChat('Spring'));
        fireEvent.click(addButton(card())!, { detail: 1 });
        await waitFor(() => expect(words(card())).toContain('Added 3 · Already in this agent 2 · Not added 2'));
        // One write, of every row, matched by email, the empty address left out, with the fixed reason; the client
        // names the paired person as the actor, a human, as it does on every write.
        const { actor, rest } = recordedRequest('campaign.add_contacts/own-list');
        expect(adds(fake)).toEqual([{ operation: 'campaign.add_contacts', body: rest, kind: 'write' }]);
        expect(actor).toMatchObject({ type: 'human', id: expect.any(String) });
        expect(rest).toMatchObject({ match_by: 'email', reason: 'From a list given in the chat.' });
        expect(rowsOf(card().querySelector('.card-answer .card-tbl')!).map((row) => row.at(-1))).toEqual([
            'Added, as a new person',
            'Added, as the person Jason already held',
            'Already in this agent',
            'Taken out of this agent before; not added back',
            'Not added: an email value is a bare mailbox address with a dotted domain and no display name.',
            'Not added: the item carries no value for the requested match_by key; dedup was asked for, so it is not created blind.',
            'Added, as a new person',
        ]);
        // The button gives way to the answer, and to People, which opens the agent's people.
        expect(addButton(card())).toBeNull();
        expect(within(card()).getByRole('button', { name: 'People' })).toBeTruthy();
    });

    it('offers the button again on a page opened afresh, and a second press adds nobody twice', async () => {
        const first = renderConsole(
            [spring(turn(list()))],
            new FakeApi().answer('campaign.add_contacts', 'campaign.add_contacts/own-list'),
        );
        const before = await readCard(await openChat('Spring'));
        fireEvent.click(addButton(before())!);
        await waitFor(() => expect(words(before())).toContain('Added 3 · Already in this agent 2 · Not added 2'));
        expect(addButton(before())).toBeNull();
        expect(adds(first)).toHaveLength(1);
        // The page is opened again: nothing of the press was kept, so the button stands again.
        freshPage();
        const again = renderConsole(
            [spring(turn(list()))],
            new FakeApi().answer('campaign.add_contacts', 'campaign.add_contacts/own-list-again'),
        );
        const card = await readCard(await openChat('Spring'));
        expect(words(card())).not.toContain('Added 3');
        fireEvent.click(addButton(card())!);
        await waitFor(() => expect(words(card())).toContain('Added 0 · Already in this agent 5 · Not added 2'));
        expect(rowsOf(card().querySelector('.card-answer .card-tbl')!).map((row) => row.at(-1))).toEqual([
            'Already in this agent',
            'Already in this agent',
            'Already in this agent',
            'Taken out of this agent before; not added back',
            'Not added: an email value is a bare mailbox address with a dotted domain and no display name.',
            'Not added: the item carries no value for the requested match_by key; dedup was asked for, so it is not created blind.',
            'Already in this agent',
        ]);
        expect(adds(again)).toHaveLength(1);
    });

    it('offers the button on the conversation’s last list alone, never on an earlier one', async () => {
        renderConsole([spring([...turn(list(recorded.slice(0, 2))), ...turn(list())])]);
        const pane = await openChat('Spring');
        const lists = () => within(pane).getAllByRole('group', { name: 'Your list' });
        await waitFor(() => expect(within(lists()[1]!).queryByText('Reading...')).toBeNull());
        expect(lists()).toHaveLength(2);
        expect(addButton(lists()[1]!)?.hasAttribute('disabled')).toBe(false);
        // The earlier list stands as a stub; read, it says it is not the one added, and has no button.
        expect(addButton(lists()[0]!)).toBeNull();
        fireEvent.click(within(lists()[0]!).getByRole('button', { name: 'Show now' }));
        await waitFor(() =>
            expect(words(lists()[0]!)).toContain('Only the conversation’s last list is added from the chat.'),
        );
        expect(addButton(lists()[0]!)).toBeNull();
    });

    it('in an agent’s chat, says a list for another agent is not its own, and offers nothing to press', async () => {
        renderConsole([spring(turn(list(recorded, { campaign: 'cmp_2' })))]);
        const pane = await openChat('Spring');
        const card = await waitFor(() => within(pane).getByRole('group', { name: 'Your list' }));
        expect(words(card)).toBe('Your list This chat shows only its own agent.');
        expect(within(card).queryAllByRole('button')).toEqual([]);
    });

    it('leaves out an answer a click on a choice sent, whose words the AI wrote', async () => {
        const choice = {
            kind: 'choice',
            question: 'Add the last one too?',
            options: [{ label: 'rafa@example.test' }, { label: 'Skip it' }],
        };
        renderConsole([
            spring([
                { kind: 'user', text: `Add these people:\n${pasted}` },
                { kind: 'text', text: fenced(choice), at },
                { kind: 'user', text: 'rafa@example.test' },
                { kind: 'text', text: fenced(list()), at },
            ]),
        ]);
        const card = await readCard(await openChat('Spring'));
        expect(words(card().querySelector('.card-compare')!)).toContain(
            '5 of 6 addresses are in what you wrote in this chat',
        );
    });

    it('draws no comparison for a list matched by LinkedIn profile with no address column', async () => {
        const linkedin = {
            kind: 'own_list',
            match_by: 'linkedin',
            columns: [{ field: 'first_name' }, { field: 'linkedin' }],
            rows: [['Alma', 'linkedin.com/in/alma-novak']],
        };
        renderConsole([spring(turn(linkedin))]);
        const card = await readCard(await openChat('Spring'));
        expect(card().querySelector('.card-compare')).toBeNull();
        expect(words(card())).toContain(
            'Someone Jason already holds with this LinkedIn profile is added as they are; this list does not change their record.',
        );
    });

    it('takes a double click as one press, and a press from the keyboard as a press', async () => {
        const fake = new FakeApi().answer('campaign.add_contacts', 'campaign.add_contacts/own-list');
        const release = fake.hold('campaign.add_contacts');
        renderConsole([spring(turn(list()))], fake);
        const card = await readCard(await openChat('Spring'));
        // The second click of a double click alone, as if the first had landed elsewhere: no press.
        fireEvent.click(addButton(card())!, { detail: 2 });
        expect(adds(fake)).toHaveLength(0);
        // A press from the keyboard has no click count, and is a press.
        fireEvent.click(addButton(card())!, { detail: 0 });
        fireEvent.click(addButton(card())!, { detail: 2 });
        await waitFor(() => expect(adds(fake)).toHaveLength(1));
        release();
        await waitFor(() => expect(words(card())).toContain('Added 3'));
        expect(adds(fake)).toHaveLength(1);
    });

    it('holds its button from the press until the agent read after it has answered', async () => {
        const fake = new FakeApi().answer('campaign.add_contacts', 'campaign.add_contacts/own-list');
        renderConsole([spring(turn(list()))], fake);
        const card = await readCard(await openChat('Spring'));
        const reads = () => fake.calls.filter((call) => call.operation === 'campaign.get').length;
        const before = reads();
        const release = fake.hold('campaign.get');
        fireEvent.click(addButton(card())!);
        await waitFor(() => expect(reads()).toBe(before + 1));
        // The runtime has answered the add; the agent is being read again, and nothing can be pressed meanwhile.
        expect(adds(fake)).toHaveLength(1);
        expect(within(card()).getByText('Adding the list...')).toBeTruthy();
        expect(addButton(card())?.hasAttribute('disabled')).toBe(true);
        expect(words(card())).not.toContain('Added 3');
        fireEvent.click(addButton(card())!);
        expect(adds(fake)).toHaveLength(1);
        release();
        await waitFor(() => expect(words(card())).toContain('Added 3 · Already in this agent 2 · Not added 2'));
        expect(within(card()).queryByText('Adding the list...')).toBeNull();
    });

    it('draws a request refused whole with the runtime’s words, and the agent as it is after it', async () => {
        const fake = renderConsole(
            [spring(turn(list()))],
            new FakeApi().answer('campaign.add_contacts', 'campaign.add_contacts/own-list-archived'),
        );
        const card = await readCard(await openChat('Spring'));
        // The agent was archived elsewhere after the card read it.
        agent = 'campaign.get/own-list-archived';
        fireEvent.click(addButton(card())!);
        await waitFor(() => expect(within(card()).getByRole('alert')).toBeTruthy());
        expect(words(within(card()).getByRole('alert'))).toBe(
            "campaign_archived Campaign 'cmp_1' is archived; only journal entries can still be appended.",
        );
        expect(words(card())).toContain('This agent is archived, so nobody can be added to it.');
        expect(addButton(card())).toBeNull();
        expect(adds(fake)).toHaveLength(1);
    });

    it('offers no button on an archived agent', async () => {
        agent = 'campaign.get/own-list-archived';
        renderConsole([spring(turn(list()))]);
        const card = await readCard(await openChat('Spring'));
        expect(words(card())).toContain('This agent is archived, so nobody can be added to it.');
        expect(addButton(card())).toBeNull();
    });

    it('offers no button on a list something stands after, nor while its turn runs', async () => {
        renderConsole([spring([...turn(list()), { kind: 'user', text: 'Wait, one more.' }])]);
        const later = await readCard(await openChat('Spring'));
        expect(addButton(later())).toBeNull();
        expect(words(later())).toContain('Only the conversation’s last list is added from the chat.');
        cleanup();
        chats.value = {};
        conversations.value = [];
        cardReads.value = {};
        renderConsole([spring(turn(list()), { running: true })]);
        const running = await readCard(await openChat('Spring'));
        expect(addButton(running())).toBeNull();
        expect(words(running())).not.toContain('Only the conversation’s last list');
    });

    it('says what adding does in a running agent whose every message waits for the person', async () => {
        agent = 'campaign.get/own-list-running';
        renderConsole([spring(turn(list()))]);
        const card = await readCard(await openChat('Spring'));
        const sentence = card().querySelector('.card-status')!;
        expect(words(sentence)).toBe(
            'This agent is running, and every message it writes to people who have not replied yet waits for you to approve it.',
        );
        // The sentence stands right above the button.
        expect(sentence.nextElementSibling?.classList.contains('card-nav')).toBe(true);
    });

    it('says, in a paused agent whose messages to new people go automatically, what happens once it runs', async () => {
        agent = 'campaign.get/own-list-paused';
        renderConsole([spring(turn(list()))]);
        const card = await readCard(await openChat('Spring'));
        expect(words(card().querySelector('.card-status')!)).toBe(
            'It is paused: none of its work starts but what stops it. When it runs under this send mode, a message it writes to someone added here can be sent without asking you, once Jason’s own review of it passes.',
        );
        // A draft whose every message waits says only that it is a draft.
        agent = 'campaign.get/own-list';
        fireEvent.click(within(card()).getByRole('button', { name: 'Refresh' }));
        await waitFor(() =>
            expect(words(card().querySelector('.card-status')!)).toBe(
                'It is a draft: none of its work starts until it is started.',
            ),
        );
    });

    it('says, in a running agent whose messages to new people go automatically, that they can go without asking', async () => {
        agent = 'campaign.get/own-list-automatic';
        renderConsole([spring(turn(list()))]);
        const card = await readCard(await openChat('Spring'));
        expect(words(card().querySelector('.card-status')!)).toBe(
            'This agent is running, and its messages to people who have not replied yet go automatically: one it writes to someone added here can be sent without asking you, once Jason’s own review of it passes.',
        );
        expect(addButton(card())?.hasAttribute('disabled')).toBe(false);
    });

    it('is drawn as one line in a set-up chat, which holds no agent to add to', async () => {
        renderConsole([{ id: setupChat, campaignId: null, title: 'Looking', entries: turn(list()) }]);
        const pane = await openChat('Looking');
        await waitFor(() =>
            expect(words(pane)).toContain('Jason’s card did not hold what a list card needs, so it is not shown.'),
        );
        expect(within(pane).queryByRole('group', { name: 'Your list' })).toBeNull();
    });
});
