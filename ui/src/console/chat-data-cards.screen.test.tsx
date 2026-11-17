import { act as flush, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import type { DesktopConversationRecord, DesktopKeptEntry } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { journalLine, stubDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { shownAgent } from './agent-place';
import { fixtures } from '../test/fixtures';
import { nothingWaitsInChats } from '../test/waiting';
import { acts } from './card-acts';
import { cardReads } from './card-frame';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { activePanel, chats, conversations, feedId, feedUnseen } from './state';
import { forgetWaiting } from './waiting';

// The data cards the chat's AI asks for by an id alone, as a person meets them: a card reads the runtime once, when it
// is drawn, says when ("As of"), shows the runtime's answer under "On record now" and nothing the runtime did not say,
// reads again only on its own Refresh, and never on a line of the journal. A conversation opened again reads its last
// such card alone; an earlier one stands as a stub until "Show now". Nothing on any of them decides anything.

const setupChat = 'conv_01JDDATA00000000000000000S';
const springChat = 'conv_01JDDATA00000000000000000A';
/** The moment the cards' story read Spring launch's status: its thirty days end then. */
const nine05 = '2026-10-16T09:05:00.000Z';
/** The moment the story read the archived campaign, two minutes later. */
const nine07 = '2026-10-16T09:07:00.000Z';

const fenced = (value: unknown) => `\`\`\`card\n${JSON.stringify(value)}\n\`\`\``;
const turn = (value: unknown, words = 'Here it is.'): DesktopKeptEntry[] => [
    { kind: 'user', text: 'Show me' },
    { kind: 'text', text: `${words}\n\n${fenced(value)}`, at: nine05 },
];

/** The console over a fake that answers what the console reads at its start, with the given conversations kept. */
function renderConsole(
    records: (Partial<DesktopConversationRecord> & { id: string })[],
    fake: FakeApi = nothingWaitsInChats(new FakeApi()),
    journal: StubStream = stubStream(),
    desktop: StubDesktop = stubDesktop(),
): { fake: FakeApi; desktop: StubDesktop; journal: StubStream } {
    if (!fake.offers('approval.list')) fake.answer('approval.list', 'approval.list/pending');
    if (!fake.offers('decision.list')) fake.answer('decision.list', 'decision.list/pending');
    fake.answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('system.info', 'system.info/with-ui');
    if (!fake.offers('campaign.summary')) fake.answer('campaign.summary', 'campaign.summary/all');
    // Needs you reads the sending name under an email's From (`needs-sender-line.screen.test.tsx`); here it is answered
    // with one recorded list of accounts, which draws one line and holds nothing.
    if (!fake.offers('sender.list')) fake.answerAnyRequest('sender.list', 'sender.list/card-message-sender');
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

const setUp = (entries: DesktopKeptEntry[]) => ({ id: setupChat, campaignId: null, title: 'Looking', entries });
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

const card = (pane: HTMLElement, name: string | RegExp) => within(pane).getByRole('group', { name });
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
/** What the cards asked the runtime, by operation and body, in order: only the operations a data card reads. */
const cardOps = ['campaign.get', 'campaign.summary', 'campaign.activity', 'campaign.list_contacts', 'contact.list'];
const reads = (fake: FakeApi, ops: readonly string[] = cardOps) =>
    fake.calls.filter((each) => ops.includes(each.operation)).map((each) => ({ op: each.operation, body: each.body }));
/**
 * The reads the console makes at its start, which no card makes: its campaigns' counts; and the agent's header over its
 * chat, which reads the agent once as it is drawn, before anything the chat draws reads.
 */
const idsOf = (body: object): readonly string[] => (body as { campaign_ids?: string[] }).campaign_ids ?? [];
const notStart = (fake: FakeApi) => {
    let headed = shownAgent.value?.id ?? null;
    return reads(fake).filter((each) => {
        if (each.op === 'campaign.summary' && idsOf(each.body).length > 1) return false;
        if (each.op !== 'campaign.get' || (each.body as { campaign_id?: string }).campaign_id !== headed) return true;
        headed = null;
        return false;
    });
};
/** The numbers a card shows, in order, from its count tiles. */
const tiles = (element: HTMLElement) =>
    [...element.querySelectorAll('.card-count')].map((tile) => [...tile.children].map(words).join(' '));

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(nine05));
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
    vi.useRealTimers();
});

