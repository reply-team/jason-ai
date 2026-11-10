import { describe, expect, it } from 'vitest';
import type { JournalEntry } from '../../api/types';
import { inboxEntriesOf, inboxThreadReads } from './inbox-lines';

let n = 0;
function line(contact: string | null, plugin: unknown, channel = 'email', name: string | null = null): JournalEntry {
    n += 1;
    const ts = `2026-10-16T08:${String(59 - n).padStart(2, '0')}:00.000Z`;
    return {
        id: `jrn_${n}`,
        ts,
        actor: { type: 'system', id: 'dispatcher' },
        kind: 'inbound_reply_observed',
        key: null,
        old: null,
        new: { plugin, thread: `t_${n}`, channel, last_activity_at: ts, contact_id: contact },
        reason: null,
        campaign_id: 'cmp_1',
        work_item_id: null,
        contact_id: contact,
        campaign_name: 'Pilot',
        contact_name: name,
        work_item_kind: null,
        work_item_role: null,
        work_item_operation: null,
        actor_role: null,
    };
}

describe('inboxEntriesOf', () => {
    it('keeps each person and plugin once, newest first, with the plugin the line names', () => {
        const lines = [
            line('cnt_1', 'reply', 'email', 'Paula'),
            line('cnt_2', 'reply', 'email', 'Mateo'),
            line('cnt_1', 'reply', 'email', 'Paula'),
            line('cnt_1', 'other', 'email', 'Paula'),
        ];
        expect(inboxEntriesOf(lines).entries).toEqual([
            { kind: 'person', contactId: 'cnt_1', name: 'Paula', pluginId: 'reply', at: lines[0]!.ts },
            { kind: 'person', contactId: 'cnt_2', name: 'Mateo', pluginId: 'reply', at: lines[1]!.ts },
            { kind: 'person', contactId: 'cnt_1', name: 'Paula', pluginId: 'other', at: lines[3]!.ts },
        ]);
    });

    it('draws a line tied to nobody as its channel and moment, and reads nothing for it', () => {
        const lines = [line(null, 'reply', 'linkedin'), line('cnt_2', 'reply')];
        expect(inboxEntriesOf(lines).entries).toEqual([
            { kind: 'untied', channel: 'linkedin', at: lines[0]!.ts },
            { kind: 'person', contactId: 'cnt_2', name: null, pluginId: 'reply', at: lines[1]!.ts },
        ]);
    });

    it('reads at most ten people, and says how many lines it left out', () => {
        const lines = Array.from({ length: 13 }, (_, i) => line(`cnt_${i}`, 'reply'));
        const { entries, left } = inboxEntriesOf(lines);
        expect(inboxThreadReads).toBe(10);
        expect(entries.map((e) => (e.kind === 'person' ? e.contactId : null))).toEqual(
            Array.from({ length: 10 }, (_, i) => `cnt_${i}`),
        );
        expect(left).toBe(3);
    });

    it('a line that names no plugin is drawn without a read', () => {
        const lines = [line('cnt_1', undefined, 'email', 'Paula')];
        expect(inboxEntriesOf(lines).entries).toEqual([
            { kind: 'person', contactId: 'cnt_1', name: 'Paula', pluginId: null, at: lines[0]!.ts },
        ]);
    });
});
