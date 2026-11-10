import { describe, expect, it } from 'vitest';
import { answered, failed, lost, newChat, received, said, shown, type Chat } from './chat-model';

const asking = (chat: Chat): Chat =>
    received(chat, {
        kind: 'permission',
        request: 'req_1',
        title: 'Run a command',
        detail: 'git status',
        remember: 'git status \u2026',
    });

describe('a chat as the page keeps it', () => {
    it('takes its title from the first words, or from the campaign it was opened from', () => {
        const chat = said(newChat('c1', 'claude'), 'What is waiting for me?');
        expect(chat).toMatchObject({ title: 'What is waiting for me?', running: true });
        expect(said(chat, 'And then?').title).toBe('What is waiting for me?');
        expect(said(newChat('c2', 'claude'), 'x'.repeat(80)).title).toHaveLength(48);
        const about = said(newChat('c3', 'claude', { id: 'cmp_1', name: 'Autumn outreach' }), 'How is it going?');
        expect(about).toMatchObject({ title: 'Autumn outreach', campaignId: 'cmp_1' });
    });

    it('folds the steps between two messages, and keeps the last ones open while the turn runs', () => {
        let chat = said(newChat('c1', 'claude'), 'Go');
        chat = received(chat, { kind: 'step', text: 'Ran jason status' });
        chat = received(chat, { kind: 'step', text: 'Ran jason campaign list' });
        chat = received(chat, { kind: 'text', text: 'Two campaigns.' });
        chat = received(chat, { kind: 'step', text: 'Ran jason approval list' });
        expect(shown(chat)).toEqual([
            { kind: 'user', text: 'Go' },
            { kind: 'steps', steps: ['Ran jason status', 'Ran jason campaign list'], open: false },
            { kind: 'text', text: 'Two campaigns.' },
            { kind: 'steps', steps: ['Ran jason approval list'], open: true },
        ]);
    });

    it('lets the first answer to a request stand, and says when a request no longer waits', () => {
        const chat = asking(said(newChat('c1', 'claude'), 'Go'));
        const allowed = answered(chat, 'req_1', 'always');
        expect(answered(allowed, 'req_1', 'deny').entries[1]).toMatchObject({ answer: 'always' });
        expect(received(allowed, { kind: 'permission_gone', request: 'req_1' }).entries[1]).toMatchObject({
            answer: 'always',
        });
        expect(received(chat, { kind: 'permission_gone', request: 'req_1' }).entries[1]).toMatchObject({
            answer: 'gone',
        });
        expect(received(chat, { kind: 'permission_gone', request: 'req_9' })).toEqual(chat);
        expect(lost(allowed, 'req_1').entries[1]).toMatchObject({ answer: 'gone' });
    });

    it('ends a turn with the host own words when it failed, and with every request it held gone', () => {
        const chat = asking(said(newChat('c1', 'claude'), 'Go'));
        const over = received(chat, { kind: 'done', error: 'Not logged in' });
        expect(over.running).toBe(false);
        expect(over.entries.slice(1)).toEqual([
            {
                kind: 'permission',
                request: 'req_1',
                title: 'Run a command',
                detail: 'git status',
                remember: 'git status \u2026',
                answer: 'gone',
            },
            { kind: 'error', text: 'Not logged in' },
        ]);
        expect(received(said(newChat('c1', 'claude'), 'Go'), { kind: 'done', error: null }).entries).toHaveLength(1);
        expect(failed(said(newChat('c1', 'claude'), 'Go'), 'No such host')).toMatchObject({
            running: false,
            entries: [{ kind: 'user' }, { kind: 'error', text: 'No such host' }],
        });
    });
});