describe('the status card', () => {
    const status = { kind: 'agent_status' };

    function springStatus(fake = nothingWaitsInChats(new FakeApi())): FakeApi {
        return fake
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status')
            .answer('campaign.activity', 'campaign.activity/card-status-30-days');
    }

    it('reads the agent once and shows only what the runtime counts, under its name, as of the minute it was read', async () => {
        const fake = springStatus();
        const release = fake.hold('campaign.summary', (body) => idsOf(body).length === 1);
        renderConsole([spring(turn(status))], fake);
        const pane = await openChat('Spring');
        const reading = await waitFor(() => card(pane, 'Agent'));
        expect(within(reading).getByText('Reading...')).toBeTruthy();
        // Refresh stays usable while a read is under way: a read that never answers is never the card's last word.
        expect(within(reading).getByRole('button', { name: 'Refresh' }).hasAttribute('disabled')).toBe(false);
        release();
        const read = await waitFor(() => card(pane, 'Agent: Spring launch'));
        expect(within(read).getByText('As of 09:05')).toBeTruthy();
        expect(within(read).getByText('On record now')).toBeTruthy();
        expect(within(read).getByText('It is active.')).toBeTruthy();
        // The state is a pill at the top of the card, in the runtime's own status and the agent list's tone.
        const pill = read.querySelector('.card-pills .card-state');
        expect(pill?.textContent).toBe('Active');
        expect(pill?.classList.contains('green')).toBe(true);
        expect(tiles(read)).toEqual([
            '12 People',
            '2 People waiting on you',
            '2 Working',
            '0 Failed',
            '0 In the sequence',
            '1 Out',
            '1 Contacted',
            '1 Replied',
            '0 Bounced',
            '0 Opted out',
        ]);
        expect(words(read)).toContain('People who have not replied yet: Every message waits for you');
        expect(words(read)).not.toMatch(/Meeting|%|Interested|Sequence pill|Adaptive/i);
        // Every count is the recording's own number for its key, and no other number is on the card but the minute
        // it was read and the thirty days of its window.
        const counted: Record<string, number> = {
            ...fixtures['campaign.summary/card-status'].response.campaigns[0]!,
            ...fixtures['campaign.activity/card-status-30-days'].response.campaigns[0]!,
        } as unknown as Record<string, number>;
        const tilesByKey = [...read.querySelectorAll<HTMLElement>('.card-count')].map((tile) => [
            tile.dataset['count']!,
            Number(tile.querySelector('b')!.textContent),
        ]);
        expect(tilesByKey).toEqual(tilesByKey.map(([key]) => [key, counted[key as string]]));
        const rest = read.cloneNode(true) as HTMLElement;
        for (const tile of rest.querySelectorAll('.card-count, .card-asof')) tile.remove();
        expect(words(rest).replace('Last 30 days', '')).not.toMatch(/\d/);
        expect(notStart(fake)).toEqual([
            { op: 'campaign.get', body: { campaign_id: 'cmp_1' } },
            { op: 'campaign.summary', body: { campaign_ids: ['cmp_1'], exclude_archived_contacts: true } },
            {
                op: 'campaign.activity',
                body: {
                    campaign_ids: ['cmp_1'],
                    exclude_archived_contacts: true,
                    since: '2026-09-17T00:00:00.000Z',
                    until: nine05,
                },
            },
        ]);
        // Its controls open pages; one shows what waits. None of them acts.
        const buttons = within(read)
            .getAllByRole('button')
            .map((button) => words(button));
        expect(buttons).toEqual(['Refresh', 'People', 'Inbox', 'Results', 'Settings', 'Show what waits']);
    });

    it('reads again on Refresh, the same reads and nothing else, and draws the newer answer', async () => {
        const fake = springStatus(nothingWaitsInChats(new FakeApi()));
        fake.answer(
            'campaign.summary',
            'campaign.summary/all',
            'campaign.summary/card-status',
            'campaign.summary/card-status-later',
        );
        renderConsole([spring(turn(status))], fake);
        const pane = await openChat('Spring');
        const read = await waitFor(() => card(pane, 'Agent: Spring launch'));
        await waitFor(() => expect(tiles(read)[0]).toBe('12 People'));
        const first = notStart(fake);
        fireEvent.click(within(read).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => expect(tiles(card(pane, 'Agent: Spring launch'))[0]).toBe('13 People'));
        expect(notStart(fake)).toEqual([...first, ...first]);
    });

    it('is not read again by a line of the journal', async () => {
        const fake = springStatus();
        const { journal } = renderConsole([spring(turn(status))], fake);
        const pane = await openChat('Spring');
        await waitFor(() => expect(tiles(card(pane, 'Agent: Spring launch'))).toHaveLength(10));
        const before = notStart(fake).length;
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_31', kind: 'campaign_paused', campaign_id: 'cmp_1', ts: nine05 }));
        });
        await flush(async () => {
            await Promise.resolve();
        });
        expect(notStart(fake)).toHaveLength(before);
    });

    it('says a draft’s sentence, and shows no In the sequence while no sequence is linked', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status-draft')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status-draft');
        // The draft's thirty days were not recorded: the count says why in its place, the rest stands as read.
        fake.unreachable('campaign.activity');
        renderConsole([setUp(turn({ kind: 'agent_status', campaign: 'cmp_3' }))], fake);
        const pane = await openChat('Looking');
        const read = await waitFor(() => card(pane, 'Agent: Summer reach'));
        expect(within(read).getByText('It is a draft: none of its work starts until it is started.')).toBeTruthy();
        expect(tiles(read)).toEqual(['0 People', '0 People waiting on you', '0 Working', '0 Failed', '0 Out']);
        expect(within(read).getByRole('alert').textContent).toContain('Failed to fetch');
        expect(within(read).queryByRole('button', { name: 'Show what waits' })).toBeNull();
    });

    it('says an archived agent is archived, with its counts as read', async () => {
        vi.setSystemTime(new Date(nine07));
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-archived')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-archived')
            .answer('campaign.activity', 'campaign.activity/card-archived-30-days');
        renderConsole([setUp(turn({ kind: 'agent_status', campaign: 'cmp_5' }))], fake);
        const pane = await openChat('Looking');
        const read = await waitFor(() => card(pane, 'Agent: Winter plan'));
        await waitFor(() => expect(within(read).getByText('It is archived.')).toBeTruthy());
        expect(tiles(read)[0]).toBe('2 People');
        expect(within(read).getByText('As of 09:07')).toBeTruthy();
    });

    it('says an agent this installation does not hold is not in it, and reads nothing more', async () => {
        const fake = nothingWaitsInChats(new FakeApi()).answer('campaign.get', 'campaign.get/card-unknown-campaign');
        renderConsole([setUp(turn({ kind: 'agent_status', campaign: 'cmp_4' }))], fake);
        const pane = await openChat('Looking');
        await waitFor(() => within(card(pane, 'Agent')).getByText('This agent is not in this installation.'));
        expect(notStart(fake)).toEqual([{ op: 'campaign.get', body: { campaign_id: 'cmp_4' } }]);
    });

    it('in an agent’s chat, reads nothing about another agent and says so', async () => {
        const fake = nothingWaitsInChats(new FakeApi());
        renderConsole([spring(turn({ kind: 'agent_status', campaign: 'cmp_2' }))], fake);
        const pane = await openChat('Spring');
        await waitFor(() => within(card(pane, 'Agent')).getByText('This chat shows only its own agent.'));
        expect(notStart(fake)).toEqual([]);
    });

    it('says why a read failed, with the runtime’s words', async () => {
        const fake = nothingWaitsInChats(new FakeApi());
        fake.unreachable('campaign.get');
        renderConsole([spring(turn(status))], fake);
        const pane = await openChat('Spring');
        await waitFor(() =>
            expect(within(card(pane, 'Agent')).getByRole('alert').textContent).toContain('Failed to fetch'),
        );
        expect(notStart(fake)).toEqual([{ op: 'campaign.get', body: { campaign_id: 'cmp_1' } }]);
    });
});

