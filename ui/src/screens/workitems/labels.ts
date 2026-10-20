import type { WorkItemSummary } from '../../api/types';
import { clock } from '../../time/format';
import { operationLabel } from '../attention/labels';

/** The status mark of a row, in the design system's marks. */
export function mark(status: string): { kind: string; glyph: string } {
    switch (status) {
        case 'created':
            return { kind: 'plan', glyph: '○' };
        case 'scheduled':
        case 'processing':
            return { kind: 'run', glyph: '▶' };
        case 'awaiting_approval':
            return { kind: 'wait', glyph: '⏸' };
        case 'succeeded':
            return { kind: 'ok', glyph: '✓' };
        case 'failed':
            return { kind: 'fail', glyph: '✗' };
        default:
            return { kind: 'off', glyph: status === 'expired' ? '◌' : '–' };
    }
}

/** What the item is: its role, or its operation in words. */
export function what(item: Pick<WorkItemSummary, 'role' | 'operation'>): string {
    return item.role ?? (item.operation ? operationLabel(item.operation) : 'work');
}

/** When planned work runs, in the runtime's own terms: a start it may not pass, a retry it waits for, or as soon as it can. */
export function plannedNote(item: WorkItemSummary, now: Date, zone: string): string {
    const notBefore = item.not_before ? new Date(item.not_before) : null;
    const retryAfter = item.retry_after ? new Date(item.retry_after) : null;
    const when =
        notBefore && notBefore > now
            ? `not before ${clock(notBefore, zone)}`
            : retryAfter && retryAfter > now
              ? `retry after ${clock(retryAfter, zone)}`
              : 'as soon as eligible';
    return item.due_at ? `${when} · due ${clock(new Date(item.due_at), zone)}` : when;
}

/** How finished work ended, in one line. */
export function doneNote(item: WorkItemSummary): string {
    if (item.status === 'failed') return item.last_error ? `failed: ${item.last_error.code}` : 'failed';
    return item.status;
}

/** Who made it: a person by name, a role by its name, the runtime as Jason. */
export function creator(actor: { type: string; id: string | null }): string {
    if (actor.type === 'system') return 'Jason';
    if (actor.id) return actor.id;
    return actor.type === 'human' ? 'a person' : `a ${actor.type}`;
}
