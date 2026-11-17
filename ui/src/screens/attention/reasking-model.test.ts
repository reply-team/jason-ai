import { describe, expect, it } from 'vitest';
import type { ApprovalSummary } from '../../api/types';
import { fixtures } from '../../test/fixtures';
import {
    afterRead,
    askedAfter,
    askedIn,
    askingOf,
    editOf,
    placeRows,
    rowNow,
    withWords,
    type Edited,
} from './reasking-model';

const withdrawn = fixtures['approval.list/card-message-pending'].response.items[0] as ApprovalSummary;
const asked = fixtures['approval.list/card-message-reasked'].response.items[0] as ApprovalSummary;
const others = fixtures['approval.list/needs-first-page'].response.items as ApprovalSummary[];
const row = (id: string, workItemId = `wi_of_${id}`): ApprovalSummary => ({
    ...others[0]!,
    id,
    work_item_id: workItemId,
});

const edit: Edited = {
    workItemId: withdrawn.work_item_id,
    withdrawn: withdrawn.id,
    operation: 'message.send',
    words: { subject: 'Notes from the meetup', body: 'Hi Ines, shall we talk next week?', compose: null },
    asked: null,
};
const ids = (rows: readonly ApprovalSummary[]) => rows.map((each) => each.id);

describe('an edited message’s row, matched by its work item alone', () => {
    it('is the pending approval of the same work item other than the one the edit withdrew', () => {
        expect(asked.work_item_id).toBe(withdrawn.work_item_id);
        expect(askedIn(edit, [withdrawn, asked])).toBe(asked);
        expect(askedIn(edit, [withdrawn])).toBeUndefined();
        // Another work item's approval is never the one asked again, whatever else it shares.
        expect(askedIn(edit, [{ ...asked, work_item_id: 'wi_9' }])).toBeUndefined();
        // Nor is one no longer pending.
        expect(askedIn(edit, [{ ...asked, status: 'approved' }])).toBeUndefined();
    });

    it('is asking until it is asked, then names the approval asked after the one withdrawn', () => {
        expect(askingOf([edit], withdrawn.id)).toBe(edit);
        expect(askedAfter([edit], withdrawn.id)).toBeNull();
        const now = { ...edit, asked: asked.id };
        expect(askingOf([now], withdrawn.id)).toBeUndefined();
        expect(askedAfter([now], withdrawn.id)).toBe(asked.id);
        expect(editOf([now], asked.id)).toBe(now);
        expect(editOf([now], withdrawn.id)).toBe(now);
        expect(editOf([now], 'apr_other')).toBeUndefined();
    });

    it('shows the words saved, keeping what the edit did not write', () => {
        const read = { subject: 'old subject', body: 'old body', to: 'Ines' };
        expect(withWords(read, edit.words)).toEqual({ ...read, subject: edit.words.subject, body: edit.words.body });
        // A reply has no subject of its own: the one read stays.
        expect(withWords(read, { subject: null, body: 'new', compose: null })).toEqual({ ...read, body: 'new' });
    });
});

describe('what a kept row becomes at a read of its list', () => {
    it('becomes the approval asked again when the list holds it', () => {
        expect(rowNow(edit, withdrawn, [asked])).toEqual({ asked });
    });

    it('needs a read of its work item while the list does not hold one', () => {
        expect(rowNow(edit, withdrawn, [])).toBe('read');
        // Asked elsewhere, but this list still drew the withdrawn row: only the read says where it is.
        expect(rowNow({ ...edit, asked: asked.id }, withdrawn, [])).toBe('read');
    });

    it('goes once the approval asked again, drawn here, has left the list: it was decided', () => {
        expect(rowNow({ ...edit, asked: asked.id }, asked, [])).toBe('gone');
    });

    it('is still asking while its work is queued or running, and ended once the work ended another way', () => {
        for (const status of ['created', 'scheduled', 'processing', 'waiting'])
            expect(afterRead(edit, status)).toBe('asking');
        for (const status of ['succeeded', 'failed', 'cancelled', 'expired'])
            expect(afterRead(edit, status)).toBe('ended');
    });

    it('is asked once its work waits for an approval again and that approval is among the item’s pending ones', () => {
        expect(afterRead(edit, 'awaiting_approval', [asked])).toEqual({ asked });
        expect(afterRead(edit, 'awaiting_approval', [])).toBe('asking');
        expect(afterRead(edit, 'awaiting_approval', [withdrawn])).toBe('asking');
    });
});

describe('a kept row keeps its place', () => {
    const [a, b, c] = [row('apr_a'), row('apr_b'), row('apr_c')];

    it('stands after the row it stood after, whatever order the runtime lists the rest in', () => {
        // The runtime lists the approval asked again last, by its newer id; the row stays second.
        expect(ids(placeRows([a.id, withdrawn.id, b.id], [a, b, asked], [{ from: withdrawn.id, row: asked }]))).toEqual(
            [a.id, asked.id, b.id],
        );
    });

    it('stays where it stood while the runtime lists it nowhere', () => {
        expect(ids(placeRows([a.id, withdrawn.id, b.id], [a, b], [{ from: withdrawn.id, row: withdrawn }]))).toEqual([
            a.id,
            withdrawn.id,
            b.id,
        ]);
    });

    it('stands first when it stood first, or when every row before it has left', () => {
        expect(ids(placeRows([withdrawn.id, a.id], [a], [{ from: withdrawn.id, row: withdrawn }]))).toEqual([
            withdrawn.id,
            a.id,
        ]);
        expect(ids(placeRows([a.id, withdrawn.id, b.id], [b], [{ from: withdrawn.id, row: withdrawn }]))).toEqual([
            withdrawn.id,
            b.id,
        ]);
    });

    it('is drawn once when the runtime lists it too, and every other row keeps the runtime’s order', () => {
        expect(
            ids(
                placeRows(
                    [a.id, withdrawn.id, b.id, c.id],
                    [c, a, withdrawn, b],
                    [{ from: withdrawn.id, row: withdrawn }],
                ),
            ),
        ).toEqual([c.id, a.id, withdrawn.id, b.id]);
    });

    it('places two kept rows each after its own neighbour, one after the other', () => {
        const second = row('apr_second', 'wi_second');
        const asked2 = row('apr_second_asked', 'wi_second');
        expect(
            ids(
                placeRows(
                    [withdrawn.id, a.id, second.id],
                    [a, asked2, asked],
                    [
                        { from: second.id, row: asked2 },
                        { from: withdrawn.id, row: asked },
                    ],
                ),
            ),
        ).toEqual([asked.id, a.id, asked2.id]);
    });

    it('lists the runtime’s rows as they come when nothing is kept', () => {
        expect(ids(placeRows([a.id], [b, a], []))).toEqual([b.id, a.id]);
    });
});