describe('the people list', () => {
    it('lists an agent’s people as the runtime holds them, its count in the footer, never a channel’s value', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.list_contacts', 'campaign.list_contacts/card-people-first')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status');
        renderConsole([spring(turn({ kind: 'people_list' }))], fake);
        const pane = await openChat('Spring');
        const read = await waitFor(() => card(pane, 'People'));
        await waitFor(() => within(read).getByText('12 people in this agent'));
        const rows = within(within(read).getByRole('list', { name: 'People' })).getAllByRole('listitem');
        expect(rows).toHaveLength(10);
        expect(words(rows[0]!)).toBe('Ines Duarte Kavali · Email Waiting');
        expect(read.textContent).not.toContain('@');
        expect(within(read).getByText('As of 09:05')).toBeTruthy();
        expect(within(read).queryByText(/Jason’s query/)).toBeNull();
        expect(notStart(fake)).toEqual([
            { op: 'campaign.get', body: { campaign_id: 'cmp_1' } },
            {
                op: 'campaign.list_contacts',
                body: { campaign_id: 'cmp_1', limit: 10, exclude_archived_contacts: true },
            },
            { op: 'campaign.summary', body: { campaign_ids: ['cmp_1'], exclude_archived_contacts: true } },
        ]);
    });

    it('shows the AI’s search as its query, outside the facts, and asks the runtime that text match', async () => {
        const fake = nothingWaitsInChats(new FakeApi()).answer('contact.list', 'contact.list/card-people-query');
        renderConsole([setUp(turn({ kind: 'people_list', search: 'ines' }))], fake);
        const pane = await openChat('Looking');
        const read = await waitFor(() => card(pane, 'People'));
        await waitFor(() => within(read).getByText('Ines Duarte'));
        const query = within(read).getByText('Jason’s query: “ines”');
        expect(query.closest('.card-facts')).toBeNull();
        expect(within(read).getByText('A text match in names, companies, titles, addresses and fields.')).toBeTruthy();
        expect(read.querySelector('.card-more')).toBeNull();
        expect(notStart(fake)).toEqual([{ op: 'contact.list', body: { query: 'ines', limit: 10 } }]);
    });

    it('points at the People page when the runtime has more of everyone, and says when nobody matches', async () => {
        const fake = nothingWaitsInChats(new FakeApi()).answer('contact.list', 'contact.list/card-people-everyone');
        renderConsole([setUp(turn({ kind: 'people_list' }))], fake);
        const pane = await openChat('Looking');
        await waitFor(() => within(card(pane, 'People')).getByRole('button', { name: 'More on the People page' }));
        cleanup();
        chats.value = {};
        cardReads.value = {};
        const nobody = nothingWaitsInChats(new FakeApi()).answer('contact.list', 'contact.list/card-people-nobody');
        renderConsole([setUp(turn({ kind: 'people_list', search: 'nobody-by-this-name' }))], nobody);
        const again = await openChat('Looking');
        await waitFor(() => within(card(again, 'People')).getByText('Nobody matches.'));
    });

    it('counts the people a standing names, and says an empty agent holds nobody yet', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.list_contacts', 'campaign.list_contacts/card-people-waiting')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status');
        renderConsole([spring(turn({ kind: 'people_list', standing: 'waiting' }))], fake);
        const pane = await openChat('Spring');
        await waitFor(() => within(card(pane, 'People')).getByText('2 people in this agent are counted under Waiting'));
        cleanup();
        chats.value = {};
        cardReads.value = {};
        const empty = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status-draft')
            .answer('campaign.list_contacts', 'campaign.list_contacts/card-people-empty')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status-draft');
        renderConsole([setUp(turn({ kind: 'people_list', campaign: 'cmp_3' }))], empty);
        const again = await openChat('Looking');
        await waitFor(() => within(card(again, 'People')).getByText('Nobody is in this agent yet.'));
    });

    it('says an archived agent is archived above its people', async () => {
        vi.setSystemTime(new Date(nine07));
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-archived')
            .answer('campaign.list_contacts', 'campaign.list_contacts/card-archived')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-archived');
        renderConsole([setUp(turn({ kind: 'people_list', campaign: 'cmp_5' }))], fake);
        const pane = await openChat('Looking');
        const read = await waitFor(() => card(pane, 'People'));
        await waitFor(() => within(read).getByText('This agent is archived.'));
        expect(within(read).getByText('Mira Sato')).toBeTruthy();
        expect(within(read).getByText('2 people in this agent')).toBeTruthy();
    });

    it('says an unknown agent is not in this installation, and in an agent’s chat reads no other agent', async () => {
        const fake = nothingWaitsInChats(new FakeApi()).answer('campaign.get', 'campaign.get/card-unknown-campaign');
        renderConsole(
            [
                setUp(turn({ kind: 'people_list', campaign: 'cmp_4' })),
                spring(turn({ kind: 'people_list', campaign: 'cmp_2' })),
            ],
            fake,
        );
        const pane = await openChat('Looking');
        await waitFor(() => within(card(pane, 'People')).getByText('This agent is not in this installation.'));
        const agent = await openChat('Spring');
        await waitFor(() => within(card(agent, 'People')).getByText('This chat shows only its own agent.'));
        expect(notStart(fake)).toEqual([{ op: 'campaign.get', body: { campaign_id: 'cmp_4' } }]);
    });

    it('says why its read failed', async () => {
        const fake = nothingWaitsInChats(new FakeApi());
        fake.unreachable('contact.list');
        renderConsole([setUp(turn({ kind: 'people_list' }))], fake);
        const pane = await openChat('Looking');
        await waitFor(() =>
            expect(within(card(pane, 'People')).getByRole('alert').textContent).toContain('Failed to fetch'),
        );
    });

    it('moves its time on Refresh', async () => {
        const fake = nothingWaitsInChats(new FakeApi()).answer('contact.list', 'contact.list/card-people-query');
        renderConsole([setUp(turn({ kind: 'people_list', search: 'ines' }))], fake);
        const pane = await openChat('Looking');
        await waitFor(() => within(card(pane, 'People')).getByText('As of 09:05'));
        vi.setSystemTime(new Date('2026-10-16T09:06:30.000Z'));
        fireEvent.click(within(card(pane, 'People')).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => within(card(pane, 'People')).getByText('As of 09:06'));
        expect(notStart(fake)).toEqual([
            { op: 'contact.list', body: { query: 'ines', limit: 10 } },
            { op: 'contact.list', body: { query: 'ines', limit: 10 } },
        ]);
    });
});

