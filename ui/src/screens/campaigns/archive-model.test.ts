import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import type { ApprovalSummary, DecisionSummary, WorkItemSummary } from '../../api/types';
import { countCancels, jasonMayPauseFirst, nothingOpen, runsPastArchive, saidArchived } from './archive-model';

const contracts = join(import.meta.dirname, '..', '..', '..', '..', 'docs', 'contracts', 'operations');

const item = (id: string, status: string, operation: string | null = 'message.send') =>
    ({ id, status, operation }) as WorkItemSummary;
const approval = (work_item_id: string, status: string) => ({ work_item_id, status }) as ApprovalSummary;
const question = { id: 'dec_1' } as DecisionSummary;

describe('what an archive cancels', () => {
    it('leaves to finish exactly the operations whose contract says they run in an archived campaign', () => {
        const runs = readdirSync(contracts)
            .filter((name) => name.endsWith('.json'))
            .map(
                (name) =>
                    JSON.parse(readFileSync(join(contracts, name), 'utf8')) as {
                        id: string;
                        dispatch?: { in_archived_campaign?: string };
                    },
            )
            .filter((contract) => contract.dispatch?.in_archived_campaign === 'runs')
            .map((contract) => contract.id)
            .sort();
        expect(runs.length).toBeGreaterThan(0);
        expect([...runsPastArchive].sort()).toEqual(runs);
    });

    it('counts the open work it cancels apart from a search’s stop, and only the approvals on that work', () => {
        const counted = countCancels(
            [
                item('wi_1', 'awaiting_approval'),
                item('wi_2', 'created'),
                item('wi_3', 'processing', null),
                item('wi_4', 'created', 'audience.search_cancel'),
            ],
            [
                approval('wi_5', 'approved'),
                approval('wi_1', 'pending'),
                approval('wi_2', 'approved'),
                approval('wi_4', 'approved'),
            ],
            [question],
        );
        expect(counted).toEqual({ work: 3, processing: 1, asked: 1, given: 1, questions: 1, finishing: 1 });
        expect(nothingOpen(counted)).toBe(false);
    });

    it('says nothing is open only when there is no work, no approval and no question at all', () => {
        expect(nothingOpen(countCancels([], [], []))).toBe(true);
        expect(nothingOpen(countCancels([item('wi_1', 'created', 'audience.search_cancel')], [], []))).toBe(false);
    });
});

describe('the way to stop the provider’s campaign first', () => {
    it('is Jason’s pause only for a live or paused agent whose route for it is usable', () => {
        expect(jasonMayPauseFirst('active', true)).toBe(true);
        expect(jasonMayPauseFirst('paused', true)).toBe(true);
        // A pinned draft's work waits, so it pauses nothing first; an unusable route and an unread one are no way either.
        expect(jasonMayPauseFirst('draft', true)).toBe(false);
        expect(jasonMayPauseFirst('active', false)).toBe(false);
        expect(jasonMayPauseFirst('active', null)).toBe(false);
    });
});

describe('the page’s word after the archive', () => {
    const none = { work: 0, processing: 0, asked: 0, given: 0, questions: 0, finishing: 0 };
    it('names only what was cancelled', () => {
        expect(
            saidArchived('Spring launch', { ...none, work: 3, processing: 1, asked: 1, given: 1, questions: 1 }),
        ).toBe('Spring launch is archived: 3 open work items, 2 approvals and 1 question are cancelled.');
        expect(saidArchived('Spring launch', { ...none, questions: 1 })).toBe(
            'Spring launch is archived: 1 question is cancelled.',
        );
    });

    it('says the archive alone where nothing was open, only a search’s stop was, or the counts were not read', () => {
        expect(saidArchived('Spring launch', none)).toBe('Spring launch is archived.');
        expect(saidArchived('Spring launch', { ...none, finishing: 1 })).toBe('Spring launch is archived.');
        expect(saidArchived('Spring launch', null)).toBe('Spring launch is archived.');
    });
});
