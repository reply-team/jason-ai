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