describe('what waits for a decision', () => {
    it('lists every agent’s items, questions first, each with Open and nothing that decides', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('approval.list', 'approval.list/card-needs-all')
            .answer('decision.list', 'decision.list/card-needs-all');
        renderConsole([setUp(turn({ kind: 'needs' }))], fake);
        const pane = await openChat('Looking');
        const read = await waitFor(() => card(pane, 'Waiting for your decision'));
        await waitFor(() => within(read).getByText('2 approvals and 2 questions waiting for your decision'));
        const rows = within(within(read).getByRole('list')).getAllByRole('listitem').map(words);
        expect(rows[0]).toMatch(
            /^Question Should Tomas hear about the autumn pricing before the spring one\? Tomas Berg · Spring launch · 5 min Open$/,
        );
        expect(rows[1]).toMatch(/^Question Keep Lena .* Lena Pohl · Autumn outreach · 5 min Open$/);
        expect(rows[2]).toMatch(/^Message Ines Duarte · Spring launch · 5 min Open$/);
        expect(rows[3]).toMatch(/^Message Kai Lindt · Autumn outreach · 5 min Open$/);
        const buttons = within(read).getAllByRole('button').map(words);
        expect(buttons).toEqual(['Refresh', 'Open', 'Open', 'Open', 'Open']);
        expect(within(read).queryByRole('button', { name: /Approve|Reject|Edit|Answer|Send/ })).toBeNull();
    });

    it('in the agent’s own chat points at the waiting cards below, as of its read', async () => {
        const fake = new FakeApi()
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('approval.list', 'approval.list/pending', 'approval.list/card-status-waiting')
            .answer('decision.list', 'decision.list/pending', 'decision.list/card-status-waiting');
        // The waiting cards below read each item whole, as the story recorded them.
        fake.answer('approval.get', 'approval.get/card-needs-pending').answer(
            'decision.get',
            'decision.get/card-needs-question',
        );
        renderConsole([spring(turn({ kind: 'needs' }))], fake);
        const pane = await openChat('Spring');
        const read = await waitFor(() => card(pane, 'Waiting for your decision'));
        await waitFor(() => within(read).getByText('1 approval and 1 question waiting for your decision'));
        expect(within(read).getByText('As of 09:05, they were below this conversation.')).toBeTruthy();
        expect(within(read).getAllByRole('button').map(words)).toEqual(['Refresh', 'Show them']);
        expect(within(read).queryByRole('list')).toBeNull();
    });

    it('says when nothing waits in an archived agent, that it is archived', async () => {
        vi.setSystemTime(new Date(nine07));
        const fake = new FakeApi()
            .answer('campaign.get', 'campaign.get/card-archived')
            .answer('approval.list', 'approval.list/pending', 'approval.list/card-archived')
            .answer('decision.list', 'decision.list/pending', 'decision.list/card-archived');
        renderConsole([setUp(turn({ kind: 'needs', campaign: 'cmp_5' }))], fake);
        const pane = await openChat('Looking');
        // A read that lists nothing is not headed "Waiting for your decision": its heading says no more than its body.
        const read = await waitFor(() => card(pane, 'Your decisions'));
        await waitFor(() => within(read).getByText('This agent is archived.'));
        expect(within(read).getByText('Nothing is waiting for your decision.')).toBeTruthy();
        expect(within(pane).queryByRole('group', { name: 'Waiting for your decision' })).toBeNull();
    });

    it('says an unknown agent is not in this installation, and reads no list for it', async () => {
        const fake = nothingWaitsInChats(new FakeApi()).answer('campaign.get', 'campaign.get/card-unknown-campaign');
        renderConsole([setUp(turn({ kind: 'needs', campaign: 'cmp_4' }))], fake);
        const pane = await openChat('Looking');
        await waitFor(() =>
            within(card(pane, 'Waiting for your decision')).getByText('This agent is not in this installation.'),
        );
        expect(
            fake.calls
                .filter(
                    (each) =>
                        'campaign_id' in each.body && (each.body as { campaign_id: string }).campaign_id === 'cmp_4',
                )
                .map((each) => each.operation),
        ).toEqual(['campaign.get']);
    });

    it('says why its read failed', async () => {
        const fake = nothingWaitsInChats(new FakeApi());
        fake.unreachable('decision.list', (body) => Object.keys(body).length === 0);
        renderConsole([setUp(turn({ kind: 'needs' }))], fake);
        const pane = await openChat('Looking');
        await waitFor(() =>
            expect(within(card(pane, 'Waiting for your decision')).getByRole('alert').textContent).toContain(
                'Failed to fetch',
            ),
        );
    });
});

