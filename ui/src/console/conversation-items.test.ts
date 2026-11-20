import { describe, expect, it } from 'vitest';
import { conversationItems, rowItems, type ConversationFacts, type Gates, type MenuItem } from './conversation-items';

const kept: ConversationFacts = { kept: 'conv_1', id: 'conv_1', running: false, removing: false };
const everything: Gates = { create: true, rename: true, restart: true, remove: true, tab: false };

/** Each entry by its word, a held one marked, a separator as a dash. */
const said = (items: MenuItem[]) =>
    items.map((each) =>
        each.kind === 'separator' ? '-' : 'disabled' in each && each.disabled ? `${each.label} (held)` : each.label,
    );

describe('the menu on a row of Recent', () => {
    it('offers an agent the move its status allows, Archive…, and the conversation’s acts, Remove last', () => {
        const rest = ['-', 'Rename conversation', 'Start conversation fresh', '-', 'Remove conversation'];
        expect(said(rowItems({ status: 'active' }, kept, everything))).toEqual(['Open', 'Pause', 'Archive…', ...rest]);
        expect(said(rowItems({ status: 'paused' }, kept, everything))).toEqual(['Open', 'Resume', 'Archive…', ...rest]);
        expect(said(rowItems({ status: 'draft' }, kept, everything))).toEqual(['Open', 'Start', 'Archive…', ...rest]);
    });

    it('offers an archived agent neither a move nor Archive…', () => {
        expect(said(rowItems({ status: 'archived' }, kept, everything))).toEqual([
            'Open',
            '-',
            'Rename conversation',
            'Start conversation fresh',
            '-',
            'Remove conversation',
        ]);
    });

    it('offers a chat about no agent, and an agent not read yet, Open and the conversation’s acts alone', () => {
        const alone = ['Open', '-', 'Rename conversation', 'Start conversation fresh', '-', 'Remove conversation'];
        expect(said(rowItems(null, kept, everything))).toEqual(alone);
    });

    it('in a tab the desktop opened leaves out Start fresh and Remove, and a separator with nothing after it', () => {
        expect(said(rowItems({ status: 'active' }, kept, { ...everything, tab: true }))).toEqual([
            'Open',
            'Pause',
            'Archive…',
            '-',
            'Rename conversation',
        ]);
    });

    it('holds Start fresh and Remove while the conversation’s turn runs or its removal is asked, and Rename while it is removed', () => {
        expect(said(rowItems(null, { ...kept, running: true }, everything))).toEqual([
            'Open',
            '-',
            'Rename conversation',
            'Start conversation fresh (held)',
            '-',
            'Remove conversation (held)',
        ]);
        expect(said(rowItems(null, { ...kept, removing: true }, everything))).toEqual([
            'Open',
            '-',
            'Rename conversation (held)',
            'Start conversation fresh (held)',
            '-',
            'Remove conversation (held)',
        ]);
    });

    it('leaves out each act its place does not allow, and every one where the place makes no chat', () => {
        expect(said(rowItems(null, kept, { ...everything, rename: false }))).toEqual([
            'Open',
            '-',
            'Start conversation fresh',
            '-',
            'Remove conversation',
        ]);
        expect(said(rowItems(null, kept, { ...everything, restart: false }))).toEqual([
            'Open',
            '-',
            'Rename conversation',
            '-',
            'Remove conversation',
        ]);
        expect(said(rowItems(null, kept, { ...everything, remove: false }))).toEqual([
            'Open',
            '-',
            'Rename conversation',
            'Start conversation fresh',
        ]);
        expect(said(rowItems({ status: 'paused' }, kept, { ...everything, create: false }))).toEqual([
            'Open',
            'Resume',
            'Archive…',
        ]);
    });
});

describe('the conversation’s acts', () => {
    it('are the agent’s “…” as it always held them, Rename waiting for a kept conversation', () => {
        expect(said(conversationItems(kept, everything))).toEqual([
            'Rename conversation',
            'Start conversation fresh',
            'Remove conversation',
        ]);
        const unkept = conversationItems(
            { kept: undefined, id: 'unmade:1', running: false, removing: false },
            everything,
        );
        expect(said(unkept)).toEqual([
            'Rename conversation (held)',
            'Start conversation fresh (held)',
            'Remove conversation (held)',
        ]);
        expect(unkept[0]).toMatchObject({ title: 'Send a message first; then the chat can be renamed' });
        expect(conversationItems(kept, { ...everything, create: false })).toEqual([]);
    });
});
