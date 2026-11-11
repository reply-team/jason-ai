import { describe, expect, it } from 'vitest';
import type { Contact, ContactCampaign, JournalEntry } from '../../api/types';
import { leading, others, parseStage, stageWords, standingOf } from './standing';

function membership(id: string, added: string, standing: string[], state = 'enrolled'): ContactCampaign {
    return {
        campaign_id: id,
        campaign_name: `Campaign ${id}`,
        state,
        added_at: added,
        updated_at: added,
        standing,
    };
}

function person(campaigns: ContactCampaign[] | null, lastIn: string | null = null): Contact {
    return {
        id: 'cnt_1',
        first_name: 'Ada',
        last_name: 'Example',
        company: null,
        title: null,
        time_zone: null,
        channels: [],
        custom: {},
        external_ids: [],
        created_at: '2026-10-01T10:00:00.000Z',
        campaigns,
        last_activity: lastIn
            ? ({ id: 'jrn_1', ts: '2026-10-15T16:00:00.000Z', campaign_id: lastIn } as JournalEntry)
            : null,
    };
}

describe('the campaign a row shows', () => {
    const old = membership('cmp_1', '2026-10-01T10:00:00.000Z', ['new']);
    const recent = membership('cmp_2', '2026-10-12T10:00:00.000Z', ['waiting']);
    const gone = membership('cmp_3', '2026-10-14T10:00:00.000Z', ['removed'], 'excluded');

    it('is the campaign of their latest activity when they are a member there and not excluded', () => {
        expect(leading(person([old, recent], 'cmp_1'))?.campaign_id).toBe('cmp_1');
    });

    it('is their newest membership not excluded when that campaign is not theirs, or there is no activity', () => {
        expect(leading(person([old, recent, gone], 'cmp_3'))?.campaign_id).toBe('cmp_2');
        expect(leading(person([old, recent, gone], 'cmp_9'))?.campaign_id).toBe('cmp_2');
        expect(leading(person([old, recent, gone]))?.campaign_id).toBe('cmp_2');
    });

    it('is the newest membership when every one is excluded', () => {
        const other = membership('cmp_4', '2026-10-02T10:00:00.000Z', ['removed'], 'excluded');
        expect(leading(person([other, gone]))?.campaign_id).toBe('cmp_3');
    });

    it('is nothing for a person in no campaign', () => {
        expect(leading(person([]))).toBeNull();
        expect(leading(person(null))).toBeNull();
    });

    it('under a stage is the first by the same rule whose standing holds it', () => {
        const both = person([old, recent], 'cmp_1');
        expect(leading(both, 'waiting')?.campaign_id).toBe('cmp_2');
        expect(leading(both, 'new')?.campaign_id).toBe('cmp_1');
        expect(leading(both, 'failed')).toBeNull();
    });

    it('is the campaign the list is narrowed to, whatever their latest activity was in', () => {
        const both = person([old, recent], 'cmp_1');
        expect(leading(both, undefined, 'cmp_2')?.campaign_id).toBe('cmp_2');
        expect(leading(both, 'new', 'cmp_2')?.campaign_id).toBe('cmp_1');
        expect(leading(both, undefined, 'cmp_9')?.campaign_id).toBe('cmp_1');
    });

    it('names the others, in the same order', () => {
        const all = person([old, recent, gone], 'cmp_1');
        expect(others(all, leading(all)).map((c) => c.campaign_id)).toEqual(['cmp_2', 'cmp_3']);
        expect(others(person([]), null)).toEqual([]);
    });
});

describe('the stage words', () => {
    it('is one word for one count and each word for two, with the runtime definition as the hint', () => {
        expect(stageWords(['waiting']).map((w) => w.label)).toEqual(['Waiting']);
        expect(stageWords(['waiting', 'working']).map((w) => w.label)).toEqual(['Waiting', 'Working']);
        expect(stageWords(['waiting'])[0]?.hint).toBe('An approval or a question about their work is waiting for you.');
    });

    it('is empty when no count counts them, and names Removed for an excluded membership', () => {
        expect(stageWords([])).toEqual([]);
        expect(stageWords(['removed']).map((w) => w.label)).toEqual(['Removed']);
    });

    it('ignores a count this build has no word for', () => {
        expect(stageWords(['waiting', 'paused'])).toHaveLength(1);
    });
});

describe('a runtime that does not say where people stand', () => {
    it('reads an absent standing as none, and never throws', () => {
        const bare = membership('cmp_1', '2026-10-01T10:00:00.000Z', []) as Partial<ContactCampaign>;
        delete bare.standing;
        const shown = person([bare as ContactCampaign]);
        expect(leading(shown)?.campaign_id).toBe('cmp_1');
        expect(leading(shown, 'waiting')).toBeNull();
        expect(stageWords(standingOf(bare as ContactCampaign))).toEqual([]);
    });
});

describe('the stage in an address', () => {
    it('is one of the seven counts the page offers, and nothing else', () => {
        expect(parseStage('waiting')).toBe('waiting');
        expect(parseStage('removed')).toBeUndefined();
        expect(parseStage('people')).toBeUndefined();
        expect(parseStage(null)).toBeUndefined();
    });
});