describe('a conversation opened again', () => {
    it('reads its last card alone; an earlier one is a stub until Show now reads it', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.list_contacts', 'campaign.list_contacts/card-people-first')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status')
            .answer('campaign.activity', 'campaign.activity/card-status-30-days');
        renderConsole(
            [
                spring([
                    ...turn({ kind: 'people_list' }),
                    ...turn({ kind: 'choice', question: 'Next?', options: [{ label: 'More' }, { label: 'Done' }] }),
                    ...turn({ kind: 'agent_status' }, 'And its status.'),
                ]),
            ],
            fake,
        );
        const pane = await openChat('Spring');
        await waitFor(() => expect(tiles(card(pane, 'Agent: Spring launch'))).toHaveLength(10));
        const stub = card(pane, 'People');
        expect(words(stub)).toBe('People Show now');
        expect(reads(fake, ['campaign.list_contacts'])).toEqual([]);
        fireEvent.click(within(stub).getByRole('button', { name: 'Show now' }));
        await waitFor(() => within(card(pane, 'People')).getByText('12 people in this agent'));
        expect(reads(fake, ['campaign.list_contacts'])).toHaveLength(1);
        // The status card already read stays as it was read: Show now read the people alone.
        expect(reads(fake, ['campaign.activity'])).toHaveLength(1);
    });

    it('reads a card that arrives while it is drawn, and keeps the earlier one as it was read', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.list_contacts', 'campaign.list_contacts/card-people-first')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status')
            .answer('campaign.activity', 'campaign.activity/card-status-30-days');
        const { desktop } = renderConsole([spring(turn({ kind: 'people_list' }))], fake);
        const pane = await openChat('Spring');
        await waitFor(() => within(card(pane, 'People')).getByText('12 people in this agent'));
        // The person asks again, and the AI's answer is a status card: a turn of its own, drawn while the chat is shown.
        fireEvent.change(within(pane).getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'And how is it going?' },
        });
        fireEvent.click(within(pane).getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        flush(() => {
            desktop.emit(springChat, { kind: 'text', text: fenced({ kind: 'agent_status' }), at: nine05 });
        });
        await waitFor(() => expect(tiles(card(pane, 'Agent: Spring launch'))).toHaveLength(10));
        expect(within(card(pane, 'People')).getByText('12 people in this agent')).toBeTruthy();
        expect(reads(fake, ['campaign.list_contacts'])).toHaveLength(1);
    });
});

