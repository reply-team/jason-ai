import { describe, expect, it } from 'vitest';
import type { ApprovalSummary } from '../../api/types';
import { fixtures } from '../../test/fixtures';
import {
    afterRead,
    askedAfter,
    askedIn,
    askingOf,
    differs,
    editOf,
    placeRows,
    rowNow,
    stateSentence,
    withWords,
    withdrawnOf,
    type Edited,
} from './reasking-model';

const withdrawn = fixtures['approval.list/card-message-pending'].response.items[0] as ApprovalSummary;
const asked = fixtures['approval.list/card-message-reasked'].response.items[0] as ApprovalSummary;
const others = fixtures['approval.list/needs-first-page'].response.items as ApprovalSummary[];
// Every approval of the edited item, in every status, as the runtime listed them at each step of the recorded story.
const afterEdit = fixtures['approval.list/card-message-item-all-after-edit'].response.items as ApprovalSummary[];
const reasked = fixtures['approval.list/card-message-item-all-reasked'].response.items as ApprovalSummary[];
const approved = fixtures['approval.list/card-message-item-all-approved'].response.items as ApprovalSummary[];
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
    ended: null,
    read: 'queued',
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

    it('keeps the approval it withdrew withdrawn after it was asked again, and after its work ended', () => {
        expect(withdrawnOf([edit], withdrawn.id)).toBe(edit);
        const now = { ...edit, asked: asked.id };
        expect(withdrawnOf([now], withdrawn.id)).toBe(now);
        // The approval asked again is not withdrawn: it is the one acted on.
        expect(withdrawnOf([now], asked.id)).toBeUndefined();
        const ended = { ...edit, ended: 'cancelled' };
        expect(withdrawnOf([ended], withdrawn.id)).toBe(ended);
        expect(askingOf([ended], withdrawn.id)).toBeUndefined();
    });

    it('shows the words saved, keeping what the edit did not write', () => {
        const read = { subject: 'old subject', body: 'old body', to: 'Ines' };
        expect(withWords(read, edit.words)).toEqual({ ...read, subject: edit.words.subject, body: edit.words.body });
        // A reply has no subject of its own: the one read stays.
        expect(withWords(read, { subject: null, body: 'new', compose: null })).toEqual({ ...read, body: 'new' });
    });

    it('says whether the approval asked again carries other words than the ones saved', () => {
        expect(differs(edit, { ...edit.words })).toBe(false);
        expect(differs(edit, { ...edit.words, body: 'Hi Ines, an agent wrote this.' })).toBe(true);
        expect(differs(edit, { ...edit.words, subject: 'Another subject' })).toBe(true);
        // A reply's edit wrote no subject: the subject is not compared.
        const reply = { ...edit, words: { ...edit.words, subject: null } };
        expect(differs(reply, { ...edit.words, subject: 'x' })).toBe(false);
    });
});

describe('what the row of an edited message says', () => {
    it('promises only that the text is saved and that Jason asks again when it next takes the work', () => {
        expect(stateSentence(edit)).toBe(
            'Your new text is saved. Jason asks again with it when it next takes this work; Approve and Reject come back then.',
        );
    });

    it('says a read that failed as not read, never as asking', () => {
        expect(stateSentence({ ...edit, read: 'unread' })).toMatch(/could not be read just now/);
        expect(stateSentence({ ...edit, read: 'unread' })).not.toMatch(/asks again with it/);
    });

    it('says how the work ended, and that nothing on it can be approved', () => {
        expect(stateSentence({ ...edit, ended: 'cancelled' })).toMatch(
            /ended before Jason asked again: it was cancelled\./,
        );
        expect(stateSentence({ ...edit, ended: 'gone' })).toMatch(/it is no longer there/);
        expect(stateSentence({ ...edit, ended: 'failed' })).toMatch(/Nothing here can be approved\.$/);
    });

    it('says the approval it withdrew cannot be approved once Jason asked again', () => {
        expect(stateSentence({ ...edit, asked: asked.id })).toMatch(/withdrawn by your edit and cannot be approved/);
    });
});

describe('what a kept row becomes at a read of its list', () => {
    it('becomes the approval asked again when the list holds it', () => {
        expect(rowNow(edit, [asked])).toEqual({ asked });
        expect(rowNow({ ...edit, asked: asked.id }, [asked])).toEqual({ asked });
    });

    it('needs a read of its work item while it is not asked and the list does not hold one', () => {
        expect(rowNow(edit, [])).toBe('read');
    });

    it('goes once it was asked, here or by another list, and the list does not hold the approval asked', () => {
        expect(rowNow({ ...edit, asked: asked.id }, [])).toBe('gone');
        expect(rowNow({ ...edit, asked: asked.id }, others)).toBe('gone');
    });

    it('goes once its work ended another way', () => {
        expect(rowNow({ ...edit, ended: 'cancelled' }, [])).toBe('gone');
    });

    it('is queued while its work is, and ended once the work ended another way or is not there', () => {
        for (const status of ['created', 'scheduled', 'processing', 'waiting', 'awaiting_approval'])
            expect(afterRead(edit, status, afterEdit)).toBe('queued');
        for (const status of ['succeeded', 'failed', 'cancelled', 'expired'])
            expect(afterRead(edit, status, afterEdit)).toEqual({ ended: status });
        expect(afterRead(edit, null, [])).toEqual({ ended: 'gone' });
    });

    it('is asked once an approval of its work item other than the withdrawn one is pending', () => {
        expect(afterRead(edit, 'awaiting_approval', reasked)).toEqual({ asked });
    });

    it('was decided elsewhere once the approval asked again is decided, whatever the work does now', () => {
        for (const status of ['created', 'scheduled', 'processing'])
            expect(afterRead(edit, status, approved)).toEqual({ decided: asked.id });
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
