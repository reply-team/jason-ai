import { describe, expect, it } from 'vitest';
import type { ApprovalSummary, DecisionSummary } from '../api/types';
import {
    agentsOf,
    countOf,
    countWords,
    moreUnder,
    filtersOf,
    needsCount,
    pick,
    waitingOf,
    type ChatWait,
    type NeedsInput,
} from './needs-model';

const approval = (id: string, campaign: string, operation = 'message.send'): ApprovalSummary => ({
    id,
    work_item_id: `wi_${id}`,
    campaign_id: campaign,
    operation,
    status: 'pending',
    reason: 'approval_required',
    plugin_id: 'reply',
    requested_at: '2026-11-15T10:00:00.000Z',
    decided_at: null,
    decided_by: null,
    campaign_name: `Agent ${campaign}`,
    contact_name: 'Ines Duarte',
});

const question = (id: string, campaign: string, thread: DecisionSummary['thread'] = null): DecisionSummary => ({
    id,
    campaign_id: campaign,
    work_item_id: `wi_${id}`,
    status: 'pending',
    question: 'Which of the two?',
    raised_at: '2026-11-15T10:00:00.000Z',
    answered_at: null,
    answered_by: null,
    campaign_name: `Agent ${campaign}`,
    contact_name: null,
    thread,
});

/** A question the runtime's own responder raised about a reply: the runtime names the thread. */
const aboutAReply = (id: string, campaign: string): DecisionSummary =>
    question(id, campaign, { plugin_id: 'reply', thread_id: 't_501' });

const chat = (id: string, campaign: string | null): ChatWait => ({
    conversationId: id,
    campaignId: campaign,
    agent: campaign === null ? null : `Agent ${campaign}`,
    title: 'A chat',
    question: 'Shall I go on?',
    options: ['Yes', 'No'],
    at: '2026-11-15T10:00:00.000Z',
});

const input = (over: Partial<NeedsInput> = {}): NeedsInput => ({
    approvals: [approval('apr_1', 'cmp_1'), approval('apr_2', 'cmp_2', 'conversation.reply')],
    questions: [question('dec_1', 'cmp_1')],
    chats: null,
    more: { approvals: false, questions: false },
    ...over,
});

describe('what Needs you lists', () => {
    it('lists the approvals, then the questions, then the chats, in the order each list holds them', () => {
        const all = waitingOf(input({ chats: [chat('conv_1', 'cmp_2')] }), 'all', null);
        expect(all.map((each) => `${each.kind}:${each.id}`)).toEqual([
            'approval:apr_1',
            'approval:apr_2',
            'question:dec_1',
            'chat:conv_1',
        ]);
    });

    it('holds under Inbox the approvals that answer a reply and the questions about a thread, by what the runtime listed', () => {
        const about = input({ questions: [question('dec_1', 'cmp_1'), aboutAReply('dec_2', 'cmp_2')] });
        expect(waitingOf(about, 'inbox', null).map((each) => `${each.kind}:${each.id}`)).toEqual([
            'approval:apr_2',
            'question:dec_2',
        ]);
        expect(waitingOf(about, 'inbox', 'cmp_1')).toEqual([]);
        expect(waitingOf(about, 'questions', null).map((each) => each.id)).toEqual(['dec_1', 'dec_2']);
        expect(countOf(about, 'inbox')).toEqual({ n: 2, more: false });
    });

    it('says Inbox may hold more while either list has another page, since it holds rows of both', () => {
        expect(countOf(input({ more: { approvals: false, questions: true } }), 'inbox')).toEqual({ n: 1, more: true });
        expect(countOf(input({ more: { approvals: true, questions: false } }), 'inbox')).toEqual({ n: 1, more: true });
        expect(moreUnder(input({ more: { approvals: false, questions: true } }), 'inbox')).toBe(true);
        expect(moreUnder(input({ more: { approvals: false, questions: true } }), 'approvals')).toBe(false);
        expect(moreUnder(input({ more: { approvals: true, questions: true } }), 'chats')).toBe(false);
    });

    it('counts under Inbox only the approvals that answer a reply, by the operation the runtime listed', () => {
        expect(waitingOf(input(), 'inbox', null).map((each) => each.id)).toEqual(['apr_2']);
        expect(waitingOf(input(), 'approvals', null).map((each) => each.id)).toEqual(['apr_1', 'apr_2']);
        expect(waitingOf(input(), 'questions', null).map((each) => each.id)).toEqual(['dec_1']);
    });

    it('narrows every filter to one agent, and keeps a chat of no agent under all agents alone', () => {
        const some = input({ chats: [chat('conv_1', null), chat('conv_2', 'cmp_1')] });
        expect(waitingOf(some, 'all', 'cmp_1').map((each) => each.id)).toEqual(['apr_1', 'dec_1', 'conv_2']);
        expect(waitingOf(some, 'inbox', 'cmp_1')).toEqual([]);
        expect(waitingOf(some, 'chats', null).map((each) => each.id)).toEqual(['conv_1', 'conv_2']);
    });

    it('names each agent that has something waiting once, in the order they first appear', () => {
        const some = input({ chats: [chat('conv_1', 'cmp_3'), chat('conv_2', null)] });
        expect(agentsOf(some)).toEqual([
            { id: 'cmp_1', name: 'Agent cmp_1' },
            { id: 'cmp_2', name: 'Agent cmp_2' },
            { id: 'cmp_3', name: 'Agent cmp_3' },
        ]);
    });
});

describe('the count', () => {
    it('is the number All holds: approvals, questions and waiting chats, each once', () => {
        expect(needsCount(input())).toEqual({ n: 3, more: false });
        expect(needsCount(input({ chats: [chat('conv_1', 'cmp_1')] }))).toEqual({ n: 4, more: false });
        expect(needsCount(input(), 'cmp_1')).toEqual({ n: 2, more: false });
    });

    it('says when a list has another page, and the words carry a plus', () => {
        const count = needsCount(input({ more: { approvals: false, questions: true } }));
        expect(count).toEqual({ n: 3, more: true });
        expect(countWords(count)).toBe('3+');
        expect(countWords({ n: 2, more: false })).toBe('2');
    });

    it('draws a filter for the chats only once they have been read, and counts each filter under the agent chosen', () => {
        expect(filtersOf(input(), null).map(([filter, , count]) => `${filter} ${countWords(count)}`)).toEqual([
            'all 3',
            'approvals 2',
            'questions 1',
            'inbox 1',
        ]);
        expect(
            filtersOf(input({ chats: [], more: { approvals: true, questions: false } }), 'cmp_2').map(
                ([filter, label, count]) => [filter, label, countWords(count)],
            ),
        ).toEqual([
            ['all', 'All', '1+'],
            ['approvals', 'Approvals', '1+'],
            ['questions', 'Questions', '0'],
            ['chats', 'Chats', '0'],
            ['inbox', 'Inbox', '1+'],
        ]);
    });
});

describe('the item the pane shows', () => {
    const items = waitingOf(input(), 'all', null);

    it('is the one chosen while it is listed, else the first', () => {
        expect(pick(items, { kind: 'question', id: 'dec_1' })?.id).toBe('dec_1');
        expect(pick(items, null)?.id).toBe('apr_1');
        expect(pick(items, { kind: 'approval', id: 'apr_9' })?.id).toBe('apr_1');
        expect(pick([], null)).toBeNull();
    });

    it('is told apart by its kind, so an id of another kind is not taken for it', () => {
        expect(pick(items, { kind: 'chat', id: 'apr_2' })?.id).toBe('apr_1');
    });
});