describe('a card’s read, as the chat moves on', () => {
    const springRead = () =>
        nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status')
            .answer('campaign.activity', 'campaign.activity/card-status-30-days');

    it('is not read again when its turn is marked a plan, a later turn ends, or the chat’s mode changes', async () => {
        const fake = springRead();
        const { desktop } = renderConsole([spring(turn({ kind: 'agent_status' }))], fake);
        const pane = await openChat('Spring');
        await waitFor(() => expect(tiles(card(pane, 'Agent: Spring launch'))).toHaveLength(10));
        // The desktop marks the turn's words a plan, then a later turn ends, and the mode changes: each draws the
        // entry afresh with the same words.
        flush(() => desktop.emit(springChat, { kind: 'done', error: null, plan: true }));
        flush(() => desktop.emit(springChat, { kind: 'done', error: null }));
        await waitFor(() => expect(tiles(card(pane, 'Agent: Spring launch'))).toHaveLength(10));
        flush(() => desktop.emit(springChat, { kind: 'done', error: null, plan: true }));
        flush(() => desktop.emit(springChat, { kind: 'mode', mode: 'ask_first' }));
        await waitFor(() => expect(tiles(card(pane, 'Agent: Spring launch'))).toHaveLength(10));
        await flush(async () => {
            await Promise.resolve();
        });
        expect(reads(fake, ['campaign.activity'])).toHaveLength(1);
        expect(within(card(pane, 'Agent: Spring launch')).getByText('As of 09:05')).toBeTruthy();
    });

    it('is read again under a chat’s new campaign, never drawn under it from before', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('contact.list', 'contact.list/card-people-everyone')
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.list_contacts', 'campaign.list_contacts/card-people-first')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status');
        const { desktop } = renderConsole([setUp(turn({ kind: 'people_list' }))], fake);
        const pane = await openChat('Looking');
        await waitFor(() => within(card(pane, 'People')).getByText('More on the People page'));
        flush(() => desktop.emit(setupChat, { kind: 'attached', campaignId: 'cmp_1', title: 'Spring launch' }));
        await waitFor(() => within(card(pane, 'People')).getByText('12 people in this agent'));
        expect(within(card(pane, 'People')).queryByText('More on the People page')).toBeNull();
        expect(reads(fake, ['campaign.list_contacts'])).toHaveLength(1);
    });
});

