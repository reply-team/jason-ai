import { describe, expect, it } from 'vitest';
import {
    answered,
    failed,
    fromRecord,
    handed,
    kept,
    lost,
    newChat,
    pickedFor,
    received,
    replaced,
    said,
    shown,
    stopAsked,
    takenBack,
    typed,
    type Chat,
} from './chat-model';

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
        // As the desktop names a set-up chat: its first sixty characters, on one line.
        expect(said(newChat('c2', 'claude'), 'x'.repeat(80)).title).toBe('x'.repeat(60));
        expect(said(newChat('c4', 'claude'), 'Find\n\nleads   in Berlin').title).toBe('Find leads in Berlin');
        const about = said(newChat('c3', 'claude', { id: 'cmp_1', name: 'Autumn outreach' }), 'How is it going?');
        expect(about).toMatchObject({ title: 'Autumn outreach', campaignId: 'cmp_1' });
    });

    it('folds the steps between two messages, and keeps the last ones open while the turn runs', () => {
        let chat = said(newChat('c1', 'claude'), 'Go');
        chat = received(chat, { kind: 'step', text: 'Ran a jason command', detail: 'jason status' });
        chat = received(chat, { kind: 'step', text: 'Updated its task list' });
        chat = received(chat, { kind: 'text', text: 'Two campaigns.' });
        chat = received(chat, { kind: 'step', text: 'Ran a jason command', detail: 'jason approval list' });
        expect(shown(chat)).toEqual([
            { kind: 'user', text: 'Go' },
            {
                kind: 'steps',
                steps: [{ text: 'Ran a jason command', detail: 'jason status' }, { text: 'Updated its task list' }],
                open: false,
            },
            { kind: 'text', text: 'Two campaigns.' },
            { kind: 'steps', steps: [{ text: 'Ran a jason command', detail: 'jason approval list' }], open: true },
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

    it('becomes a campaign’s conversation on a hand-over, and keeps a fresh session and a pick in its thread', () => {
        const chat = said(newChat('conv_1', 'claude'), 'Set up an autumn campaign');
        const attached = received(chat, { kind: 'attached', campaignId: 'cmp_1', title: 'Autumn outreach' });
        expect(attached).toMatchObject({ campaignId: 'cmp_1', title: 'Autumn outreach', running: true });
        expect(attached.entries.at(-1)).toEqual({ kind: 'attached', title: 'Autumn outreach' });
        expect(received(chat, { kind: 'fresh', reason: 'not_found' }).entries.at(-1)).toEqual({
            kind: 'fresh',
            reason: 'not_found',
        });
        expect(received(chat, { kind: 'pick', campaignIds: ['cmp_1', 'cmp_2'] }).entries.at(-1)).toEqual({
            kind: 'pick',
            campaignIds: ['cmp_1', 'cmp_2'],
        });
        // A pick the person settled names the campaign; a set-up chat's later turns keep their title.
        const picked = pickedFor(received(chat, { kind: 'pick', campaignIds: ['cmp_1', 'cmp_2'] }), {
            id: 'conv_1',
            campaignId: 'cmp_2',
            title: 'Winter',
            updatedAt: '2026-11-11T10:00:00.000Z',
            running: false,
        });
        expect(picked).toMatchObject({ campaignId: 'cmp_2', title: 'Winter' });
        expect(said(picked, 'Next').title).toBe('Winter');
    });

    it('opens a kept conversation with its entries drawn as live ones are, its open cards ended', () => {
        const chat = fromRecord(
            {
                id: 'conv_1',
                campaignId: 'cmp_1',
                title: 'Spring',
                updatedAt: '2026-11-11T10:00:00.000Z',
                running: false,
                dropped: 3,
                entries: [
                    { kind: 'user', text: 'How is it doing?' },
                    { kind: 'step', text: 'Ran a jason command', detail: 'jason campaign get cmp_1' },
                    { kind: 'step', text: 'Read its task list' },
                    { kind: 'permission', title: 'Run a command', detail: 'git status', answer: 'deny' },
                    { kind: 'permission', title: 'Read a file', detail: '/x', answer: null },
                    { kind: 'text', text: 'Well.' },
                    { kind: 'error', text: 'Claude Code stopped.' },
                    { kind: 'pick', campaignIds: ['cmp_2'] },
                ],
            },
            'claude',
        );
        expect(chat).toMatchObject({ id: 'conv_1', campaignId: 'cmp_1', title: 'Spring', running: false, dropped: 3 });
        expect(shown(chat)).toEqual([
            { kind: 'user', text: 'How is it doing?' },
            {
                kind: 'steps',
                steps: [
                    { text: 'Ran a jason command', detail: 'jason campaign get cmp_1' },
                    { text: 'Read its task list' },
                ],
                open: false,
            },
            {
                kind: 'permission',
                request: '',
                title: 'Run a command',
                detail: 'git status',
                remember: null,
                answer: 'deny',
            },
            { kind: 'permission', request: '', title: 'Read a file', detail: '/x', remember: null, answer: 'gone' },
            { kind: 'text', text: 'Well.' },
            { kind: 'error', text: 'Claude Code stopped.' },
            { kind: 'pick', campaignIds: ['cmp_2'] },
        ]);
        // A kept card is never answered again: no request of a turn that ended waits.
        expect(answered(chat, '', 'allow')).toEqual(chat);
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

/** A chat whose turn runs, with `text` typed into the box. */
const busy = (text: string): Chat => typed(said(newChat('c1', 'claude'), 'Go'), text);
const done = (chat: Chat, error: string | null = null): Chat => received(chat, { kind: 'done', error });

describe('a message typed while a turn runs', () => {
    it('waits, one at a time, and the box is cleared for the next words', () => {
        const waiting = kept(busy('  And then?  '));
        expect(waiting).toMatchObject({ waiting: 'And then?', held: false, draft: '', running: true });
        expect(waiting.entries).toEqual([{ kind: 'user', text: 'Go' }]);
        expect(kept(busy('   '))).toEqual(busy('   '));
        expect(kept(typed(newChat('c1', 'claude'), 'Idle'))).toMatchObject({ waiting: null, draft: 'Idle' });
    });

    it('never replaces the waiting one silently: a second keeps its text in the box', () => {
        const second = kept(typed(kept(busy('First')), 'Second'));
        expect(second).toMatchObject({ waiting: 'First', draft: 'Second' });
    });

    it('is swapped with the box on Replace, never merged', () => {
        const second = typed(kept(busy('First')), 'Second');
        expect(replaced(second)).toMatchObject({ waiting: 'Second', draft: 'First' });
        expect(replaced(kept(busy('First')))).toMatchObject({ waiting: 'First', draft: '' });
    });

    it('is taken back only into an empty box, so nothing typed there is overwritten', () => {
        expect(takenBack(kept(busy('First')))).toMatchObject({ waiting: null, draft: 'First' });
        const second = typed(kept(busy('First')), 'Second');
        expect(takenBack(second)).toEqual(second);
    });

    it('is handed on once, at the end of a turn that succeeded, and two quick ends send it once', () => {
        const over = done(kept(busy('And then?')));
        expect(over).toMatchObject({ waiting: null, outbox: 'And then?', running: false });
        expect(over.entries).toEqual([{ kind: 'user', text: 'Go' }]);
        const sent = said(handed(over), 'And then?', true);
        expect(sent).toMatchObject({ outbox: null, running: true, resent: 'And then?' });
        const again = done(sent);
        expect(again).toMatchObject({ waiting: null, outbox: null, running: false, resent: null });
        expect(done(again)).toMatchObject({ outbox: null, running: false });
    });

    it('goes back into an empty box, unsent, when the turn failed, with the error shown', () => {
        const over = done(kept(busy('And then?')), 'Not logged in');
        expect(over).toMatchObject({ waiting: null, outbox: null, running: false, draft: 'And then?' });
        expect(over.entries.at(-1)).toEqual({ kind: 'error', text: 'Not logged in' });
    });

    it('stays in sight, not sent, when the turn failed while the box held other words', () => {
        const over = done(typed(kept(busy('And then?')), 'More'), 'Not logged in');
        expect(over).toMatchObject({ waiting: 'And then?', held: true, outbox: null, draft: 'More' });
        expect(over.entries.at(-1)).toEqual({ kind: 'error', text: 'Not logged in' });
        // Held is never sent: not at the next good end, and nothing else waits beside it.
        const next = typed(said({ ...over, draft: '' }, 'More'), 'Later');
        expect(kept(next)).toEqual(next);
        expect(done(next)).toMatchObject({ waiting: 'And then?', held: true, outbox: null });
        expect(takenBack(done(typed(next, '')))).toMatchObject({ waiting: null, held: false, draft: 'And then?' });
    });

    it('is given back the moment the person stops the turn, so the end that follows sends nothing', () => {
        const stopped = stopAsked(kept(busy('And then?')));
        expect(stopped).toMatchObject({ stopping: true, running: true, waiting: null, draft: 'And then?' });
        expect(kept(stopped)).toEqual(stopped);
        // The person types again before the stopped turn's end arrives; the end changes nothing in the box.
        const over = done(typed(stopped, 'Something else'));
        expect(over).toMatchObject({ waiting: null, outbox: null, running: false, draft: 'Something else' });
        expect(over.entries).toEqual([{ kind: 'user', text: 'Go' }]);
    });

    it('stays in sight, not sent, when the person stops the turn while the box held other words', () => {
        const stopped = stopAsked(typed(kept(busy('And then?')), 'More'));
        expect(stopped).toMatchObject({ waiting: 'And then?', held: true, draft: 'More' });
        expect(done(stopped)).toMatchObject({ waiting: 'And then?', held: true, outbox: null, draft: 'More' });
    });

    it('is given back when the turn it became fails, after a message that waited behind it', () => {
        const resent = (draft: string) => typed(said(newChat('c1', 'claude'), 'And then?', true), draft);
        expect(done(resent(''), 'Too long')).toMatchObject({ draft: 'And then?', waiting: null });
        expect(done(resent('More'), 'Too long')).toMatchObject({ draft: 'More', waiting: 'And then?', held: true });
        const behind = typed(kept(resent('Third')), 'Fourth');
        expect(done(behind, 'Too long')).toMatchObject({ draft: 'Fourth', waiting: 'Third', held: true });
        expect(done(resent(''))).toMatchObject({ draft: '', resent: null });
    });

    it('goes back when the desktop refused the turn', () => {
        const over = failed(kept(busy('And then?')), 'The desktop is closing.');
        expect(over).toMatchObject({
            waiting: null,
            outbox: null,
            running: false,
            stopping: false,
            draft: 'And then?',
        });
    });
});
