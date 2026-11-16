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

describe('a fallback answers only what nothing else the test said answers', () => {
    const contactRead = () => ({ ...fixtures['contact.list/by-campaign'].request });

    it('leaves a read it names nothing for unanswered, and an operation it does not name too', async () => {
        const fake = new FakeApi().otherwise('contact.list', () => undefined);

        await expect(fake.call('contact.list', { order: 'activity', limit: 7 })).rejects.toThrow(/nothing recorded/);
        await expect(fake.call('contact.get', { contact_id: 'ct_1' })).rejects.toThrow(/nothing recorded/);
        expect(fake.unanswered.map((each) => each.operation)).toEqual(['contact.list', 'contact.get']);
    });

    it('never takes a queued case: each stays for the call it was recorded for', async () => {
        const fake = new FakeApi()
            .answer('contact.list', 'contact.list/by-activity', 'contact.list/by-campaign')
            .otherwise('contact.list', () => 'contact.list/by-activity');

        // A request no queued case fits is the fallback's; the queue is left as it was.
        await fake.call('contact.list', { order: 'activity', limit: 7 });
        expect(await fake.call('contact.list', contactRead())).toEqual(fixtures['contact.list/by-campaign'].response);
        expect(await fake.call('contact.list', contactRead())).toEqual(fixtures['contact.list/by-campaign'].response);
        expect(fake.unanswered).toEqual([]);
    });

    it('gives way to a queued case that fits, a recording for every request, a hold and an unreachable runtime', async () => {
        const fake = new FakeApi()
            .answer('contact.list', 'contact.list/by-campaign')
            .otherwise('contact.list', () => 'contact.list/by-activity');
        expect(await fake.call('contact.list', contactRead())).toEqual(fixtures['contact.list/by-campaign'].response);

        fake.answerAnyRequest('contact.list', 'contact.list/by-campaign');
        expect(await fake.call('contact.list', { order: 'activity', limit: 7 })).toEqual(
            fixtures['contact.list/by-campaign'].response,
        );

        const release = fake.hold('contact.list');
        let answered = false;
        const held = fake.call('contact.list', contactRead()).then(() => (answered = true));
        await Promise.resolve();
        expect(answered).toBe(false);
        release();
        await held;

        const back = fake.unreachable('contact.list');
        await expect(fake.call('contact.list', contactRead())).rejects.toThrow('Failed to fetch');
        back();
    });
});
