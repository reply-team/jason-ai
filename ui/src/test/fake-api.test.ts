import { describe, expect, it } from 'vitest';
import { FakeApi } from './fake-api';
import { fixtures } from './fixtures';

describe('the fake answers a request only with the recording made for it', () => {
    it('a call is answered by the queued case whose request it equals, keys in any order, undefined dropped', async () => {
        const fake = new FakeApi().answer('contact.list', 'contact.list/by-activity', 'contact.list/by-campaign');
        const recorded = fixtures['contact.list/by-campaign'];
        const reversed = Object.fromEntries(Object.entries(recorded.request).reverse());

        const page = await fake.call('contact.list', { ...reversed, query: undefined });

        expect(page).toEqual(recorded.response);
    });

    it('a write is matched with the actor the client adds, and sent() gives the body without it', async () => {
        const fake = new FakeApi('ada').answer('approval.approve', 'approval.approve/approved');
        const { approval_id } = fixtures['approval.approve/approved'].request;

        const approved = await fake.call('approval.approve', { approval_id }, 'write');

        expect(approved).toEqual(fixtures['approval.approve/approved'].response);
        expect(fake.sent('approval.approve')).toEqual({ approval_id });
    });

    it('a call no queued case matches rejects and is remembered', async () => {
        const fake = new FakeApi().answer('contact.list', 'contact.list/by-activity');

        await expect(fake.call('contact.list', { order: 'activity', limit: 7 })).rejects.toThrow(
            /contact\.list .*"limit":7.*contact\.list\/by-activity/,
        );
        expect(fake.unanswered).toEqual([
            { operation: 'contact.list', body: { order: 'activity', limit: 7 }, kind: 'read' },
        ]);
    });
});
