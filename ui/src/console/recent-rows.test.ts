import { describe, expect, it } from 'vitest';
import type { ApprovalSummary, CampaignSummary, DecisionSummary } from '../api/types';
import type { DesktopConversation } from '../platform/desktop';
import { ageOf } from './live';
import type { ChatWait } from './needs-model';
import { agentRows, chatRows, inFilter, pageSize, recentRows, shownRows, type RecentRow } from './recent-rows';

const now = Date.parse('2026-10-16T09:00:00.000Z');
const before = (ms: number) => new Date(now - ms).toISOString();

const conversation = (fields: Partial<DesktopConversation> & { id: string }): DesktopConversation => ({
    campaignId: null,
    title: 'A chat',
    updatedAt: before(0),
    running: false,
    mode: 'ask_first',
    ...fields,
});

const agents: CampaignSummary[] = [
    { id: 'cmp_a', name: 'Alpha', status: 'active' },
    { id: 'cmp_p', name: 'Pending', status: 'paused' },
    { id: 'cmp_d', name: 'Drafty', status: 'draft' },
];

const approval = (campaign: string) => ({ campaign_id: campaign }) as ApprovalSummary;
const question = (campaign: string) => ({ campaign_id: campaign }) as DecisionSummary;

const row = (fields: Partial<RecentRow>): RecentRow => ({
    id: 'conv_1',
    campaignId: null,
    conversationId: 'conv_1',
    title: 'A chat',
    agent: null,
    aboutAgent: false,
    waiting: 0,
    waitingWords: '0',
    state: null,
    running: false,
    dot: 'none',
    age: 'now',
    ...fields,
});

describe('the age of a row', () => {
    it('is written now, N min, N h and N d at the four boundaries', () => {
        expect(ageOf(before(59_000), now)).toBe('now');
        expect(ageOf(before(60_000), now)).toBe('1 min');
        expect(ageOf(before(59 * 60_000), now)).toBe('59 min');
        expect(ageOf(before(60 * 60_000), now)).toBe('1 h');
        expect(ageOf(before(23 * 3_600_000 + 59 * 60_000), now)).toBe('23 h');
        expect(ageOf(before(24 * 3_600_000), now)).toBe('1 d');
        expect(ageOf(before(3 * 24 * 3_600_000), now)).toBe('3 d');
    });
});

