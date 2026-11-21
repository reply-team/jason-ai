import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import type { DesktopConversationRecord, DesktopEffort, DesktopHost, DesktopLineEntry } from '../platform/desktop';
import {
    anotherModel,
    answered,
    effortChosen,
    hostChosen,
    modelChosen,
    modelTyped,
    modelView,
    onHost,
    sendHeld,
    turnOptions,
    withModels,
    drawnMode,
    fullAccessLine,
    autoEditLine,
    olderDesktopAutoEdit,
    modeChoices,
    olderDesktop,
    failed,
    fromRecord,
    handed,
    kept,
    lost,
    newChat,
    pickedFor,
    queued,
    received,
    said,
    shown,
    stopAsked,
    takenBack,
    everyWordBack,
    exportNotTyped,
    exportTypeIt,
    typed,
    afresh,
    noteClosed,
    withPaths,
    dropCaretAt,
    codexPlanLine,
    choiceStops,
    livePlan,
    planElsewhere,
    planOtherHost,
    moded,
    planApproved,
    planGo,
    planLine,
    planModeMoved,
    told,
    actKinds,
    choiceState,
    personActed,
    type Chat,
    unseen,
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
        // The characters the desktop drops from a title are dropped here too: controls, format characters, separators.
        const hidden = ['\u200b', '\u{E0041}', '\u0085'];
        expect(said(newChat('c5', 'claude'), `Find${hidden[0]} leads${hidden[1]} in Berlin${hidden[2]}`).title).toBe(
            'Find leads in Berlin',
        );
        // And every default-ignorable code point, as the desktop drops it: a filler draws nothing.
        expect(said(newChat('c6', 'claude'), '\u3164\u115f\ufe0fFind leads').title).toBe('Find leads');
        const about = said(newChat('c3', 'claude', { id: 'cmp_1', name: 'Autumn outreach' }), 'How is it going?');
        expect(about).toMatchObject({ title: 'Autumn outreach', campaignId: 'cmp_1' });
    });

    it('takes a new title from the desktop, a person’s rename or the name it asked for, and leaves the thread and the turn as they were', () => {
        const running = said(newChat('c1', 'claude'), 'Help me reach dentists');
        const renamed = received(running, { kind: 'renamed', title: 'Dentist outreach' });
        expect(renamed).toMatchObject({ title: 'Dentist outreach', running: true, entries: running.entries });
        expect(said(received(renamed, { kind: 'done', error: null }), 'Next').title).toBe('Dentist outreach');
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

    it('keeps a notice the desktop says in the thread, with the folder it names for the Open folder button', () => {
        const chat = said(newChat('conv_1', 'claude'), 'Go');
        expect(received(chat, { kind: 'notice', text: 'Left out of this chat: x.' }).entries.at(-1)).toEqual({
            kind: 'notice',
            text: 'Left out of this chat: x.',
            folder: null,
        });
        expect(
            received(chat, { kind: 'notice', text: 'Kept seven days: /d', folder: 'ID-1788' }).entries.at(-1),
        ).toEqual({ kind: 'notice', text: 'Kept seven days: /d', folder: 'ID-1788' });
    });

    describe('the paths of files dropped on the box', () => {
        const windows = 'C:\\Users\\Ана Ли\\Desktop\\1.txt';

        it('go in at the caret, each quoted, with a space after them, as a terminal puts them', () => {
            expect(withPaths('', 0, 0, [windows])).toEqual({
                text: `'${windows}' `,
                caret: windows.length + 3,
            });
            expect(withPaths('add these lines to ', 19, 19, ['/Users/ana/1.txt'])).toEqual({
                text: "add these lines to '/Users/ana/1.txt' ",
                caret: 38,
            });
        });

        it('stand apart from the words on each side, and several go in one after another', () => {
            expect(withPaths('readthat now', 4, 4, ['/a b', '/c'])).toEqual({
                text: "read '/a b' '/c' that now",
                caret: 17,
            });
            expect(withPaths('compare  and', 8, 8, ['/x'])).toEqual({ text: "compare '/x' and", caret: 12 });
        });

        it('take the place of the words selected', () => {
            expect(withPaths('open FILE please', 5, 9, ['/d/e.txt'])).toEqual({
                text: "open '/d/e.txt' please",
                caret: 15,
            });
        });

        const put = (path: string) => withPaths('', 0, 0, [path]).text;

        it('on macOS and Linux go in single quotes, where a shell takes every sign as it is, closed around a quote', () => {
            expect(put('/Users/ana/a"b.txt')).toBe(`'/Users/ana/a"b.txt' `);
            expect(put('/Users/ana/$x.txt')).toBe(`'/Users/ana/$x.txt' `);
            expect(put('/Users/ana/`date`.txt')).toBe("'/Users/ana/`date`.txt' ");
            expect(put('/Users/ana/hi!.txt')).toBe(`'/Users/ana/hi!.txt' `);
            expect(put("/Users/ana/it's.txt")).toBe(`'/Users/ana/it'\\''s.txt' `);
        });

        it('leave the caret after them once the box shows them, and nowhere for another conversation or a text they never reached', () => {
            const left = { key: 'conv_1', text: "see '/a' ", at: 9 };
            expect(dropCaretAt(left, 'conv_1', "see '/a' ")).toBe(9);
            expect(dropCaretAt(left, 'conv_2', "see '/a' ")).toBeNull();
            // A conversation gone before the paths went in: a later word typed in a box takes no caret meant for them.
            expect(dropCaretAt(left, 'conv_1', 'see x')).toBeNull();
            expect(dropCaretAt(null, 'conv_1', 'see ')).toBeNull();
        });

        // What each Windows form below reads back as, run through Git Bash (`printf '%s\n' <form>`) and through
        // PowerShell 7 and Windows PowerShell 5.1 (`Write-Output <form>`); the unit project starts no shell itself.
        //
        //   form                                  Git Bash                    PowerShell 7 and 5.1
        //   'C:\Users\ana\a b.txt'                the name                    the name
        //   'C:\Users\ana\$x.txt'                 the name                    the name
        //   'C:\Users\ana\`date`.txt'             the name                    the name
        //   'C:\Users\ana\100% a&b;c hi!.txt'     the name                    the name
        //   'C:\Users\Ана Ли\Desktop\1.txt'       the name                    the name
        //   '\\server\share\a.txt'                the name                    the name
        //   "C:\Users\ana\it's.txt"               the name                    the name
        //   "C:\Ана’s ‘q’ ‚z‛.txt"                the name                    the name
        //   '\\server\share\it''s.txt'            \\server\share\its.txt      the name
        //   'C:\Users\ana\$x it''s.txt'           C:\Users\ana\$x its.txt     the name
        //   'C:\Users\ana\`d` it’’s.txt'          C:\Users\ana\`d` it’’s.txt  the name
        //
        // The double quotes this form replaced lost a UNC path's first backslash in Git Bash: "\\server\share\a.txt"
        // read back as \server\share\a.txt.
        const dir = 'C:\\Users\\ana\\';

        it('on Windows go in single quotes where the name holds no quote, as PowerShell and Git Bash both read them', () => {
            expect(put(`${dir}a b.txt`)).toBe("'C:\\Users\\ana\\a b.txt' ");
            expect(put(`${dir}$x.txt`)).toBe("'C:\\Users\\ana\\$x.txt' ");
            expect(put(`${dir}\`date\`.txt`)).toBe("'C:\\Users\\ana\\`date`.txt' ");
            expect(put(`${dir}100% a&b;c hi!.txt`)).toBe("'C:\\Users\\ana\\100% a&b;c hi!.txt' ");
            expect(put(windows)).toBe("'C:\\Users\\Ана Ли\\Desktop\\1.txt' ");
            expect(put('\\\\server\\share\\a.txt')).toBe("'\\\\server\\share\\a.txt' ");
        });

        it('on Windows go in double quotes where the name holds a quote, typographic ones among them, and nothing double quotes expand', () => {
            expect(put(`${dir}it's.txt`)).toBe(`"C:\\Users\\ana\\it's.txt" `);
            expect(put('C:\\Ана\u2019s \u2018q\u2019 \u201az\u201b.txt')).toBe(
                '"C:\\Ана\u2019s \u2018q\u2019 \u201az\u201b.txt" ',
            );
        });

        it('on Windows keep the quotes doubled as PowerShell reads them for a quote with a UNC path, a `$` or a backtick, which Bash reads wrong', () => {
            expect(put("\\\\server\\share\\it's.txt")).toBe("'\\\\server\\share\\it''s.txt' ");
            expect(put(`${dir}$x it's.txt`)).toBe("'C:\\Users\\ana\\$x it''s.txt' ");
            expect(put(`${dir}\`d\` it\u2019s.txt`)).toBe("'C:\\Users\\ana\\`d` it\u2019\u2019s.txt' ");
        });

        it('on macOS and Linux keep every other sign as it is, a trailing space and a name in another script among them', () => {
            expect(put('/Users/ana/$x `date` 100% a&b;c.txt')).toBe("'/Users/ana/$x `date` 100% a&b;c.txt' ");
            expect(put("/Users/ana/it's here.txt")).toBe("'/Users/ana/it'\\''s here.txt' ");
            expect(put('/Users/ana/Ана Ли/note ')).toBe("'/Users/ana/Ана Ли/note ' ");
            expect(put('/Users/ana/\u2019q.txt')).toBe("'/Users/ana/\u2019q.txt' ");
        });
    });

    describe('a notice and an aside', () => {
        const open = (chat: Chat) =>
            chat.entries.flatMap((each) =>
                (each.kind === 'notice' || each.kind === 'aside') && each.closed !== true ? [each.text] : [],
            );
        /** A turn that said a notice and an aside, ended, and the person's next message. */
        const next = () => {
            const first = received(
                received(received(said(newChat('conv_1', 'claude'), 'Go'), { kind: 'notice', text: 'Fell back.' }), {
                    kind: 'text',
                    text: 'Done.',
                }),
                { kind: 'notice', text: 'Kept seven days: /d', folder: 'ID-1788' },
            );
            const asided = {
                ...first,
                entries: [...first.entries, { kind: 'aside' as const, text: 'Written: /h/a.md' }],
            };
            return said(received(asided, { kind: 'done', error: null }), 'And now?');
        };

        it('stay through their turn and until the next answer begins, then go', () => {
            const chat = next();
            expect(open(chat)).toEqual(['Fell back.', 'Kept seven days: /d', 'Written: /h/a.md']);
            expect(open(received(chat, { kind: 'text', text: 'Here.' }))).toEqual([]);
            expect(open(received(chat, { kind: 'step', text: 'Read a file' }))).toEqual([]);
        });

        it('said in the turn that runs, stay while its answer goes on', () => {
            const chat = received(next(), { kind: 'notice', text: 'This turn runs in Ask first.' });
            expect(
                open(received(received(chat, { kind: 'text', text: 'Here.' }), { kind: 'done', error: null })),
            ).toEqual(['This turn runs in Ask first.']);
        });

        it('go at the end of the next turn even where it gave no answer', () => {
            expect(open(received(next(), { kind: 'done', error: 'The host stopped.' }))).toEqual([]);
        });

        it('go as well when the next turn ends with no end from the desktop: refused, or failed to start', () => {
            expect(open(failed(next(), 'The desktop could not start the turn.'))).toEqual([]);
        });

        it('go when an export is refused, by the send or by its answer, which says its own refusal beside them', () => {
            const exporting = { ...next(), exporting: '/export' };
            expect(open(failed(exporting, 'Not written.'))).toEqual(['Not written.']);
            expect(open(received(exporting, { kind: 'done', error: 'Not written.' }))).toEqual(['Not written.']);
        });

        it('are closed one by one by their close control, and nothing else is', () => {
            const chat = next();
            const at = chat.entries.findIndex((each) => each.kind === 'aside');
            expect(open(noteClosed(chat, at))).toEqual(['Fell back.', 'Kept seven days: /d']);
            const text = chat.entries.findIndex((each) => each.kind === 'text');
            expect(noteClosed(chat, text)).toBe(chat);
            expect(noteClosed(chat, chat.entries.length + 3)).toBe(chat);
        });

        it('still lock the choice before them once closed', () => {
            const asked = received(said(newChat('conv_1', 'claude'), 'Go'), { kind: 'text', text: 'Pick one.' });
            const told = received(received(asked, { kind: 'done', error: null }), { kind: 'notice', text: 'Gone.' });
            const closed = noteClosed(told, told.entries.length - 1);
            expect(choiceState(closed, 1, ['A'], null, true)).toEqual({ state: 'locked' });
        });
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
            mode: 'ask_first',
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
                mode: 'ask_first',
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
                chosen: null,
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

describe('the Feed as the page keeps it', () => {
    const line = (n: number, fields: Partial<DesktopLineEntry> = {}): DesktopLineEntry => ({
        kind: 'line',
        journal_kind: 'inbound_reply_observed',
        journal_id: `jrn_${n}`,
        at: '2026-11-11T10:00:00.000Z',
        campaign_id: 'cmp_1',
        count: 1,
        ...fields,
    });
    const feedRecord = {
        id: 'conv_feed',
        campaignId: null,
        main: true as const,
        title: 'Feed',
        updatedAt: '',
        running: false,
        mode: 'ask_first' as const,
        entries: [{ kind: 'user' as const, text: 'Hi' }, line(1), { kind: 'gap' as const }],
        dropped: 0,
        chosen: null,
    };

    it('opens with its lines and gaps drawn as kept, and is the main chat', () => {
        const feed = fromRecord(feedRecord, 'claude');
        expect(feed.main).toBe(true);
        expect(feed.entries).toEqual([{ kind: 'user', text: 'Hi' }, { kind: 'line', line: line(1) }, { kind: 'gap' }]);
        expect(newChat('c1', 'claude').main).toBe(false);
    });

    it('takes a line at its end, or in place of its last line when the desktop folded it', () => {
        let feed = fromRecord({ ...feedRecord, entries: [] }, 'claude');
        feed = received(feed, { kind: 'line', line: line(1), folded: false });
        feed = received(feed, { kind: 'line', line: line(2, { count: 2 }), folded: true });
        feed = received(feed, { kind: 'gap' });
        feed = received(feed, { kind: 'line', line: line(3), folded: false });
        expect(feed.entries).toEqual([
            { kind: 'line', line: line(2, { count: 2 }) },
            { kind: 'gap' },
            { kind: 'line', line: line(3) },
        ]);
        // A fold the page cannot place, after a page-only entry, goes at the end.
        feed = received(
            { ...feed, entries: [...feed.entries, { kind: 'afresh' }] },
            { kind: 'line', line: line(4), folded: true },
        );
        expect(feed.entries.at(-1)).toEqual({ kind: 'line', line: line(4) });
    });

    it('never starts a turn for a line: nothing is sent, the waiting message waits, and the turn that runs goes on', () => {
        const idle = received(fromRecord({ ...feedRecord, entries: [] }, 'claude'), {
            kind: 'line',
            line: line(1),
            folded: false,
        });
        expect([idle.running, idle.outbox, idle.waiting]).toEqual([false, null, null]);
        const waiting = kept(
            typed(said(fromRecord({ ...feedRecord, entries: [] }, 'claude'), 'What happened?'), 'And then?'),
        );
        const after = received(waiting, { kind: 'line', line: line(2), folded: false });
        expect([after.running, after.outbox, after.waiting]).toEqual([true, null, 'And then?']);
    });

    it('keeps its title when the person writes in it first', () => {
        expect(said(fromRecord({ ...feedRecord, entries: [] }, 'claude'), 'What happened today?').title).toBe('Feed');
    });
});

/** The blank line between messages that wait together, or come back together. */
const blank = String.fromCharCode(10, 10);
/** A chat whose turn runs, with `text` typed into the box. */
const busy = (text: string): Chat => typed(said(newChat('c1', 'claude'), 'Go'), text);
const done = (chat: Chat, error: string | null = null): Chat => received(chat, { kind: 'done', error });

describe('a message typed while a turn runs', () => {
    it('waits, and the box is cleared for the next words', () => {
        const waiting = kept(busy('  And then?  '));
        expect(waiting).toMatchObject({ waiting: 'And then?', draft: '', running: true });
        expect(waiting).not.toHaveProperty('held');
        expect(waiting.entries).toEqual([{ kind: 'user', text: 'Go' }]);
        expect(kept(busy('   '))).toEqual(busy('   '));
        expect(kept(typed(newChat('c1', 'claude'), 'Idle'))).toMatchObject({ waiting: null, draft: 'Idle' });
    });

    it('adds a second message under the one that waits, a blank line between, and they go together', () => {
        const second = kept(typed(kept(busy('First')), '  Second  '));
        expect(second).toMatchObject({ waiting: ['First', 'Second'].join(blank), draft: '' });
        const third = kept(typed(second, 'Third'));
        expect(third.waiting).toBe(['First', 'Second', 'Third'].join(blank));
        // An empty box adds nothing.
        expect(kept(typed(second, ' '))).toEqual(typed(second, ' '));
        const over = done(third);
        expect(over).toMatchObject({ waiting: null, outbox: ['First', 'Second', 'Third'].join(blank) });
    });

    it('adds while a conversation is made as while a turn runs, and waits for nothing on an empty box', () => {
        const idle = typed(newChat('c1', 'claude'), 'First');
        expect(queued(idle)).toMatchObject({ waiting: 'First', draft: '' });
        expect(queued(typed(queued(idle), 'Second')).waiting).toBe(['First', 'Second'].join(blank));
        expect(queued(typed(idle, ''))).toEqual(typed(idle, ''));
    });

    it('is taken back only into an empty box, so nothing typed there is overwritten', () => {
        expect(takenBack(kept(busy('First')))).toMatchObject({ waiting: null, draft: 'First' });
        const second = typed(kept(busy('First')), 'Second');
        expect(takenBack(second)).toEqual(second);
        const both = kept(typed(kept(busy('First')), 'Second'));
        expect(takenBack(both)).toMatchObject({ waiting: null, draft: ['First', 'Second'].join(blank) });
    });

    it('is given back with every word not sent, in the order written, when nothing could take them', () => {
        const second = typed(kept(busy('First')), 'Third');
        expect(everyWordBack(second, 'Zeroth')).toMatchObject({
            waiting: null,
            draft: ['Zeroth', 'First', 'Third'].join(blank),
        });
        expect(everyWordBack(typed(newChat('c1', 'claude'), '  '), 'Only')).toMatchObject({
            draft: 'Only',
            waiting: null,
        });
        const empty = newChat('c1', 'claude');
        expect(everyWordBack(empty)).toEqual(empty);
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

    it('goes back above what is typed when the turn failed while the box held other words', () => {
        const over = done(typed(kept(busy('And then?')), 'More'), 'Not logged in');
        expect(over).toMatchObject({ waiting: null, outbox: null, draft: ['And then?', 'More'].join(blank) });
        expect(over.entries.at(-1)).toEqual({ kind: 'error', text: 'Not logged in' });
    });

    it('is given back the moment the person stops the turn, so the end that follows sends nothing', () => {
        const stopped = stopAsked(kept(busy('And then?')));
        expect(stopped).toMatchObject({ stopping: true, running: true, waiting: null, draft: 'And then?' });
        // While the stop is asked for, nothing waits: the box keeps what is sent.
        expect(kept(typed(stopped, 'More'))).toEqual(typed(stopped, 'More'));
        // The person types again before the stopped turn's end arrives; the end changes nothing in the box.
        const over = done(typed(stopped, 'Something else'));
        expect(over).toMatchObject({ waiting: null, outbox: null, running: false, draft: 'Something else' });
        expect(over.entries).toEqual([{ kind: 'user', text: 'Go' }]);
    });

    it('comes back above what is typed when the person stops the turn with words in the box', () => {
        const stopped = stopAsked(typed(kept(typed(kept(busy('First')), 'Second')), 'Typed'));
        expect(stopped).toMatchObject({
            stopping: true,
            waiting: null,
            draft: ['First', 'Second', 'Typed'].join(blank),
        });
        expect(done(stopped)).toMatchObject({
            waiting: null,
            outbox: null,
            running: false,
            draft: ['First', 'Second', 'Typed'].join(blank),
        });
    });

    it('is given back when the turn it became fails, after a message that waited behind it', () => {
        const resent = (draft: string) => typed(said(newChat('c1', 'claude'), 'And then?', true), draft);
        expect(done(resent(''), 'Too long')).toMatchObject({ draft: 'And then?', waiting: null });
        expect(done(resent('More'), 'Too long')).toMatchObject({
            draft: ['And then?', 'More'].join(blank),
            waiting: null,
        });
        const behind = typed(kept(resent('Third')), 'Fourth');
        expect(done(behind, 'Too long')).toMatchObject({
            draft: ['And then?', 'Third', 'Fourth'].join(blank),
            waiting: null,
        });
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

describe('an /export typed in the chat, which the desktop answers without a turn', () => {
    const planning = (): Chat => ({ ...newChat('c1', 'claude'), mode: 'plan_only', turnHost: 'claude' });
    const planned = (): Chat =>
        received(received(said(planning(), 'plan it'), { kind: 'text', text: 'Read the replies first.' }), {
            kind: 'done',
            error: null,
            plan: true,
        });
    const refusal = 'Give a file name alone, such as notes.md, with no folder in it.';

    it('is no message of the conversation: no You entry and no title, while the desktop answers it', () => {
        const chat = said(newChat('c1', 'claude'), '/export notes.md');
        expect(chat).toMatchObject({ title: 'New chat', entries: [], running: true, exporting: '/export notes.md' });
        // Any other word with a slash is a message as any other, and names the chat.
        expect(said(newChat('c1', 'claude'), '/exports notes.md')).toMatchObject({
            title: '/exports notes.md',
            entries: [{ kind: 'user', text: '/exports notes.md' }],
            exporting: null,
        });
    });

    it('says where it wrote beside the thread, and a plan before it stays live', () => {
        const over = received(
            received(said(planned(), '/export notes.md'), { kind: 'notice', text: 'Written: /h/notes.md' }),
            { kind: 'done', error: null },
        );
        expect(over.entries.slice(-2)).toEqual([
            { kind: 'text', text: 'Read the replies first.', plan: true },
            { kind: 'aside', text: 'Written: /h/notes.md' },
        ]);
        expect(over).toMatchObject({ running: false, exporting: null, draft: '' });
        expect(livePlan(over, 'claude')).toBe(true);
    });

    it('is given back to the box when refused, says why beside the thread, and a plan before it stays live', () => {
        const over = received(said(planned(), '/export ../x.md'), { kind: 'done', error: refusal });
        expect(over.entries.at(-1)).toEqual({ kind: 'aside', text: refusal });
        expect(over).toMatchObject({ running: false, exporting: null, draft: '/export ../x.md' });
        expect(livePlan(over, 'claude')).toBe(true);
        // Above what was typed meanwhile, and once when it was a waiting message joined with another.
        const joinedWords = ['/export a.md', 'And then?'].join(blank);
        const queuedOver = received(typed(said(planned(), joinedWords, true), 'Typed'), {
            kind: 'done',
            error: refusal,
        });
        expect(queuedOver.draft).toBe([joinedWords, 'Typed'].join(blank));
    });

    it('leaves a chat about no campaign named New chat, so its first message names it', () => {
        const refused = received(said(newChat('c1', 'claude'), '/export x.md'), {
            kind: 'done',
            error: 'This conversation has nothing to write yet.',
        });
        expect(refused.title).toBe('New chat');
        expect(said(typed(refused, ''), 'Find leads in Austin').title).toBe('Find leads in Austin');
    });

    it('is an export after any space the desktop takes as one, a line break of Unicode’s included', () => {
        for (const space of [' ', '\t', '\u00a0', '\u0085', '\u2028']) {
            expect(said(newChat('c1', 'claude'), `/export${space}x.md`).exporting).toBe(`/export${space}x.md`);
        }
        expect(said(newChat('c1', 'claude'), '/exportx.md').exporting).toBeNull();
    });

    it('when the desktop refuses the send itself, says why beside the thread, gives the words back, and a plan stays live', () => {
        const over = failed(said(planned(), '/export x.md'), 'The desktop is closing.');
        expect(over.entries.at(-1)).toEqual({ kind: 'aside', text: 'The desktop is closing.' });
        expect(over).toMatchObject({ running: false, exporting: null, outbox: null, draft: '/export x.md' });
        expect(livePlan(over, 'claude')).toBe(true);
        // Once, when it was a waiting message.
        expect(failed(said(planned(), '/export x.md', true), 'No.').draft).toBe('/export x.md');
    });

    it('that the person did not type in the message box is sent as nothing: the chat says so beside it, and a plan stays live', () => {
        const before = planned();
        const over = exportNotTyped(before);
        expect(over.entries).toEqual([...before.entries, { kind: 'aside', text: exportTypeIt }]);
        expect(exportTypeIt).toBe('Type /export in the message box yourself.');
        expect(over).toMatchObject({ running: false, exporting: null, draft: '' });
        expect(livePlan(over, 'claude')).toBe(true);
    });
});

describe('the model and the effort a chat is sent with', () => {
    const every: DesktopEffort[] = ['low', 'medium', 'high', 'xhigh', 'max'];
    const claude: DesktopHost = {
        id: 'claude',
        label: 'Claude Code',
        kind: 'program',
        found: true,
        version: '2.1.300',
        drivesChat: true,
        install: null,
        belowFloor: null,
        fullAccess: null,
        autoEdit: null,
        runs: { window: {}, tab: {} },
        models: [
            { id: 'opus', label: 'Opus', efforts: every },
            { id: 'haiku', label: 'Haiku', efforts: [] },
        ],
        typedModel: true,
        modelsUnread: null,
        ownServers: [],
        runsRoles: null,
    };
    const codex: DesktopHost = {
        ...claude,
        id: 'codex',
        label: 'Codex',
        models: [{ id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low', 'high'] }],
        typedModel: false,
    };

    it("draws the mode the next turn runs in as the desktop hands it for the host and the place, and the record's mode where it hands nothing", () => {
        const chat = { ...newChat('conv_1', 'codex'), mode: 'full_access' as const };
        const handed: DesktopHost = {
            ...codex,
            runs: {
                window: { full_access: { mode: 'ask_first', why: 'Full access runs on Claude Code only.' } },
                tab: { full_access: { mode: 'ask_first', why: 'From a tab.' } },
            },
        };
        expect(drawnMode(chat, handed, 'window')).toEqual({
            mode: 'ask_first',
            why: 'Full access runs on Claude Code only.',
        });
        expect(drawnMode(chat, handed, 'tab')).toEqual({ mode: 'ask_first', why: 'From a tab.' });
        expect(drawnMode({ ...chat, mode: 'plan_only' }, handed, 'window')).toEqual({ mode: 'plan_only', why: null });
        expect(drawnMode(chat, codex, 'window')).toEqual({ mode: 'full_access', why: null });
        expect(drawnMode(chat, null, 'window')).toEqual({ mode: 'full_access', why: null });
        // A host an earlier desktop listed hands nothing: the record's mode is drawn, as before.
        const [older] = withModels([{ ...handed, runs: undefined } as unknown as DesktopHost]);
        expect(older!.runs).toEqual({ window: {}, tab: {} });
        expect(drawnMode(chat, older!, 'window')).toEqual({ mode: 'full_access', why: null });
    });
    const fresh = newChat('conv_1', 'claude');

    it('names no model and no effort until one is chosen, so the host keeps its own', () => {
        expect(fresh).toMatchObject({ model: null, effort: null, typing: false });
        expect(turnOptions(fresh, claude)).toEqual({ host: 'claude' });
    });

    it('keeps a chat on the alias it chose before names were shown: the id is what is sent, the menu shows its display name', () => {
        const named: DesktopHost = { ...claude, models: [{ id: 'opus', label: 'Opus (latest)', efforts: every }] };
        const before = { ...fresh, model: 'opus', effort: 'high' as const };
        expect(modelView(before, named).offer?.label).toBe('Opus (latest)');
        expect(turnOptions(before, named)).toEqual({ host: 'claude', model: 'opus', effort: 'high' });
    });

    it('names the model chosen, and the effort only where that model takes it', () => {
        const opus = effortChosen(modelChosen(fresh, claude, 'opus'), 'xhigh');
        expect(turnOptions(opus, claude)).toEqual({ host: 'claude', model: 'opus', effort: 'xhigh' });
        // A model that takes none drops the effort chosen before, and sends none.
        const haiku = modelChosen(opus, claude, 'haiku');
        expect(haiku.effort).toBeNull();
        expect(turnOptions(effortChosen(haiku, 'low'), claude)).toEqual({ host: 'claude', model: 'haiku' });
        // The host's own model again: neither is named.
        expect(turnOptions(modelChosen(opus, claude, ''), claude)).toEqual({ host: 'claude' });
        expect(turnOptions(effortChosen(opus, ''), claude)).toEqual({ host: 'claude', model: 'opus' });
    });

    it('keeps an effort across models that both take it, and drops one the next model does not take', () => {
        const opus = effortChosen(modelChosen(fresh, claude, 'opus'), 'high');
        expect(turnOptions(modelChosen(opus, codex, 'gpt-6.1-sol'), codex)).toEqual({
            host: 'codex',
            model: 'gpt-6.1-sol',
            effort: 'high',
        });
        const max = effortChosen(modelChosen(fresh, claude, 'opus'), 'max');
        expect(modelChosen(max, codex, 'gpt-6.1-sol').effort).toBeNull();
    });

    it('takes a full model name typed on a host that takes one, trimmed, and never with an effort', () => {
        const typing = modelChosen(fresh, claude, anotherModel);
        expect(typing).toMatchObject({ typing: true, model: null, effort: null });
        expect(turnOptions(typing, claude)).toEqual({ host: 'claude' });
        const named = effortChosen(modelTyped(typing, '  claude-opus-5-5 '), 'high');
        expect(turnOptions(named, claude)).toEqual({ host: 'claude', model: 'claude-opus-5-5' });
        expect(modelView(named, claude)).toEqual({ offer: null, typing: true, kept: null });
        // A typed name that is one of the host's own models is that model, with its efforts.
        expect(modelView(modelTyped(typing, 'opus'), claude).offer?.id).toBe('opus');
        // On a host that takes no typed name, a model it no longer offers is shown as kept and still sent.
        const gone = { ...fresh, host: 'codex', model: 'gpt-5-old' };
        expect(modelView(gone, codex)).toEqual({ offer: null, typing: false, kept: 'gpt-5-old' });
        // The desktop takes no model its host no longer offers: the turn goes on the host's own, with no effort.
        expect(turnOptions({ ...gone, effort: 'high' }, codex)).toEqual({ host: 'codex' });
    });

    it('holds the send while another model is chosen and its name is empty', () => {
        const typing = modelChosen(fresh, claude, anotherModel);
        expect(sendHeld(typing, claude)).toBe(true);
        expect(sendHeld(modelTyped(typing, '   '), claude)).toBe(true);
        expect(sendHeld(modelTyped(typing, 'claude-opus-5-5'), claude)).toBe(false);
        expect(sendHeld(fresh, claude)).toBe(false);
        expect(sendHeld(modelChosen(fresh, claude, 'opus'), claude)).toBe(false);
    });

    it('starts again from the host\u2019s own model when the host changes', () => {
        const opus = effortChosen(modelChosen(fresh, claude, 'opus'), 'high');
        expect(hostChosen(opus, 'codex')).toMatchObject({ host: 'codex', model: null, effort: null, typing: false });
    });

    it('never sends a model to a host other than the one it was chosen on, when the turn falls to another host', () => {
        const onCodex = effortChosen(modelChosen({ ...fresh, host: 'codex' }, codex, 'gpt-6.1-sol'), 'high');
        // Codex is gone, and the next turn runs on Claude Code: it names none of Codex's choices.
        expect(turnOptions(onHost(onCodex, 'claude'), claude)).toEqual({ host: 'claude' });
        expect(modelView(onHost(onCodex, 'claude'), claude)).toEqual({ offer: null, typing: false, kept: null });
        expect(onHost(onCodex, 'codex')).toBe(onCodex);
        // A chat that named no host yet takes the one its turn runs on, and a model chosen there sticks.
        const chosen = modelChosen(onHost(newChat('conv_2', null), 'claude'), claude, 'opus');
        expect(chosen).toMatchObject({ host: 'claude', model: 'opus' });
        expect(turnOptions(onHost(chosen, 'claude'), claude)).toEqual({ host: 'claude', model: 'opus' });
    });

    it('opens a kept conversation on the host, model and effort its last turn was sent with', () => {
        const record = {
            id: 'conv_1',
            campaignId: null,
            title: 'New chat',
            updatedAt: '2026-11-11T10:00:00.000Z',
            running: false,
            mode: 'ask_first' as const,
            dropped: 0,
            entries: [],
        };
        expect(
            fromRecord({ ...record, chosen: { host: 'codex', model: 'gpt-6.1-sol', effort: 'high' } }, null),
        ).toMatchObject({
            host: 'codex',
            model: 'gpt-6.1-sol',
            effort: 'high',
        });
        expect(fromRecord({ ...record, chosen: null }, 'claude')).toMatchObject({
            host: 'claude',
            model: null,
            effort: null,
        });
    });
});

describe('the hosts as a desktop of another version answers them', () => {
    it('reads a host listed without models as one that offers none and takes no typed name', () => {
        const older = {
            id: 'claude',
            label: 'Claude Code',
            kind: 'program',
            found: true,
            version: '2.1.300',
            drivesChat: true,
            install: null,
            belowFloor: null,
        } as unknown as DesktopHost;
        const [read] = withModels([older]);
        expect(read).toMatchObject({ id: 'claude', models: [], typedModel: false, modelsUnread: null });
        expect(turnOptions(modelChosen(newChat('conv_1', 'claude'), read!, 'opus'), read!)).toEqual({
            host: 'claude',
        });
        const current: DesktopHost = {
            ...older,
            fullAccess: null,
            autoEdit: null,
            runs: { window: {}, tab: {} },
            models: [{ id: 'opus', label: 'Opus', efforts: ['low'] }],
            typedModel: true,
            modelsUnread: null,
        };
        expect(withModels([current])[0]).toEqual(current);
    });

    it('reads a host listed without Full access as one on which Full access cannot be chosen, saying why', () => {
        const older = {
            id: 'claude',
            label: 'Claude Code',
            models: [],
            typedModel: true,
            modelsUnread: null,
        } as unknown as DesktopHost;
        expect(withModels([older])[0]?.fullAccess).toBe(olderDesktop);
    });

    it('reads a host listed without Auto-edit as one on which Auto-edit cannot be chosen, keeping what else it lists', () => {
        const listed = {
            id: 'claude',
            label: 'Claude Code',
            fullAccess: null,
            models: [],
            typedModel: true,
            modelsUnread: null,
        } as unknown as DesktopHost;
        const [read] = withModels([listed]);
        expect(read?.autoEdit).toBe('This version of Jason Desktop does not offer Auto-edit.');
        expect(read?.autoEdit).toBe(olderDesktopAutoEdit);
        expect(read?.fullAccess).toBeNull();
        expect(read?.typedModel).toBe(true);
        expect(modeChoices(read!).find((each) => each.mode === 'auto_edit')?.disabled).toBe(olderDesktopAutoEdit);
    });
});

describe("a chat's access mode", () => {
    const claude = {
        id: 'claude',
        label: 'Claude Code',
        fullAccess: null,
        autoEdit: null,
        runs: { window: {}, tab: {} },
    } as unknown as DesktopHost;
    const codexAutoEdit =
        'Auto-edit runs on Claude Code only: Codex cannot ask before it acts, so on Codex the chat only reads and proposes.';
    const codex = {
        id: 'codex',
        label: 'Codex',
        fullAccess: null,
        autoEdit: codexAutoEdit,
        runs: { window: { auto_edit: { mode: 'ask_first', why: codexAutoEdit } }, tab: {} },
    } as unknown as DesktopHost;

    it('is Ask first in a new chat, and is what the desktop says it is, never what the page asked for', () => {
        const chat = newChat('conv_1', 'claude');
        expect(chat.mode).toBe('ask_first');
        expect(received(chat, { kind: 'mode', mode: 'full_access' }).mode).toBe('full_access');
        expect(
            received(received(chat, { kind: 'mode', mode: 'full_access' }), { kind: 'mode', mode: 'ask_first' }).mode,
        ).toBe('ask_first');
        expect(received(chat, { kind: 'mode', mode: 'plan_only' }).entries).toEqual([]);
    });

    it('opens a kept conversation in the mode the desktop holds, and one an older desktop kept in Ask first', () => {
        const record = {
            id: 'conv_1',
            campaignId: null,
            title: 'New chat',
            updatedAt: '',
            running: false,
            dropped: 0,
            entries: [],
            chosen: null,
        };
        expect(fromRecord({ ...record, mode: 'plan_only' }, 'claude').mode).toBe('plan_only');
        expect(fromRecord(record as unknown as DesktopConversationRecord, 'claude').mode).toBe('ask_first');
    });

    it("offers the four modes, narrow to wide with Full access last, each disabled with the desktop's reason", () => {
        expect(modeChoices(claude)).toEqual([
            { mode: 'ask_first', label: 'Ask first', disabled: null },
            { mode: 'plan_only', label: 'Plan only', disabled: null },
            { mode: 'auto_edit', label: 'Auto-edit', disabled: null },
            { mode: 'full_access', label: 'Full access', disabled: null },
        ]);
        // Codex offers Full access, as Claude Code does; with no host nothing is said against it.
        expect(modeChoices(codex)[3]).toEqual({ mode: 'full_access', label: 'Full access', disabled: null });
        expect(modeChoices(null)[3]?.disabled).toBeNull();
    });

    it('stops a running turn for a choice narrower than the mode that turn runs in, not the mode the chat keeps: the checked mode stops nothing', () => {
        const running = { ...newChat('conv_1', 'codex'), mode: 'auto_edit' as const, running: true };
        // On Codex Auto-edit runs in Ask first: the checked Ask first changes nothing the turn may do.
        expect(choiceStops(running, 'ask_first', codex, 'window')).toBe(false);
        expect(choiceStops(running, 'plan_only', codex, 'window')).toBe(true);
        // On Claude Code the turn runs in Auto-edit, so Ask first is narrower.
        expect(choiceStops(running, 'ask_first', claude, 'window')).toBe(true);
        // Codex runs Full access as itself: leaving it stops the turn.
        expect(choiceStops({ ...running, mode: 'full_access' }, 'ask_first', codex, 'window')).toBe(true);
        expect(choiceStops({ ...running, mode: 'full_access' }, 'full_access', codex, 'window')).toBe(false);
        // A chat with no turn running stops nothing.
        expect(choiceStops({ ...running, running: false }, 'plan_only', codex, 'window')).toBe(false);
    });

    it("disables Auto-edit on Codex and with no host in the desktop's words, and nowhere else, the Feed included", () => {
        expect(modeChoices(codex)[2]).toEqual({ mode: 'auto_edit', label: 'Auto-edit', disabled: codexAutoEdit });
        expect(modeChoices(null)[2]?.disabled).toBe(codexAutoEdit);
        // A Claude Code below Full access's floor still offers Auto-edit: it needs no hook.
        const below = { ...claude, fullAccess: 'Full access needs a newer Claude Code.' } as DesktopHost;
        expect(modeChoices(below)[2]?.disabled).toBeNull();
        expect(modeChoices(below)[3]?.disabled).toBe('Full access needs a newer Claude Code.');
    });

    it('says Auto-edit is on in its own line under the composer', () => {
        expect(autoEditLine).toBe(
            "Auto-edit · the AI creates, moves, copies and removes files in the chat's own folder without asking; everything else is as in Ask first",
        );
    });

    it('says Full access is on in the line under the composer, from the mode the desktop holds alone', () => {
        expect(fullAccessLine).toBe(
            "Full access · the AI runs commands without asking, except those that name Jason's own settings, data or variables",
        );
    });
});

describe('Plan only’s plan card, as the page keeps it', () => {
    const planning = (fields: Partial<Chat> = {}): Chat => ({
        ...newChat('c1', 'claude'),
        mode: 'plan_only',
        turnHost: 'claude',
        ...fields,
    });
    /** A Plan only turn that ended well with these words last, as the desktop says it. */
    const planned = (chat: Chat, words: string): Chat =>
        received(received(said(chat, 'plan it'), { kind: 'text', text: words }), {
            kind: 'done',
            error: null,
            plan: true,
        });
    const marks = (chat: Chat) =>
        chat.entries.flatMap((entry, at) => (entry.kind === 'text' && entry.plan === true ? [at] : []));

    it('marks the turn’s last words a plan only when the desktop says so, and live while nothing came after them', () => {
        const chat = planned(planning(), 'Read the replies first.');
        expect(chat.entries.at(-1)).toEqual({ kind: 'text', text: 'Read the replies first.', plan: true });
        expect(livePlan(chat, 'claude')).toBe(true);
        // A turn's end without the mark marks nothing: an older desktop, or a turn that was no plan.
        const plain = received(received(said(planning(), 'hi'), { kind: 'text', text: 'Hi' }), {
            kind: 'done',
            error: null,
        });
        expect(marks(plain)).toEqual([]);
        expect(livePlan(plain, 'claude')).toBe(false);
        // Nor for the Feed, whatever the desktop says.
        const feed = planned(planning({ main: true }), 'A plan');
        expect(marks(feed)).toEqual([]);
        expect(livePlan(feed, 'claude')).toBe(false);
        // Nor where the turn’s last thing was no words.
        const stepped = received(received(said(planning(), 'go'), { kind: 'step', text: 'Read a file' }), {
            kind: 'done',
            error: null,
            plan: true,
        });
        expect(marks(stepped)).toEqual([]);
    });

    it('is a plan of the past once a later turn ended, anything followed it, a turn runs, words wait, or the mode is not Plan only', () => {
        const first = planned(planning(), 'One');
        const second = planned(first, 'Two');
        expect(marks(second)).toEqual([second.entries.length - 1]);
        const failedLater = received(said(first, 'again'), { kind: 'done', error: 'It broke.' });
        expect(marks(failedLater)).toEqual([]);

        const chat = planned(planning(), 'Plan');
        expect(livePlan(told(chat, 'A refusal.'), 'claude')).toBe(false);
        expect(livePlan(afresh(chat), 'claude')).toBe(false);
        expect(livePlan(said(chat, 'Something else'), 'claude')).toBe(false);
        expect(livePlan({ ...chat, running: true }, 'claude')).toBe(false);
        expect(livePlan({ ...chat, waiting: 'later' }, 'claude')).toBe(false);
        expect(livePlan({ ...chat, plan: 'kept' }, 'claude')).toBe(false);
        expect(livePlan({ ...chat, plan: 'approving' }, 'claude')).toBe(false);
        // Leaving Plan only ends the card, as the desktop ends it: back in Plan only it is no card.
        const left = moded(chat, 'ask_first');
        expect(marks(left)).toEqual([]);
        expect(livePlan(moded(left, 'plan_only'), 'claude')).toBe(false);
        expect(marks(received(chat, { kind: 'mode', mode: 'full_access' }))).toEqual([]);
    });

    it('keeps an approved card, inert, through the change to Ask first its approval made, until the go’s turn ends', () => {
        const chat = planned(planning(), 'Plan');
        const approving = received({ ...chat, plan: 'approving' }, { kind: 'mode', mode: 'ask_first' });
        expect(marks(approving)).toEqual([approving.entries.length - 1]);
        const approved = moded({ ...approving, plan: 'approved' }, 'ask_first');
        expect(livePlan(approved, 'claude')).toBe(false);
        const going = said(approved, planGo);
        const over = received(received(going, { kind: 'text', text: 'Done.' }), { kind: 'done', error: null });
        expect(marks(over)).toEqual([]);
        expect(over.plan).toBeNull();
    });

    it('reads the mark from a kept conversation, so a reopened chat still in Plan only shows its card', () => {
        const record: DesktopConversationRecord = {
            id: 'conv_1',
            campaignId: null,
            title: 'Plans',
            updatedAt: '2026-11-13T10:00:00.000Z',
            running: false,
            mode: 'plan_only',
            dropped: 0,
            entries: [
                { kind: 'user', text: 'Plan it' },
                { kind: 'text', text: 'The plan', plan: true },
            ],
            chosen: { host: 'claude', model: null, effort: null },
        };
        const chat = fromRecord(record, 'claude');
        expect(chat.entries.at(-1)).toEqual({ kind: 'text', text: 'The plan', plan: true });
        expect(chat.plan).toBeNull();
        expect(livePlan(chat, 'claude')).toBe(true);
        expect(livePlan(fromRecord({ ...record, mode: 'ask_first' }, 'claude'), 'claude')).toBe(false);
        // The plan's host is the one the record says the last turn went to.
        expect(fromRecord(record, 'codex').turnHost).toBe('claude');
        expect(livePlan(fromRecord({ ...record, chosen: null }, 'claude'), 'claude')).toBe(false);
    });

    it('is the plan of the host that made it alone: on another host it is no card to approve, until the chat is back on it', () => {
        const chat = planned(planning(), 'Plan');
        expect(livePlan(chat, 'claude')).toBe(true);
        expect(planElsewhere(chat, 'claude')).toBe(false);
        expect(livePlan(chat, 'codex')).toBe(false);
        expect(planElsewhere(chat, 'codex')).toBe(true);
        // No host found is neither: there is nothing to send the go to.
        expect(livePlan(chat, null)).toBe(false);
        expect(planElsewhere(chat, null)).toBe(false);
        // A plan of the past is no plan elsewhere either.
        expect(planElsewhere({ ...chat, plan: 'kept' }, 'codex')).toBe(false);
        expect(planElsewhere(said(chat, 'More'), 'codex')).toBe(false);
        expect(planOtherHost).toBe(
            'This plan came from another AI; the one this chat is on now never saw it. Ask for the plan again to approve it.',
        );
    });

    it('says its sentences as ruled', () => {
        expect(planGo).toBe('Go ahead with the plan in your last answer.');
        expect(planLine).toBe(
            'Approve and run leaves Plan only for Ask first and tells the AI to go ahead; whatever Ask first asks about still comes to you as a card.',
        );
        expect(codexPlanLine).toBe(
            'On Codex the approved plan still only reads: Codex runs Ask first read-only. To have it carried out, choose Full access or Claude Code for this chat.',
        );
        expect(planApproved).toBe('Approved: the chat is in Ask first.');
        expect(planModeMoved).toBe("The chat's mode changed before the plan was approved, so nothing was sent.");
    });
});

describe('a choice the AI asked', () => {
    const at = '2026-10-16T09:05:00.000Z';
    const labels = ['From me', 'From someone else on my team'];
    const asked = (campaign?: { id: string; name: string }): Chat => {
        const chat = received(said(newChat('c1', 'claude', campaign), 'Set it up'), {
            kind: 'text',
            text: 'Who should the emails come from?',
            at,
        });
        return received(chat, { kind: 'done', error: null });
    };
    const clear = { at, read: 'clear' as const };
    const state = (chat: Chat, acts: Parameters<typeof choiceState>[3] = null, maySend = true) =>
        choiceState(chat, 1, labels, acts, maySend);
    const after = (chat: Chat, entry: Chat['entries'][number]): Chat => ({
        ...chat,
        entries: [...chat.entries, entry],
    });
    const record = (entries: DesktopConversationRecord['entries']): DesktopConversationRecord => ({
        id: 'conv_1',
        campaignId: null,
        title: 'Set up',
        updatedAt: at,
        running: false,
        mode: 'ask_first',
        dropped: 0,
        entries,
        chosen: null,
    });
    const line: DesktopLineEntry = {
        kind: 'line',
        journal_kind: 'campaign_paused',
        journal_id: 'jrn_1',
        at,
        campaign_id: 'cmp_1',
        count: 1,
    };

    it('keeps the moment the desktop kept its words, live and from a record', () => {
        expect(asked().entries[1]).toEqual({ kind: 'text', text: 'Who should the emails come from?', at });
        const chat = fromRecord(
            record([
                { kind: 'user', text: 'Set it up' },
                { kind: 'text', text: 'Who?', at },
                { kind: 'text', text: 'Earlier desktop' },
            ]),
            'claude',
        );
        expect(chat.entries[1]).toEqual({ kind: 'text', text: 'Who?', at });
        expect(chat.entries[2]).toEqual({ kind: 'text', text: 'Earlier desktop' });
        // A plan's mark put on and taken off leaves the moment as it was.
        const marked = received(
            { ...chat, entries: chat.entries.slice(0, 2), mode: 'plan_only' },
            { kind: 'done', error: null, plan: true },
        );
        expect(marked.entries[1]).toEqual({ kind: 'text', text: 'Who?', at, plan: true });
        expect(moded(marked, 'ask_first').entries[1]).toEqual({ kind: 'text', text: 'Who?', at });
    });

    it('is live while nothing follows it, no turn runs, no message waits and the chat may send', () => {
        expect(state(asked())).toEqual({ state: 'live' });
        expect(state({ ...asked(), running: true })).toEqual({ state: 'idle' });
        expect(state({ ...asked(), waiting: 'Later' })).toEqual({ state: 'idle' });
        expect(state(asked(), null, false)).toEqual({ state: 'idle' });
    });

    it('is answered by the next message: the label it equals once both are trimmed, else the person’s own words', () => {
        expect(state(said(asked(), '  From me '))).toEqual({ state: 'chosen', label: 'From me' });
        expect(state(said(asked(), 'from me'))).toEqual({ state: 'own_words' });
        expect(state(said(asked(), 'Both of us, alternately'))).toEqual({ state: 'own_words' });
    });

    it.each<Chat['entries'][number]>([
        { kind: 'text', text: 'One more thing.' },
        { kind: 'step', step: { text: 'Ran a jason command' } },
        { kind: 'permission', request: 'r', title: 'Run', detail: 'x', remember: null, answer: null },
        { kind: 'error', text: 'It stopped.' },
        { kind: 'notice', text: 'A folder was left out.', folder: null },
        { kind: 'attached', title: 'Spring' },
        { kind: 'fresh', reason: 'not_found' },
        { kind: 'afresh' },
    ])('is locked with no mark by a later $kind', (entry) => {
        expect(state(after(asked(), entry))).toEqual({ state: 'locked' });
        // The first locking entry decides: a message after it marks nothing.
        expect(state(said(after(asked(), entry), 'From me'))).toEqual({ state: 'locked' });
    });

    it.each<Chat['entries'][number]>([
        { kind: 'line', line },
        { kind: 'gap' },
        { kind: 'pick', campaignIds: ['cmp_1', 'cmp_2'] },
    ])('is not locked by a later $kind', (entry) => {
        expect(state(after(asked(), entry))).toEqual({ state: 'live' });
        expect(state(said(after(asked(), entry), 'From me'))).toEqual({ state: 'chosen', label: 'From me' });
    });

    it('is drawn locked once the chat is opened again, as the desktop keeps fewer entries than lock it, and answered as it was', () => {
        const asking: DesktopConversationRecord['entries'] = [
            { kind: 'user', text: 'Set it up' },
            { kind: 'text', text: 'Who?', at },
        ];
        // A notice, a campaign attached or a session started fresh locked it; the record keeps none of them.
        expect(state(fromRecord(record(asking), 'claude'))).toEqual({ state: 'reopened' });
        expect(state(fromRecord({ ...record(asking), campaignId: 'cmp_1' }, 'claude'), clear)).toEqual({
            state: 'reopened',
        });
        expect(state(after(asked(), { kind: 'afresh' }))).toEqual({ state: 'locked' });
        // A choice asked after the chat was opened again is live as any other.
        const later = received(said(fromRecord(record(asking), 'claude'), 'And?'), { kind: 'text', text: 'Who?', at });
        expect(choiceState(received(later, { kind: 'done', error: null }), 3, labels, null, true)).toEqual({
            state: 'live',
        });
        expect(state(fromRecord(record([...asking, { kind: 'user', text: 'From me' }]), 'claude'))).toEqual({
            state: 'chosen',
            label: 'From me',
        });
        expect(state(fromRecord(record([...asking, { kind: 'error', text: 'Stopped.' }]), 'claude'))).toEqual({
            state: 'locked',
        });
    });

    describe('in an agent’s chat', () => {
        const spring = { id: 'cmp_1', name: 'Spring' };

        it('is live only once the person’s acts since its moment were read and there were none', () => {
            expect(state(asked(spring), clear)).toEqual({ state: 'live' });
            expect(state(asked(spring), { at, read: 'reading' })).toEqual({ state: 'idle' });
            expect(state(asked(spring), null)).toEqual({ state: 'idle' });
            // A read made for another moment says nothing of this one.
            expect(state(asked(spring), { at: '2026-10-16T09:00:00.000Z', read: 'clear' })).toEqual({ state: 'idle' });
        });

        it('is locked by a person’s act, and drawn locked when the read failed or its words have no moment', () => {
            expect(state(asked(spring), { at, read: 'acted' })).toEqual({ state: 'acted' });
            expect(state(asked(spring), { at, read: 'unread' })).toEqual({ state: 'unread' });
            const earlier = asked(spring);
            const words: Chat['entries'][number] = { kind: 'text', text: 'Who should the emails come from?' };
            expect(state({ ...earlier, entries: [earlier.entries[0]!, words] }, clear)).toEqual({ state: 'unread' });
        });

        it('takes a later entry first: an answer stays an answer whatever was acted after it', () => {
            expect(state(said(asked(spring), 'From me'), { at, read: 'acted' })).toEqual({
                state: 'chosen',
                label: 'From me',
            });
        });

        it('reads no act in a chat of no agent, where only later entries lock', () => {
            expect(state(asked(), { at, read: 'acted' })).toEqual({ state: 'live' });
            expect(state({ ...asked(spring), main: true }, null)).toEqual({ state: 'live' });
        });
    });
});

describe('a person’s act on an agent, as a journal line', () => {
    const since = '2026-10-16T09:05:00.000Z';
    const line = (
        fields: Partial<{ kind: string; campaign_id: string | null; ts: string; actor: { type: string } }>,
    ) => ({
        kind: 'campaign_paused',
        campaign_id: 'cmp_1',
        ts: '2026-10-16T09:07:00.000Z',
        actor: { type: 'human' },
        ...fields,
    });

    it('is not a line with no actor, which is skipped and throws nothing', () => {
        const bare: Record<string, unknown> = { ...line({}) };
        delete bare.actor;
        expect(personActed(bare as never, 'cmp_1', since)).toBe(false);
        expect(personActed({ ...bare, actor: null } as never, 'cmp_1', since)).toBe(false);
    });

    it.each(actKinds)('is %s with a person as its actor', (kind) => {
        expect(personActed(line({ kind }), 'cmp_1', since)).toBe(true);
    });

    it.each(['role', 'attempt', 'system'])('is not a line whose actor is a %s', (type) => {
        expect(personActed(line({ actor: { type } }), 'cmp_1', since)).toBe(false);
    });

    it('is not another campaign’s, another kind, or a line before the moment; one at the moment is, as since keeps it', () => {
        expect(personActed(line({ campaign_id: 'cmp_2' }), 'cmp_1', since)).toBe(false);
        expect(personActed(line({ campaign_id: null }), 'cmp_1', since)).toBe(false);
        expect(personActed(line({ kind: 'campaign_created' }), 'cmp_1', since)).toBe(false);
        expect(personActed(line({ ts: '2026-10-16T09:04:59.999Z' }), 'cmp_1', since)).toBe(false);
        expect(personActed(line({ ts: since }), 'cmp_1', since)).toBe(true);
    });
});

describe('the unseen characters', () => {
    const desktop = (file: string, declaration: RegExp) =>
        readFileSync(join(import.meta.dirname, '..', '..', '..', 'desktop', 'src', file), 'utf8').match(
            declaration,
        )?.[1];

    it('are the pattern the desktop drops from a title, in its header and in its naming', () => {
        const header = desktop('chat-header.ts', /export const unseen = (\/.*\/gu);/);
        const naming = desktop('chat-naming.ts', /const unseen = (\/.*\/gu);/);
        expect(header).toBeDefined();
        expect(unseen.toString()).toBe(header);
        expect(unseen.toString()).toBe(naming);
    });
});
