import { describe, expect, it } from 'vitest';
import type { ApprovalSummary, CampaignSummary, DecisionSummary } from '../api/types';
import type { DesktopConversation } from '../platform/desktop';
import { ageOf } from './live';
import { inFilter, matches, pageSize, recentRows, shownRows, type RecentRow } from './recent-rows';

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
    title: 'A chat',
    agent: null,
    waiting: 0,
    state: null,
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

    it('draws amber when something waits, else the agent’s state, else nothing', () => {
        expect(rows.map((each) => each.dot)).toEqual(['waiting', 'paused', 'waiting', 'none', 'none']);
        expect(rows[0]!.state).toBe('active');
        expect(rows[2]!.state).toBe('draft');
    });

    it('says the title as the console holds it, and the age of the conversation’s last change', () => {
        expect(rows[4]!.title).toBe('Renamed set-up');
        expect(rows[0]!.age).toBe('5 min');
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

describe('searching and paging', () => {
    it('matches the title or the agent’s name, whatever the case, and every row for no text', () => {
        const renamed = row({ title: 'Q4 push', agent: 'Spring' });
        expect(matches(renamed, 'q4')).toBe(true);
        expect(matches(renamed, ' SPRING ')).toBe(true);
        expect(matches(renamed, 'autumn')).toBe(false);
        expect(matches(row({ title: 'Plain' }), '')).toBe(true);
        expect(matches(row({ title: 'Plain' }), 'spring')).toBe(false);
    });

    const many = Array.from({ length: 25 }, (_, at) => row({ id: `conv_${at}`, title: `Chat ${at}` }));

    it('shows ten, then ten more per limit, and says whether more remain', () => {
        expect(shownRows(many, 'all', '', pageSize)).toMatchObject({ more: true });
        expect(shownRows(many, 'all', '', pageSize).rows).toHaveLength(10);
        expect(shownRows(many, 'all', '', 20).rows).toHaveLength(20);
        expect(shownRows(many, 'all', '', 30)).toMatchObject({ more: false });
        expect(shownRows(many, 'all', '', 30).rows).toHaveLength(25);
    });

    it('shows every match while a search text or a filter is active', () => {
        expect(shownRows(many, 'all', 'chat', pageSize)).toMatchObject({ more: false });
        expect(shownRows(many, 'all', 'chat', pageSize).rows).toHaveLength(25);
        const live = many.map((each) => ({ ...each, state: 'active' as const }));
        expect(shownRows(live, 'live', '', pageSize).rows).toHaveLength(25);
    });
});