describe('the rows under Recent', () => {
    const rows = recentRows({
        conversations: [
            conversation({ id: 'conv_feed', main: true, title: 'Feed' }),
            conversation({ id: 'conv_a', campaignId: 'cmp_a', title: 'Alpha', updatedAt: before(5 * 60_000) }),
            conversation({ id: 'conv_p', campaignId: 'cmp_p', title: 'Pending' }),
            conversation({ id: 'conv_d', campaignId: 'cmp_d', title: 'Drafty' }),
            conversation({ id: 'conv_x', campaignId: 'cmp_gone', title: 'Archived one' }),
            conversation({ id: 'conv_n', title: 'Set-up chat' }),
        ],
        titles: (id) => (id === 'conv_n' ? 'Renamed set-up' : undefined),
        campaigns: agents,
        approvals: [approval('cmp_a'), approval('cmp_a'), approval('cmp_d')],
        questions: [question('cmp_a')],
        now,
    });

    it('leaves the Feed out and counts approvals plus questions per agent', () => {
        expect(rows.map((each) => each.id)).toEqual(['conv_a', 'conv_p', 'conv_d', 'conv_x', 'conv_n']);
        expect(rows.map((each) => each.waiting)).toEqual([3, 0, 1, 0, 0]);
    });

    it('counts a chat that waits on a choice with its agent’s, and a chat of no agent by its own, once read', () => {
        const wait = (conversationId: string, campaignId: string | null): ChatWait => ({
            conversationId,
            campaignId,
            agent: null,
            title: 'A chat',
            question: 'Shall I go on?',
            options: ['Yes'],
            at: before(0),
        });
        const read = recentRows({
            conversations: [
                conversation({ id: 'conv_a', campaignId: 'cmp_a', title: 'Alpha' }),
                conversation({ id: 'conv_p', campaignId: 'cmp_p', title: 'Pending' }),
                conversation({ id: 'conv_n', title: 'Set-up chat' }),
                conversation({ id: 'conv_m', title: 'Another set-up chat' }),
            ],
            titles: () => undefined,
            campaigns: agents,
            approvals: [approval('cmp_a')],
            questions: [],
            chats: [wait('conv_a', 'cmp_a'), wait('conv_n', null)],
            now,
        });
        expect(read.map((each) => each.waiting)).toEqual([2, 0, 1, 0]);
        expect(read.map((each) => each.dot)).toEqual(['waiting', 'paused', 'waiting', 'none']);
    });

    it('carries the plus Needs you’s count carries while the runtime holds another page', () => {
        const paged = recentRows({
            conversations: [
                conversation({ id: 'conv_a', campaignId: 'cmp_a', title: 'Alpha' }),
                conversation({ id: 'conv_p', campaignId: 'cmp_p', title: 'Pending' }),
            ],
            titles: () => undefined,
            campaigns: agents,
            approvals: [approval('cmp_a'), approval('cmp_a')],
            questions: [question('cmp_a')],
            more: { approvals: true, questions: false },
            now,
        });
        expect(paged.map((each) => each.waitingWords)).toEqual(['3+', '0+']);
        expect(paged.map((each) => each.waiting)).toEqual([3, 0]);
        expect(rows.map((each) => each.waitingWords)).toEqual(['3', '0', '1', '0', '0']);
    });

    it('draws amber when something waits, else the agent’s state, else nothing', () => {
        expect(rows.map((each) => each.dot)).toEqual(['waiting', 'paused', 'waiting', 'none', 'none']);
        expect(rows[0]!.state).toBe('active');
        expect(rows[2]!.state).toBe('draft');
    });

    it('says the title as the console holds it, and the age of the conversation’s last change', () => {
        expect(rows[4]!.title).toBe('Renamed set-up');
        expect(rows[0]!.age).toBe('5 min');
    });

    it('says a turn runs now from the chat the console holds, else from the desktop’s list', () => {
        const listed = [conversation({ id: 'c1', running: true }), conversation({ id: 'c2' })];
        const input = {
            conversations: listed,
            titles: () => undefined,
            campaigns: [],
            approvals: [],
            questions: [],
            now,
        };
        expect(recentRows(input).map((each) => each.running)).toEqual([true, false]);
        expect(
            recentRows({ ...input, running: (id) => (id === 'c2' ? true : false) }).map((each) => each.running),
        ).toEqual([false, true]);
    });

    it('knows a chat about no campaign from one whose campaign the page does not hold', () => {
        expect(rows[4]!.aboutAgent).toBe(false);
        expect(rows[3]!.aboutAgent).toBe(true);
        expect(rows[3]!.agent).toBeNull();
        expect(rows[3]!.dot).toBe('none');
    });

    it('puts a chat with no agent under All alone', () => {
        const chat = rows[4]!;
        expect(['all', 'needs', 'live', 'paused', 'draft'].filter((each) => inFilter(chat, each as never))).toEqual([
            'all',
        ]);
        const archived = rows[3]!;
        expect(inFilter(archived, 'live') || inFilter(archived, 'paused') || inFilter(archived, 'draft')).toBe(false);
    });

    it('holds each state under its own entry, a waiting agent under Needs you as well as its state', () => {
        expect(rows.filter((each) => inFilter(each, 'needs')).map((each) => each.id)).toEqual(['conv_a', 'conv_d']);
        expect(rows.filter((each) => inFilter(each, 'live')).map((each) => each.id)).toEqual(['conv_a']);
        expect(rows.filter((each) => inFilter(each, 'paused')).map((each) => each.id)).toEqual(['conv_p']);
        expect(rows.filter((each) => inFilter(each, 'draft')).map((each) => each.id)).toEqual(['conv_d']);
    });
});

describe('an agent the list of agents does not hold', () => {
    const read: Record<string, CampaignSummary> = {
        cmp_w: { id: 'cmp_w', name: 'Winter', status: 'archived' },
        cmp_l: { id: 'cmp_l', name: 'Late', status: 'paused' },
    };
    const input = {
        conversations: [
            conversation({ id: 'conv_w', campaignId: 'cmp_w', title: 'Winter' }),
            conversation({ id: 'conv_l', campaignId: 'cmp_l', title: 'Late' }),
            conversation({ id: 'conv_u', campaignId: 'cmp_unread', title: 'Unread' }),
        ],
        titles: () => undefined,
        campaigns: agents,
        approvals: [approval('cmp_l')],
        questions: [],
        now,
    };

    it('leaves out a conversation whose agent is archived, once that agent is read on its own', () => {
        expect(recentRows({ ...input, agentRead: (id) => read[id] }).map((each) => each.id)).toEqual([
            'conv_l',
            'conv_u',
        ]);
    });

    it('gives a conversation of an agent read on its own that agent’s name, state and what waits for it', () => {
        const late = recentRows({ ...input, agentRead: (id) => read[id] })[0]!;
        expect([late.agent, late.state, late.waiting, late.dot]).toEqual(['Late', 'paused', 1, 'waiting']);
    });

    it('keeps a conversation whose agent is not read yet, as one whose agent is not held', () => {
        const rows = recentRows(input);
        expect(rows.map((each) => each.id)).toEqual(['conv_w', 'conv_l', 'conv_u']);
        expect(rows.map((each) => [each.agent, each.aboutAgent, each.dot])).toEqual([
            [null, true, 'none'],
            [null, true, 'none'],
            [null, true, 'none'],
        ]);
    });
});

