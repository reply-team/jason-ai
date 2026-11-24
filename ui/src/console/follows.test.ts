import { describe, expect, it } from 'vitest';
import { followsOf, followsSentence } from './follows';

describe('what an email send follows, in words', () => {
    it('reads the preview highlight and nothing else', () => {
        expect(followsOf(null)).toBeNull();
        expect(followsOf('x')).toBeNull();
        expect(
            followsOf({ work_item: 'wi_1', operation: 'message.send', reference: 'r', because: 'line_root' }),
        ).toEqual({
            workItem: 'wi_1',
            because: 'line_root',
        });
    });

    it('says a reply goes under the followed letter and its own subject is not sent', () => {
        const words = followsSentence({ workItem: 'wi_1', because: 'named' }, { subject: 'Hello', sent: '2 d ago' });
        expect(words).toBe(
            'A reply to "Hello", sent 2 d ago. It goes under that subject; its own subject below is not sent.',
        );
    });

    it('says so without the letter when it could not be read', () => {
        expect(followsSentence({ workItem: 'wi_1', because: 'named' }, null)).toMatch(/^A reply to an earlier letter/);
    });

    it.each(['asked_new_thread', 'not_offered', 'no_earlier_letter', 'other_account', 'other_mailbox'])(
        'says why %s is a new letter',
        (because) => {
            expect(followsSentence({ workItem: null, because }, null)).toMatch(/^A new letter: /);
        },
    );
});
