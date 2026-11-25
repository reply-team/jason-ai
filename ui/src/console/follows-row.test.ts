import { describe, expect, it } from 'vitest';
import { followsRow, subjectNotSent } from './follows';

const followed = { subject: 'Hello', moment: '3 Oct, 18:02' };
const under = { workItem: 'wi_1', because: 'named' };
const own = { workItem: null, because: 'asked_new_thread' };

describe('the Follows row both shells draw', () => {
    it('says a follow-up goes under the letter it follows and marks its subject as not sent', () => {
        const row = followsRow(under, true, followed);
        expect(row?.words).toMatch(/^A reply to "Hello" of 3 Oct, 18:02, as asked\./);
        expect(row?.subjectNotSent).toBe(true);
    });

    it('says the unread sentence for a follow-up whose letter is not read, and still marks the subject', () => {
        const row = followsRow(under, true, null);
        expect(row?.words).toMatch(/^A reply to an earlier letter of this campaign\./);
        expect(row?.subjectNotSent).toBe(true);
    });

    it('says a letter of its own in the row’s own words and marks no subject', () => {
        expect(followsRow({ workItem: null, because: 'not_offered' }, true, null)).toEqual({
            words: "A new letter: this provider's plugin does not offer a reply in a thread on this channel.",
            subjectNotSent: false,
        });
        expect(followsRow(own, true, null)?.words).toBe('A new letter, as asked.');
    });

    it('shows no row where the preview says nothing of where the send goes', () => {
        expect(followsRow(null, true, followed)).toBeNull();
        expect(subjectNotSent(null, true)).toBe(false);
    });

    it('shows no row and marks nothing for a send that is not an email', () => {
        expect(followsRow(under, false, followed)).toBeNull();
        expect(subjectNotSent(under, false)).toBe(false);
    });
});