describe('where a card’s controls lead', () => {
    it('opens a person from a row and the agent’s People tab from the footer', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.list_contacts', 'campaign.list_contacts/card-people-first')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status');
        // The screens a link opens read what they show; this test is about where it lands.
        fake.unreachable('contact.get');
        fake.unreachable('contact.list');
        fake.unreachable('campaign.summary', (body) => idsOf(body).length === 0);
        fake.unreachable('campaign.list_contacts', (body) => (body as { limit?: number }).limit !== 10);
        renderConsole([spring(turn({ kind: 'people_list' }))], fake);
        let pane = await openChat('Spring');
        await waitFor(() => within(card(pane, 'People')).getByText('12 people in this agent'));
        fireEvent.click(within(card(pane, 'People')).getByRole('button', { name: /^Ines Duarte/ }));
        await waitFor(() => expect(route.value).toMatchObject({ screen: 'people', id: 'cnt_1' }));
        expect(activePanel.value).toBe('page');
        pane = await openChat('Spring');
        fireEvent.click(within(card(pane, 'People')).getByRole('button', { name: '12 people in this agent' }));
        await waitFor(() => expect(route.value).toMatchObject({ screen: 'campaigns', id: 'cmp_1', tab: 'people' }));
        expect(activePanel.value).toBe('page');
    });

    it('opens the item an Open names in Needs you, by its own read, and an item decided since as it is now', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('approval.list', 'approval.list/card-needs-all')
            .answer('decision.list', 'decision.list/card-needs-all')
            .answer('decision.get', 'decision.get/card-needs-question')
            .answer('approval.get', 'approval.get/card-needs-decided');
        renderConsole([setUp(turn({ kind: 'needs' }))], fake);
        const pane = await openChat('Looking');
        const read = await waitFor(() => card(pane, 'Waiting for your decision'));
        await waitFor(() => within(read).getByText('2 approvals and 2 questions waiting for your decision'));
        // Needs you reads its own rows whole as it is shown; Autumn's approval is not this story's to answer.
        fake.unreachable('approval.get', (body) => (body as { approval_id?: string }).approval_id !== 'apr_1');
        const opened = (op: string, id: string) =>
            fake.calls.filter((each) => each.operation === op && Object.values(each.body).includes(id)).length;
        fireEvent.click(within(read).getAllByRole('button', { name: 'Open' })[0]!);
        await waitFor(() => expect(attention.detail.value?.item.id).toBe('dec_1'));
        expect(activePanel.value).toBe('needs');
        // The Open read that question once and no other.
        expect(reads(fake, ['decision.get'])).toEqual([{ op: 'decision.get', body: { decision_id: 'dec_1' } }]);
        // Ines's approval was approved after the card was read: Needs you shows it as it stands now, by one read of
        // its own.
        const before = opened('approval.get', 'apr_1');
        await openChat('Looking');
        fireEvent.click(
            within(
                card(document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!, 'Waiting for your decision'),
            ).getAllByRole('button', { name: 'Open' })[2]!,
        );
        await waitFor(() => expect(attention.detail.value?.item.id).toBe('apr_1'));
        expect(attention.detail.value?.item.status).toBe('approved');
        expect(opened('approval.get', 'apr_1')).toBe(before + 1);
        expect(reads(fake, ['decision.get'])).toHaveLength(1);
    });

    it('shows Needs you from Show what waits when no waiting card is below the conversation', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status')
            .answer('campaign.activity', 'campaign.activity/card-status-30-days');
        // Needs you reads its own rows as it is shown; they are not this test's.
        fake.unreachable('approval.get');
        fake.unreachable('decision.get');
        renderConsole([spring(turn({ kind: 'agent_status' }))], fake);
        const pane = await openChat('Spring');
        const read = await waitFor(() => card(pane, 'Agent: Spring launch'));
        await waitFor(() => within(read).getByRole('button', { name: 'Show what waits' }));
        expect(pane.querySelector('.chat-waiting-for')).toBeNull();
        fireEvent.click(within(read).getByRole('button', { name: 'Show what waits' }));
        await waitFor(() => expect(activePanel.value).toBe('needs'));
    });
});

