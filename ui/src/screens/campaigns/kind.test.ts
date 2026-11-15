import { describe, expect, it } from 'vitest';
import type { ExternalId } from '../../api/types';
import { agentKind, asksForCreate, firstTabName, kindWord, unfinishedCreate } from './kind';

const pin = (plugin_id = 'reply', value = '4711'): ExternalId => ({ plugin_id, kind: 'campaign', value }) as ExternalId;
const other = { plugin_id: 'reply', kind: 'contact', value: '9' } as ExternalId;
const agent = (status: string, ids: ExternalId[] = []) => ({ status, external_ids: ids });

describe('the kind of an agent, from the facts the page has', () => {
    it('is a Sequence when a pin of kind campaign is held, in every status', () => {
        for (const status of ['draft', 'active', 'paused', 'archived']) {
            expect(agentKind(agent(status, [pin()]), false)).toBe('sequence');
        }
    });

    it('is a Sequence whatever is said of a campaign being made: a pin is a pin, a stale one included', () => {
        expect(agentKind(agent('active', [pin('gone-plugin', '1')]), true)).toBe('sequence');
        expect(agentKind(agent('active', [pin()]), null)).toBe('sequence');
    });

    it('is Adaptive when started, unpinned, and no campaign.create is unfinished', () => {
        expect(agentKind(agent('active'), false)).toBe('adaptive');
        expect(agentKind(agent('paused'), false)).toBe('adaptive');
        expect(agentKind(agent('active', [other]), false)).toBe('adaptive');
    });

    it('holds Adaptive back while a campaign.create is unfinished, and while that is not yet known', () => {
        expect(agentKind(agent('active'), true)).toBeNull();
        expect(agentKind(agent('paused'), true)).toBeNull();
        expect(agentKind(agent('active'), null)).toBeNull();
    });

    it('is nothing for a draft with no pin and for any status the two rules do not cover', () => {
        expect(agentKind(agent('draft'), false)).toBeNull();
        expect(agentKind(agent('draft'), null)).toBeNull();
        expect(agentKind(agent('archived'), false)).toBeNull();
        expect(agentKind(agent('something_new'), false)).toBeNull();
    });
});

describe('the words', () => {
    it('names the first tab Sequence where the label says Sequence and Paths otherwise', () => {
        expect(firstTabName('sequence')).toBe('Sequence');
        expect(firstTabName('adaptive')).toBe('Paths');
        expect(firstTabName(null)).toBe('Paths');
    });

    it('draws the two words of the label and nothing for none', () => {
        expect(kindWord('sequence')).toBe('Sequence');
        expect(kindWord('adaptive')).toBe('Adaptive');
        expect(kindWord(null)).toBeNull();
    });
});

describe('the one read', () => {
    it('is asked only of a started agent with no pin', () => {
        expect(asksForCreate(agent('active'))).toBe(true);
        expect(asksForCreate(agent('paused'))).toBe(true);
        expect(asksForCreate(agent('active', [pin()]))).toBe(false);
        expect(asksForCreate(agent('draft'))).toBe(false);
        expect(asksForCreate(agent('archived'))).toBe(false);
    });

    it('finds a campaign.create among the one page it gets, and no other operation', () => {
        expect(unfinishedCreate([{ operation: 'campaign.enroll' }, { operation: 'campaign.create' }])).toBe(true);
        expect(unfinishedCreate([{ operation: 'campaign.enroll' }, { operation: null }])).toBe(false);
        expect(unfinishedCreate([])).toBe(false);
    });
});
