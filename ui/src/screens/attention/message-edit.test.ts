import { describe, expect, it } from 'vitest';
import type { Approval, WorkItem } from '../../api/types';
import { fixtures } from '../../test/fixtures';
import { EDIT_REASON, awaitNewApproval, editPatch, editable, isStale } from './message-edit';
import type { Api } from '../../api/client';

const approval = (patch: (a: Approval) => void = () => {}): Approval => {
    const copy = structuredClone(fixtures['approval.get/email-message'].response) as unknown as Approval;
    patch(copy);
    return copy;
};
const argsOf = (a: Approval) => (a.subject['input'] as { args: Record<string, unknown> }).args;
const item = (args: unknown): WorkItem => ({ context: { input: args } }) as unknown as WorkItem;
const message = (a: Approval) => argsOf(a)['message'] as Record<string, unknown>;

describe('the stale card comparison', () => {
    it('does not call two equal texts different, whatever else the arguments hold or leave out', () => {
        const asked = approval();
        const held = structuredClone(argsOf(asked));
        held['send_after'] = '2026-12-01T09:00:00+00:00';
        expect(isStale(asked, item(held))).toBe(false);
        const nulls = approval((a) => (message(a)['compose'] = null));
        expect(isStale(asked, item(argsOf(nulls)))).toBe(false);
    });

    it('calls a changed subject, body or compose different, and so a message the item no longer holds', () => {
        const asked = approval();
        for (const [field, value] of [
            ['subject', 'Another subject'],
            ['body', 'Hi Ines, thanks for the talk at the meetup. '],
            ['compose', 'template'],
        ] as const) {
            const held = structuredClone(argsOf(asked));
            (held['message'] as Record<string, unknown>)[field] = value;
            expect(isStale(asked, item(held)), field).toBe(true);
        }
        expect(isStale(asked, item({}))).toBe(true);
    });

    it('asks nothing of an approval for another operation', () => {
        const other = approval((a) => (a.operation = 'campaign.enroll'));
        expect(isStale(other, item({}))).toBe(false);
    });
});

describe('what may be edited', () => {
    it('is a pending message written as text, and not a template or a decided approval', () => {
        expect(editable(approval())).toBe(true);
        expect(editable(approval((a) => (message(a)['compose'] = 'template')))).toBe(false);
        expect(editable(approval((a) => (a.status = 'approved')))).toBe(false);
        expect(editable(approval((a) => (a.operation = 'campaign.enroll')))).toBe(false);
    });
});

describe('the patch that saves an edit', () => {
    it('replaces the subject and the body of the message and puts every other argument back as it was', () => {
        const held = structuredClone(argsOf(approval()));
        held['send_after'] = '2026-12-01T09:00:00+00:00';
        (held['message'] as Record<string, unknown>)['extra'] = { kept: true };
        const patch = editPatch('message.send', 'wi_1', held, 'New subject', 'New body\nsecond line');
        expect(patch.reason).toBe(EDIT_REASON);
        expect(Object.keys(patch.set)).toEqual(['input']);
        expect(patch.set.input).toEqual({
            ...held,
            message: { ...(held['message'] as object), subject: 'New subject', body: 'New body\nsecond line' },
        });
        expect(patch.set.input['sender']).toEqual(held['sender']);
        expect(patch.set.input['send_after']).toBe(held['send_after']);
    });

    it('gives a message without a subject none', () => {
        const patch = editPatch(
            'message.send',
            'wi_1',
            { channel: 'linkedin', message: { format: 'linkedin_message', body: 'x' } },
            's',
            'y',
        );
        expect(patch.set.input['message']).toEqual({ format: 'linkedin_message', body: 'y' });
    });
});

const reply = (body: string): Approval =>
    ({
        operation: 'conversation.reply',
        status: 'pending',
        subject: { input: { args: { channel: 'email', thread: { external_id: 't_1' }, body } } },
    }) as unknown as Approval;

describe('a reply is edited by the same rules, on its body alone', () => {
    it('compares the body, and is editable as text', () => {
        expect(editable(reply('Thanks'))).toBe(true);
        expect(
            isStale(reply('Thanks'), item({ channel: 'email', thread: { external_id: 't_1' }, body: 'Thanks' })),
        ).toBe(false);
        expect(
            isStale(reply('Thanks'), item({ channel: 'email', thread: { external_id: 't_1' }, body: 'Thanks!' })),
        ).toBe(true);
    });

    it('puts every other argument back and has no subject', () => {
        const held = {
            channel: 'email',
            thread: { external_id: 't_1' },
            reply_to_message: { external_id: 'm_1' },
            body: 'Thanks',
        };
        const patch = editPatch('conversation.reply', 'wi_2', held, 'ignored', 'Thanks a lot');
        expect(patch.set.input).toEqual({ ...held, body: 'Thanks a lot' });
        expect(patch.reason).toBe(EDIT_REASON);
    });
});

describe('waiting for the approval the runtime asks again', () => {
    const listing = (ids: string[][]): Api => {
        let n = 0;
        return {
            call: async <T>() =>
                ({ items: (ids[Math.min(n++, ids.length - 1)] ?? []).map((id) => ({ id })), next_cursor: null }) as T,
        };
    };

    it('returns the first pending approval that is not the old one, once it shows', async () => {
        expect(
            await awaitNewApproval(listing([['apr_1'], ['apr_1'], ['apr_1', 'apr_2']]), 'wi_1', 'apr_1', {
                delayMs: 0,
            }),
        ).toBe('apr_2');
    });

    it('gives up with null after its tries, and stops when told to', async () => {
        expect(await awaitNewApproval(listing([['apr_1']]), 'wi_1', 'apr_1', { tries: 3, delayMs: 0 })).toBeNull();
        expect(await awaitNewApproval(listing([['apr_2']]), 'wi_1', 'apr_1', { stopped: () => true })).toBeNull();
    });
});
