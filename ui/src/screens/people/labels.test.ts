import { describe, expect, it } from 'vitest';
import type { JournalEntry } from '../../api/types';
import { entryLabel } from './labels';

function entry(kind: string, actor: JournalEntry['actor']): JournalEntry {
    return {
        id: 'jrn_01K0LABEL',
        ts: '2026-10-01T12:00:00Z',
        actor,
        kind,
        key: null,
        old: null,
        new: null,
        reason: null,
        campaign_id: null,
        work_item_id: null,
        contact_id: null,
        campaign_name: null,
        contact_name: null,
        work_item_kind: null,
        work_item_role: null,
        work_item_operation: null,
        actor_role: null,
    };
}

describe('entryLabel', () => {
    it('names a plugin that acted by its id, never as Jason', () => {
        expect(entryLabel(entry('campaign_paused', { type: 'plugin', id: 'forwarder' }), 'me').title).toBe(
            'forwarder paused it',
        );
        expect(entryLabel(entry('contact_created', { type: 'plugin', id: 'forwarder' }), 'me').short).toBe(
            'created by forwarder',
        );
        expect(entryLabel(entry('contact_created', { type: 'plugin', id: null }), 'me').short).toBe(
            'created by a plugin',
        );
    });

    it('keeps the runtime as Jason', () => {
        expect(entryLabel(entry('campaign_paused', { type: 'system', id: null }), 'me').title).toBe('Jason paused it');
    });
});

describe('a message the account sent', () => {
    it('is said as a send, with a mark of its own and never the reply mark, and claims no author or step', () => {
        const sent = entryLabel(entry('provider_send_observed', { type: 'system', id: 'dispatcher' }), 'me');
        expect(sent.title).toBe('Sent by the provider');
        expect(sent.short).toBe('sent by the provider');
        expect(sent.mark.glyph).toBe('↗');
        expect(sent.mark.glyph).not.toBe('↩');
        expect(sent.mark.kind).not.toBe('reply');
        expect(sent.note).toBeNull();
    });

    it('says the channel beside it only when the line names one the person knows', () => {
        const line = entry('provider_send_observed', { type: 'system', id: 'dispatcher' });
        expect(entryLabel({ ...line, new: { channel: 'email' } }, 'me').note).toBe('by email');
        expect(entryLabel({ ...line, new: { channel: 'other' } }, 'me').note).toBeNull();
    });
});

describe('the line a work item is put back under', () => {
    it('does not promise a retry, since an approval, a retry and a withdrawn ask after an edit all write it', () => {
        for (const actor of [
            { type: 'system', id: null },
            { type: 'human', id: 'ada' },
        ] as const) {
            const label = entryLabel(entry('workitem_released', actor), 'me');
            expect(label.title).toMatch(/was put back to be taken again$/);
            expect(label.short).toMatch(/ put back$/);
            expect(`${label.title} ${label.short}`).not.toMatch(/tried again|try again/);
        }
    });
});
