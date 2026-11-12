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
    fullAccessLine,
    modeChoices,
    olderDesktop,
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
        models: [
            { id: 'opus', label: 'Opus', efforts: every },
            { id: 'haiku', label: 'Haiku', efforts: [] },
        ],
        typedModel: true,
        modelsUnread: null,
    };
    const codex: DesktopHost = {
        ...claude,
        id: 'codex',
        label: 'Codex',
        models: [{ id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', efforts: ['low', 'high'] }],
        typedModel: false,
    };
    const fresh = newChat('conv_1', 'claude');

    it('names no model and no effort until one is chosen, so the host keeps its own', () => {
        expect(fresh).toMatchObject({ model: null, effort: null, typing: false });
        expect(turnOptions(fresh, claude)).toEqual({ host: 'claude' });
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
});

describe("a chat's access mode", () => {
    const claude = { id: 'claude', label: 'Claude Code', fullAccess: null } as unknown as DesktopHost;
    const codex = {
        id: 'codex',
        label: 'Codex',
        fullAccess: 'Full access runs on Claude Code only.',
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

    it("offers the three modes, Full access disabled with the desktop's reason on a host that has none", () => {
        expect(modeChoices(claude)).toEqual([
            { mode: 'ask_first', label: 'Ask first', disabled: null },
            { mode: 'plan_only', label: 'Plan only', disabled: null },
            { mode: 'full_access', label: 'Full access', disabled: null },
        ]);
        expect(modeChoices(codex)[2]).toEqual({
            mode: 'full_access',
            label: 'Full access',
            disabled: 'Full access runs on Claude Code only.',
        });
        expect(modeChoices(null)[2]?.disabled).toBe('Full access runs on Claude Code only.');
        expect(modeChoices(claude).map((each) => each.mode)).not.toContain('auto_edit');
    });

    it('says Full access is on in the line under the composer, from the mode the desktop holds alone', () => {
        expect(fullAccessLine).toBe(
            'Full access: the agent runs commands without asking, except those Jason cannot read.',
        );
    });
});