describe('the keyboard on a card', () => {
    it('keeps its focus on Refresh through a read, and Show now hands it to the card’s Refresh', async () => {
        const fake = nothingWaitsInChats(new FakeApi())
            .answer('campaign.get', 'campaign.get/card-status')
            .answer('campaign.list_contacts', 'campaign.list_contacts/card-people-first')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/card-status')
            .answer('campaign.activity', 'campaign.activity/card-status-30-days');
        renderConsole([spring([...turn({ kind: 'people_list' }), ...turn({ kind: 'agent_status' })])], fake);
        const pane = await openChat('Spring');
        await waitFor(() => expect(tiles(card(pane, 'Agent: Spring launch'))).toHaveLength(10));
        const refresh = within(card(pane, 'Agent: Spring launch')).getByRole('button', { name: 'Refresh' });
        refresh.focus();
        fireEvent.click(refresh);
        await waitFor(() => expect(reads(fake, ['campaign.activity'])).toHaveLength(2));
        await waitFor(() => expect(tiles(card(pane, 'Agent: Spring launch'))).toHaveLength(10));
        expect(document.activeElement).toBe(
            within(card(pane, 'Agent: Spring launch')).getByRole('button', { name: 'Refresh' }),
        );
        const show = within(card(pane, 'People')).getByRole('button', { name: 'Show now' });
        show.focus();
        fireEvent.click(show);
        await waitFor(() => within(card(pane, 'People')).getByText('12 people in this agent'));
        expect(document.activeElement).toBe(within(card(pane, 'People')).getByRole('button', { name: 'Refresh' }));
    });

    it('lets Refresh start a new read while one never answers, and draws only the new one', async () => {
        const fake = nothingWaitsInChats(new FakeApi()).answer('contact.list', 'contact.list/card-people-query');
        const release = fake.hold('contact.list');
        renderConsole([setUp(turn({ kind: 'people_list', search: 'ines' }))], fake);
        const pane = await openChat('Looking');
        await waitFor(() => within(card(pane, 'People')).getByText('Reading...'));
        release();
        await waitFor(() => within(card(pane, 'People')).getByText('Ines Duarte'));
        fake.hold('contact.list');
        fireEvent.click(within(card(pane, 'People')).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => within(card(pane, 'People')).getByText('Reading...'));
        // The read under way never answers; Refresh is still there to press, and its read is the one drawn.
        const again = within(card(pane, 'People')).getByRole('button', { name: 'Refresh' });
        expect(again.hasAttribute('disabled')).toBe(false);
        const third = fake.hold('contact.list');
        fireEvent.click(again);
        third();
        await waitFor(() => within(card(pane, 'People')).getByText('Ines Duarte'));
        expect(reads(fake, ['contact.list'])).toHaveLength(3);
    });
});