describe('the two sections', () => {
    const input = {
        conversations: [
            conversation({ id: 'conv_feed', main: true, title: 'Feed' }),
            conversation({ id: 'conv_n', title: 'Set-up chat' }),
            conversation({ id: 'conv_a', campaignId: 'cmp_a', title: 'Alpha' }),
            conversation({ id: 'conv_a2', campaignId: 'cmp_a', title: 'Alpha, an older one' }),
            conversation({ id: 'conv_h', campaignId: 'cmp_handed', title: 'Handed over' }),
            conversation({ id: 'conv_m', title: 'Mailboxes' }),
        ],
        titles: () => undefined,
        campaigns: agents,
        approvals: [approval('cmp_p')],
        questions: [],
        now,
    };

    it('puts each agent in AGENTS once, by its conversation newest first, then the agents with none, in the order of the list of agents', () => {
        const rows = agentRows(input);
        expect(rows.map((each) => each.id)).toEqual(['conv_a', 'conv_h', 'cmp_p', 'cmp_d']);
        expect(rows.map((each) => each.conversationId)).toEqual(['conv_a', 'conv_h', null, null]);
        expect(rows.map((each) => each.title)).toEqual(['Alpha', 'Handed over', 'Pending', 'Drafty']);
    });

    it('names each agent by its campaign, whatever title its conversation carries, and an agent not read by that title', () => {
        const rows = agentRows({
            ...input,
            conversations: [
                conversation({ id: 'conv_q', campaignId: 'cmp_a', title: 'Q4 push' }),
                ...input.conversations,
            ],
        });
        expect(rows[0]).toMatchObject({ id: 'conv_q', title: 'Alpha' });
        expect(rows.find((each) => each.id === 'conv_h')?.title).toBe('Handed over');
    });

    it('gives an agent with no conversation its state, what waits for it and no age', () => {
        const pending = agentRows(input).find((each) => each.id === 'cmp_p')!;
        expect([pending.campaignId, pending.state, pending.waiting, pending.dot, pending.age]).toEqual([
            'cmp_p',
            'paused',
            1,
            'waiting',
            '',
        ]);
    });

    it('puts in CHATS the conversations about no agent alone, newest first, and no conversation in both', () => {
        const chats = chatRows(input);
        expect(chats.map((each) => each.id)).toEqual(['conv_n', 'conv_m']);
        const agentKeys = new Set(agentRows(input).map((each) => each.conversationId));
        expect(chats.some((each) => agentKeys.has(each.id))).toBe(false);
    });

    it('leaves an archived agent out of AGENTS, with or without a conversation', () => {
        const archived = [...agents, { id: 'cmp_old', name: 'Old', status: 'archived' as const }];
        const rows = agentRows({
            ...input,
            campaigns: archived,
            conversations: [...input.conversations, conversation({ id: 'conv_o', campaignId: 'cmp_old' })],
        });
        expect(rows.some((each) => each.campaignId === 'cmp_old')).toBe(false);
    });
});

describe('paging', () => {
    const many = Array.from({ length: 25 }, (_, at) => row({ id: `conv_${at}`, title: `Chat ${at}` }));

    it('shows ten, then ten more per limit, and says whether more remain', () => {
        expect(shownRows(many, 'all', pageSize)).toMatchObject({ more: true });
        expect(shownRows(many, 'all', pageSize).rows).toHaveLength(10);
        expect(shownRows(many, 'all', 20).rows).toHaveLength(20);
        expect(shownRows(many, 'all', 30)).toMatchObject({ more: false });
        expect(shownRows(many, 'all', 30).rows).toHaveLength(25);
    });

    it('shows every match while a filter is active', () => {
        const live = many.map((each) => ({ ...each, state: 'active' as const }));
        expect(shownRows(live, 'live', pageSize)).toMatchObject({ more: false });
        expect(shownRows(live, 'live', pageSize).rows).toHaveLength(25);
    });
});
